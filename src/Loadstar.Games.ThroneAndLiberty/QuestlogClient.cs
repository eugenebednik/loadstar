using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Loadstar.Core.Model;

namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// Reads build data from questlog.gg's tRPC endpoints.
///
/// The API is public but undocumented and unversioned, so every call is wrapped, cached, and
/// has a manual-paste fallback in the UI. We request rarely and cache indefinitely on purpose:
/// a build doesn't change between sessions, and hammering someone else's site to re-fetch
/// static data is how third-party tools get blocked.
/// </summary>
public sealed partial class QuestlogClient
{
    private const string Base = "https://questlog.gg/throne-and-liberty/api/trpc/";

    private readonly HttpClient _http;

    public QuestlogClient(HttpClient http)
    {
        _http = http;

        // Cloudflare fronts the site and rejects requests with no meaningful User-Agent.
        // Identifying the tool honestly also gives them someone to contact if it misbehaves.
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _http.DefaultRequestHeaders.Add(
                "User-Agent",
                "Loadstar/0.1 (+https://github.com/eugenebednik/loadstar)");
        }
    }

    /// <summary>
    /// Extracts the build slug from a questlog URL. The slug is the LAST path segment —
    /// the segment before it is the author's profile id and is not what the API wants.
    /// Accepts a bare slug too, so users can paste either.
    /// </summary>
    public static string? ExtractSlug(string urlOrSlug)
    {
        if (string.IsNullOrWhiteSpace(urlOrSlug))
        {
            return null;
        }

        var trimmed = urlOrSlug.Trim();

        if (!trimmed.Contains('/', StringComparison.Ordinal))
        {
            return SlugPattern().IsMatch(trimmed) ? trimmed : null;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var last = uri.Segments[^1].Trim('/');
        return SlugPattern().IsMatch(last) ? last : null;
    }

    /// <summary>
    /// Fetches a character and its loadouts.
    ///
    /// <para><c>slug</c> is the only parameter, and it is the last path segment of a build URL.
    /// Both URL forms occur — <c>/character-builder/{userSlug}/{buildSlug}</c> and
    /// <c>/character-builder/{buildSlug}</c> — so taking the last segment is correct for both.</para>
    ///
    /// <para>The response shape is the trap: <c>builds</c> is a SIBLING of <c>character</c>, not
    /// a field inside it. Reading <c>character</c> and expecting gear there returns nothing and
    /// fails silently.</para>
    /// </summary>
    public async Task<CharacterBuilds?> GetCharacterAsync(string urlOrSlug, CancellationToken cancellationToken)
    {
        var slug = ExtractSlug(urlOrSlug)
            ?? throw new ArgumentException($"Not a recognisable questlog build URL or slug: '{urlOrSlug}'", nameof(urlOrSlug));

        var input = JsonSerializer.Serialize(new { slug });
        var requestUri = $"{Base}characterBuilder.getCharacter?input={Uri.EscapeDataString(input)}";

        using var response = await _http.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<JsonElement>(cancellationToken)
            .ConfigureAwait(false);

        if (!payload.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("data", out var data))
        {
            return null;
        }

        // tRPC returns HTTP 200 with {"status":"NOT_FOUND"} rather than a 404.
        if (data.TryGetProperty("status", out var status) &&
            status.GetString() == "NOT_FOUND")
        {
            return null;
        }

        if (!data.TryGetProperty("character", out var character))
        {
            return null;
        }

        // `builds` sits alongside `character`, NOT inside it. A character holds several
        // loadouts (six on the reference build), each with its own equipment, target
        // attributes and weapon pair — so the caller has to pick one rather than assume.
        var builds = new List<TargetBuild>();

        if (data.TryGetProperty("builds", out var buildArray) &&
            buildArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var build in buildArray.EnumerateArray())
            {
                builds.Add(BuildMapper.Map(build, slug));
            }
        }

        return new CharacterBuilds
        {
            Slug = slug,
            Name = character.TryGetProperty("name", out var n) ? n.GetString() ?? slug : slug,
            Level = character.TryGetProperty("level", out var l) && l.TryGetInt32(out var lv) ? lv : null,
            Tags = character.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array
                ? t.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => x.Length > 0).ToArray()
                : [],
            Builds = builds,
        };
    }

    /// <summary>
    /// The live server list, grouped by region.
    ///
    /// <para>Fetched rather than hardcoded because servers are added and merged over time, and a
    /// stale built-in list would quietly offer the player a server that no longer exists. Boss
    /// spawn times differ by region, so knowing which region a chosen server belongs to is what
    /// makes the countdown correct rather than plausible.</para>
    ///
    /// <para>Takes no parameters — unusual for this API, but that is the real signature.</para>
    /// </summary>
    public async Task<IReadOnlyList<GameServer>> GetServersAsync(CancellationToken cancellationToken)
    {
        using var response = await _http
            .GetAsync($"{Base}serverStatus.getServerStatus", cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<JsonElement>(cancellationToken)
            .ConfigureAwait(false);

        if (!payload.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var servers = new List<GameServer>();

        foreach (var entry in data.EnumerateArray())
        {
            var name = entry.TryGetProperty("serverName", out var n) ? n.GetString() : null;
            var region = entry.TryGetProperty("regionSlug", out var r) ? r.GetString() : null;

            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(region))
            {
                servers.Add(new GameServer(
                    name,
                    region,
                    entry.TryGetProperty("status", out var s) ? s.GetString() ?? "unknown" : "unknown"));
            }
        }

        return servers;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{4,64}$")]
    private static partial Regex SlugPattern();
}
