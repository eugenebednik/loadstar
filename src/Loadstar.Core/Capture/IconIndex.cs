using System.Text.Json;

namespace Loadstar.Core.Capture;

/// <summary>
/// Maps icon hashes to names, built once from a screen where the game labels its own icons.
///
/// <para>Throne and Liberty's map Content Settings window lists every boss beside its icon <em>with
/// the name in text</em>, so one user-initiated capture yields a complete lookup. After that,
/// resolving a boss icon on the schedule is arithmetic rather than a guess.</para>
///
/// <para><b>Two captures, two sizes.</b> Content Settings and the schedule are separate windows and
/// cannot be seen together, so the index is built from one capture and matched against another that
/// draws the same sprites smaller. Scale tolerance is therefore the mechanism, not a nicety.</para>
///
/// <para><b>An ambiguous match returns nothing.</b> The roster runs to ~38 bosses, most existing in
/// near-identical normal and "Ascended" forms, so the realistic failure is two entries landing
/// within a few bits of each other. Naming one of them would be confidently wrong, which is the
/// outcome this whole approach exists to avoid.</para>
/// </summary>
public sealed class IconIndex
{
    /// <summary>
    /// Maximum differing bits still considered the same icon, out of <see cref="IconHash.Bits"/>
    /// (256).
    ///
    /// <para><b>Measured, not guessed.</b> Across a set of synthetic icons, rescaling the same
    /// sprite from 64px to 32px moved the hash by <b>4–14 bits</b>, while genuinely different icons
    /// sat <b>23–71 bits</b> apart. 20 clears the worst observed rescale drift and stays under the
    /// closest observed pair. An earlier value of 32 would have matched two different icons.</para>
    ///
    /// <para>The gap between those bands is only nine bits, which is narrower than is comfortable —
    /// so this must be re-measured against real game icons cropped from an actual Content Settings
    /// capture before the feature is trusted. Real sprites should separate better than synthetic
    /// ones built from a shared template, but that is a prediction, not a result.</para>
    /// </summary>
    public const int DefaultTolerance = 20;

    /// <summary>
    /// A match must beat the runner-up by at least this many bits. This is the real protection
    /// against the Ascended-variant problem: even inside tolerance, a near-tie resolves to nothing
    /// rather than to whichever entry happened to be enumerated first.
    /// </summary>
    public const int DefaultMargin = 8;

    private readonly List<IconEntry> _entries = [];

    public IReadOnlyList<IconEntry> Entries => _entries;

    public int Count => _entries.Count;

    /// <summary>When the index was built, so a stale one can be rebuilt after a patch adds bosses.</summary>
    public DateTimeOffset? BuiltAt { get; set; }

    /// <summary>Game version the index was built against, if known.</summary>
    public string? GameVersion { get; set; }

    public void Add(string name, IconHash hash, string? category = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _entries.Add(new IconEntry(name.Trim(), hash, category));
    }

    /// <summary>
    /// Resolves a hash to a name, or null when nothing matches confidently.
    ///
    /// <para>Null is a real answer here, and callers must render it as "unidentified" rather than
    /// falling back to a nearest guess.</para>
    /// </summary>
    public IconMatch? Match(IconHash hash, int tolerance = DefaultTolerance, int margin = DefaultMargin)
    {
        if (_entries.Count == 0)
        {
            return null;
        }

        IconEntry? best = null;
        IconEntry? runnerUp = null;
        var bestDistance = int.MaxValue;
        var runnerUpDistance = int.MaxValue;

        foreach (var entry in _entries)
        {
            var distance = hash.DistanceTo(entry.Hash);

            if (distance < bestDistance)
            {
                runnerUp = best;
                runnerUpDistance = bestDistance;
                best = entry;
                bestDistance = distance;
            }
            else if (distance < runnerUpDistance)
            {
                runnerUp = entry;
                runnerUpDistance = distance;
            }
        }

        if (best is null || bestDistance > tolerance)
        {
            return null;
        }

        // Two entries near-equally close means the index cannot tell them apart. Say so.
        if (runnerUp is not null && runnerUpDistance - bestDistance < margin)
        {
            return null;
        }

        return new IconMatch(best.Name, bestDistance, best.Category);
    }

    /// <summary>
    /// Entries whose hashes are too close to distinguish, found at build time.
    ///
    /// <para>Worth surfacing when the index is created rather than discovering it later as silent
    /// nulls: a collision usually means the crop rectangle is too loose and is catching more
    /// background than icon.</para>
    /// </summary>
    public IReadOnlyList<(string First, string Second, int Distance)> FindCollisions(int margin = DefaultMargin)
    {
        var collisions = new List<(string, string, int)>();

        for (var i = 0; i < _entries.Count; i++)
        {
            for (var j = i + 1; j < _entries.Count; j++)
            {
                var distance = _entries[i].Hash.DistanceTo(_entries[j].Hash);

                if (distance < margin)
                {
                    collisions.Add((_entries[i].Name, _entries[j].Name, distance));
                }
            }
        }

        return collisions;
    }

    public string ToJson() => JsonSerializer.Serialize(
        new IndexFile(BuiltAt, GameVersion, _entries),
        new JsonSerializerOptions { WriteIndented = true });

    public static IconIndex FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var file = JsonSerializer.Deserialize<IndexFile>(json)
            ?? throw new InvalidOperationException("Icon index file was empty.");

        var index = new IconIndex { BuiltAt = file.BuiltAt, GameVersion = file.GameVersion };

        foreach (var entry in file.Entries ?? [])
        {
            index.Add(entry.Name, entry.Hash, entry.Category);
        }

        return index;
    }

    private sealed record IndexFile(
        DateTimeOffset? BuiltAt,
        string? GameVersion,
        IReadOnlyList<IconEntry>? Entries);
}

public sealed record IconEntry(string Name, IconHash Hash, string? Category);

public sealed record IconMatch(string Name, int Distance, string? Category)
{
    /// <summary>A near-exact hit, as opposed to one that merely scraped past tolerance.</summary>
    public bool IsStrong => Distance <= 12;
}
