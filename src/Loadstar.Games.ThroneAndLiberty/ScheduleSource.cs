using System.Text.Json;

using Loadstar.Core.Diagnostics;

namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// Where the boss schedule comes from: a published copy, a local cache of it, or the bundled fallback.
///
/// <para>The schedule is data that changes on the game's cadence, not the app's. Shipping it inside
/// the binary means a rotation change needs a release, a CI run and every user reinstalling — for a
/// three-line edit. So it is published as a plain JSON file and fetched.</para>
///
/// <para><b>The bundled copy is never removed.</b> This is the first thing in Loadstar that depends
/// on something staying up, and <c>boss-schedule.json</c>'s own comments give the reason it was local
/// to begin with: no runtime dependency on a third party. The fallback is what makes fetching
/// acceptable rather than a regression — offline, rate-limited, or repository-gone all degrade to the
/// schedule that shipped, which is stale but correct-shaped and honest about its capture date.</para>
/// </summary>
public static class ScheduleSource
{
    /// <summary>
    /// The published schedule, served by the project's GitHub Pages site.
    ///
    /// <para>The file lives in <c>docs/</c> and is embedded into the assembly FROM there, so this URL
    /// and the bundled fallback are the same bytes from the same source file. Publishing a schedule
    /// change is a commit to <c>docs/boss-schedule.json</c> and nothing else — no release, no
    /// reinstall.</para>
    /// </summary>
    public const string PublishedUrl = "https://eugenebednik.github.io/loadstar/boss-schedule.json";

    private const string CacheFileName = "boss-schedule.cache.json";

    /// <summary>
    /// Loads the best schedule available without touching the network: the cached download if it is
    /// present and parses, otherwise the bundled copy.
    ///
    /// <para>Startup never waits on a fetch. A tray app that blocks on an HTTP request before showing
    /// a countdown is worse than one showing a slightly old countdown immediately.</para>
    /// </summary>
    public static BossSchedule Load(string directory)
    {
        var cached = CachePath(directory);

        try
        {
            if (File.Exists(cached))
            {
                var schedule = Validate(BossSchedule.Parse(File.ReadAllText(cached)));

                if (schedule is not null)
                {
                    Log.Info($"Boss schedule: using cached download from {File.GetLastWriteTime(cached):yyyy-MM-dd}.");
                    return schedule;
                }

                // A cache that no longer validates is worse than no cache — delete it so the next
                // refresh starts clean rather than re-reading the same bad file every launch.
                Log.Warn("Boss schedule: cached copy failed validation, discarding it.");
                File.Delete(cached);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error("Boss schedule: could not read the cache", ex);
        }

        return BossSchedule.LoadBundled();
    }

    /// <summary>
    /// Fetches the published schedule and caches it. Returns null when nothing better than what the
    /// caller already has could be obtained — offline, a bad response, or a payload that does not
    /// validate.
    ///
    /// <para>Validation is the point of this method. A truncated download or an HTML error page parses
    /// as "no regions", and adopting that would leave the player with a timer showing nothing while
    /// the bundled data sat right there. Nothing replaces a working schedule unless it is demonstrably
    /// a schedule.</para>
    /// </summary>
    public static async Task<BossSchedule?> RefreshAsync(
        string directory,
        HttpClient http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);

        try
        {
            var json = await http.GetStringAsync(PublishedUrl, cancellationToken).ConfigureAwait(false);
            var schedule = Validate(BossSchedule.Parse(json));

            if (schedule is null)
            {
                Log.Warn($"Boss schedule: {PublishedUrl} returned {json.Length} bytes that are not a usable schedule.");
                return null;
            }

            // Written only after validation, so the cache can never hold something Load would reject.
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(CachePath(directory), json, cancellationToken).ConfigureAwait(false);

            Log.Info($"Boss schedule: refreshed, {schedule.PopulatedRegions.Count} region(s) with data.");
            return schedule;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or IOException or UnauthorizedAccessException or JsonException)
        {
            // Being offline is normal for a desktop app and is not an error worth showing anyone.
            Log.Warn($"Boss schedule: refresh failed ({ex.GetType().Name}: {ex.Message}). Keeping the current one.");
            return null;
        }
    }

    /// <summary>A schedule counts as usable only if some region actually carries slots.</summary>
    private static BossSchedule? Validate(BossSchedule schedule) =>
        schedule.PopulatedRegions.Count > 0 ? schedule : null;

    private static string CachePath(string directory) => Path.Combine(directory, CacheFileName);
}
