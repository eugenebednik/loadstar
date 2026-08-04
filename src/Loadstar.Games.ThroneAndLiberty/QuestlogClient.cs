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
                "Loadstar/0.1 (+https://github.com/loadstar/loadstar)");
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
    /// Fetches a build. Note the input shape: both <c>slug</c> and <c>url</c> take the same
    /// build slug. Passing the author's profile id in <c>slug</c> returns NOT_FOUND — a trap
    /// worth keeping in a comment, because the field name suggests otherwise.
    /// </summary>
    public async Task<TargetBuild?> GetBuildAsync(string urlOrSlug, CancellationToken cancellationToken)
    {
        var slug = ExtractSlug(urlOrSlug)
            ?? throw new ArgumentException($"Not a recognisable questlog build URL or slug: '{urlOrSlug}'", nameof(urlOrSlug));

        var input = JsonSerializer.Serialize(new { slug, url = slug });
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

        return BuildMapper.Map(character, slug);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{4,64}$")]
    private static partial Regex SlugPattern();
}
