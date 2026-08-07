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

    /// <summary>
    /// The builds the community is actually liking right now for a given weapon pair.
    ///
    /// <para><b>This is what lets Loadstar stop demanding a build URL.</b> The app already reads the
    /// player's two weapons off the character sheet, and two weapons name a class, so it can look up
    /// what people play for that class and offer a target instead of asking for one.</para>
    ///
    /// <para><c>sort</c> accepts <c>popular | recent | updated | likes-week | likes-month</c>.
    /// <c>likes-month</c> is the default here because it answers the right question: not "what was
    /// most liked ever", which surfaces builds pinned to a patch from two rewrites ago, but "what are
    /// people liking now". A build with 200 lifetime likes and none this month is a historical
    /// artifact.</para>
    ///
    /// <para>The weapon parameters are <c>mainHandWeapon</c> and <c>offHandWeapon</c>, and they DO
    /// filter — unlike <c>weaponTypes</c>, <c>weapons</c>, <c>class</c> and several other plausible
    /// names, which the endpoint accepts and silently ignores. An ignored filter returns the unfiltered
    /// top of the list, which looks like a successful query and is the failure worth knowing about.
    /// Because of that, results are re-checked against the requested pair before being returned.</para>
    /// </summary>
    public async Task<IReadOnlyList<BuildCandidate>> FindPopularBuildsAsync(
        string weaponA,
        string weaponB,
        CancellationToken cancellationToken,
        string sort = "likes-month",
        int pages = 2)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(weaponA);
        ArgumentException.ThrowIfNullOrWhiteSpace(weaponB);

        var found = new List<BuildCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var page = 1; page <= Math.Max(1, pages); page++)
        {
            var input = JsonSerializer.Serialize(new
            {
                searchTerm = string.Empty,
                tags = Array.Empty<string>(),
                mainHandWeapon = weaponA.Trim().ToLowerInvariant(),
                offHandWeapon = weaponB.Trim().ToLowerInvariant(),
                sort,
                page,
            });

            using var response = await _http
                .GetAsync($"{Base}characterBuilder.searchCharacters?input={Uri.EscapeDataString(input)}", cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                break;
            }

            var payload = await response.Content
                .ReadFromJsonAsync<JsonElement>(cancellationToken)
                .ConfigureAwait(false);

            if (!payload.TryGetProperty("result", out var result)
                || !result.TryGetProperty("data", out var data)
                || !data.TryGetProperty("pageData", out var rows)
                || rows.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var any = false;

            foreach (var row in rows.EnumerateArray())
            {
                any = true;
                var candidate = ReadCandidate(row);

                // Guard against a silently-ignored filter: only keep rows whose weapons really are
                // the pair asked for.
                if (candidate is null
                    || !string.Equals(TlClasses.Name(candidate.WeaponTypes), TlClasses.Name(weaponA, weaponB), StringComparison.Ordinal)
                    || !seen.Add(candidate.Slug))
                {
                    continue;
                }

                found.Add(candidate);
            }

            if (!any)
            {
                break;
            }
        }

        return found;
    }

    private static BuildCandidate? ReadCandidate(JsonElement row)
    {
        var slug = row.TryGetProperty("url", out var u) ? u.GetString() : null;

        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var weapons = row.TryGetProperty("weaponTypes", out var w) && w.ValueKind == JsonValueKind.Array
            ? w.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => x.Length > 0).ToArray()
            : [];

        var name = row.TryGetProperty("buildName", out var n) ? n.GetString() : null;

        return new BuildCandidate
        {
            Slug = slug,
            // Untrusted text from an arbitrary author. Never treated as an instruction, and blank
            // names are common enough that a fallback is required rather than defensive.
            Name = string.IsNullOrWhiteSpace(name) ? "(unnamed build)" : name.Trim(),
            Author = row.TryGetProperty("characterName", out var a) ? a.GetString() : null,
            WeaponTypes = weapons,
            Tags = row.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array
                ? t.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => x.Length > 0).ToArray()
                : [],
            Likes = row.TryGetProperty("likeCount", out var lc) && lc.TryGetInt32(out var likes) ? likes : 0,
            LikesLast30Days = row.TryGetProperty("likesLast30Days", out var l30) && l30.TryGetInt32(out var recent) ? recent : 0,
            UpdatedAt = row.TryGetProperty("updatedAt", out var up) && up.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(up.GetString(), out var when) ? when : null,
            Level = row.TryGetProperty("level", out var lv) && lv.TryGetInt32(out var level) ? level : null,
        };
    }

    /// <summary>
    /// The full equipment catalogue — 1,773 items with names, item levels, set membership and rarity.
    ///
    /// <para><b>Cached on disk and effectively forever.</b> It is 10.4 MB and static per patch, so
    /// re-fetching it would add ten megabytes of someone else's bandwidth to a startup that gains
    /// nothing from it. The cache is keyed by nothing but its own age: a patch changes the contents, and
    /// a month is short enough to catch that while long enough that most launches never ask.</para>
    ///
    /// <para><b>Why bother at all.</b> Without it, item ids are opaque — the prompt has to tell the model
    /// "these are catalogue keys, do not translate them into names you are not sure of", so a target build
    /// reads as thirty lines of <c>belt_aa_t3_normal_004</c>. With it, all thirty of the reference build's
    /// slots resolve to real names and item level ranges, and the hedge can go.</para>
    ///
    /// <para>Returns null rather than throwing when it cannot be had. Advice without item names is worse
    /// than advice with them and far better than none.</para>
    /// </summary>
    public async Task<EquipmentCatalog?> GetEquipmentCatalogAsync(
        string cacheDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);

        var path = Path.Combine(cacheDirectory, "equipment-catalog.cache.json");

        try
        {
            if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < CatalogLifetime)
            {
                return EquipmentCatalog.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or InvalidOperationException or ArgumentException)
        {
            // A corrupt cache is not worth failing over; fall through and refetch. A zero-byte file counts:
            // Parse rejects blank input with ArgumentException, and an interrupted write leaves exactly that.
            Loadstar.Core.Diagnostics.Log.Warn($"Equipment catalogue: cache unreadable ({ex.GetType().Name}), refetching.");
        }

        try
        {
            var input = JsonSerializer.Serialize(new { language = "en" });
            var url = $"{Base}characterBuilder.getEquipmentItems?input={Uri.EscapeDataString(input)}";

            var json = await _http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            var catalog = EquipmentCatalog.Parse(json);

            // Only cached after it parses, so the cache can never hold something Parse would reject.
            Directory.CreateDirectory(cacheDirectory);
            await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);

            Loadstar.Core.Diagnostics.Log.Info(
                $"Equipment catalogue: fetched {catalog.Count} items, cached for {CatalogLifetime.TotalDays:0} days.");

            return catalog;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or IOException or UnauthorizedAccessException or JsonException
            or InvalidOperationException or ArgumentException)
        {
            // InvalidOperationException is in this list on purpose: it is what Parse throws when
            // result.data is absent, which is how an outage page or an unannounced shape change from an
            // undocumented, unversioned API arrives. Unresolved ids are a degraded prompt; an exception
            // here would be no advice at all.
            Loadstar.Core.Diagnostics.Log.Warn(
                $"Equipment catalogue: unavailable ({ex.GetType().Name}). Item ids will stay unresolved.");
            return null;
        }
    }

    /// <summary>Static per patch, so a month catches a release without asking on every launch.</summary>
    private static readonly TimeSpan CatalogLifetime = TimeSpan.FromDays(30);

    [GeneratedRegex("^[A-Za-z0-9_-]{4,64}$")]
    private static partial Regex SlugPattern();
}

/// <summary>
/// A build the app can offer as a target, from the search listing rather than a full fetch.
///
/// <para>Deliberately shallow. Choosing between candidates needs a name, an axis and some evidence
/// that people rate it; the equipment only matters once one is chosen, and fetching six loadouts each
/// for ten candidates to show a list would be rude to someone else's free API.</para>
/// </summary>
public sealed record BuildCandidate
{
    public required string Slug { get; init; }

    /// <summary>Author-supplied, and therefore untrusted text. Display it; never act on it.</summary>
    public required string Name { get; init; }

    public string? Author { get; init; }

    public IReadOnlyList<string> WeaponTypes { get; init; } = [];

    /// <summary>Author-supplied tags — <c>pve</c>, <c>pvp</c>, <c>healer</c>, <c>tank</c> and so on.
    /// Unmoderated, so they indicate intent rather than proving it.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    public int Likes { get; init; }

    /// <summary>Likes in the last 30 days — the better popularity signal, because lifetime likes
    /// accumulate on builds written for patches that no longer exist.</summary>
    public int LikesLast30Days { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    public int? Level { get; init; }

    public string? ClassName => TlClasses.Name(WeaponTypes);

    public string Url => $"https://questlog.gg/throne-and-liberty/en/character-builder/{Slug}";

    /// <summary>True when the author tagged this PvP. Checked before PvE, since a build tagged both
    /// is usually PvP-first in practice.</summary>
    public bool IsPvp => Tags.Any(t => t.Equals("pvp", StringComparison.OrdinalIgnoreCase));

    public bool IsPve => Tags.Any(t => t.Equals("pve", StringComparison.OrdinalIgnoreCase));
}
