using System.Reflection;
using System.Text.Json;

namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// Computes world boss and dynamic-event spawn times locally.
///
/// <para>There is no spawn API to poll — Amazon never exposed one, which is why every third-party
/// timer computes the same way. The calendar is a fixed weekly grid, so region + server timezone +
/// wall-clock slot is enough. That makes this offline, rate-limit-free, and immune to a third-party
/// site changing its markup.</para>
///
/// <para>Times are stored as <b>server-local wall clock</b> and converted at read time, never as
/// precomputed UTC instants. Storing instants would silently shift every entry twice a year when
/// daylight saving moves.</para>
/// </summary>
public sealed class BossSchedule
{
    private readonly IReadOnlyDictionary<string, RegionSchedule> _regions;

    public int ResetHourLocal { get; }

    public string? CapturedAt { get; }

    public string? GamePatch { get; }

    private BossSchedule(
        IReadOnlyDictionary<string, RegionSchedule> regions,
        int resetHourLocal,
        string? capturedAt,
        string? gamePatch)
    {
        _regions = regions;
        ResetHourLocal = resetHourLocal;
        CapturedAt = capturedAt;
        GamePatch = gamePatch;
    }

    /// <summary>Region slugs that actually carry slot data. Regions present but empty are excluded.</summary>
    public IReadOnlyList<string> PopulatedRegions =>
        _regions.Where(r => !r.Value.IsEmpty).Select(r => r.Key).OrderBy(r => r).ToArray();

    public bool HasSchedule(string regionSlug) =>
        _regions.TryGetValue(Normalise(regionSlug), out var region) && !region.IsEmpty;

    public static BossSchedule LoadBundled()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("boss-schedule.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("boss-schedule.json is not embedded in the assembly.");

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);

        return Parse(reader.ReadToEnd());
    }

    public static BossSchedule Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var regions = new Dictionary<string, RegionSchedule>(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("regions", out var regionsElement) &&
            regionsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in regionsElement.EnumerateObject())
            {
                var timeZone = entry.Value.TryGetProperty("defaultTimeZone", out var tz)
                    ? tz.GetString()
                    : null;

                regions[Normalise(entry.Name)] = new RegionSchedule(
                    ReadWeeklySlots(entry.Value),
                    timeZone);
            }
        }

        return new BossSchedule(
            regions,
            root.TryGetProperty("resetHourLocal", out var reset) && reset.TryGetInt32(out var hour) ? hour : 3,
            root.TryGetProperty("capturedAt", out var captured) ? captured.GetString() : null,
            root.TryGetProperty("gamePatch", out var patch) ? patch.GetString() : null);
    }

    /// <summary>
    /// Reads a region's slots, per weekday.
    ///
    /// <para><c>weeklySlots</c> wins when present, because the schedule genuinely differs by day —
    /// a live client shows Thursday and Monday empty and Sunday running siege at a time no other day
    /// uses. <c>dailySlots</c> is the older flat form and is expanded across all seven days only as a
    /// fallback; it cannot express an empty day, which is why it was wrong.</para>
    /// </summary>
    private static IReadOnlyDictionary<DayOfWeek, IReadOnlyList<ScheduleSlot>> ReadWeeklySlots(JsonElement region)
    {
        var week = new Dictionary<DayOfWeek, IReadOnlyList<ScheduleSlot>>();

        if (region.TryGetProperty("weeklySlots", out var weekly) && weekly.ValueKind == JsonValueKind.Object)
        {
            foreach (var day in weekly.EnumerateObject())
            {
                if (Enum.TryParse<DayOfWeek>(day.Name, ignoreCase: true, out var parsedDay))
                {
                    week[parsedDay] = ReadSlots(day.Value);
                }
            }

            return week;
        }

        if (region.TryGetProperty("dailySlots", out var daily))
        {
            var slots = ReadSlots(daily);

            foreach (var day in Enum.GetValues<DayOfWeek>())
            {
                week[day] = slots;
            }
        }

        return week;
    }

    private static IReadOnlyList<ScheduleSlot> ReadSlots(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var slots = new List<ScheduleSlot>();

        foreach (var slot in array.EnumerateArray())
        {
            var time = slot.TryGetProperty("time", out var t) ? t.GetString() : null;
            var type = slot.TryGetProperty("type", out var ty) ? ty.GetString() : null;

            if (TimeSpan.TryParse(time, out var parsed))
            {
                slots.Add(new ScheduleSlot(parsed, type ?? "Unknown"));
            }
        }

        return slots.OrderBy(s => s.TimeOfDay).ToArray();
    }

    /// <summary>
    /// The suggested timezone for a region. A starting point the user overrides — servers within a
    /// region do not all share one, so this must never be treated as a fact about their server.
    /// </summary>
    public string? DefaultTimeZone(string regionSlug) =>
        _regions.TryGetValue(Normalise(regionSlug), out var region) ? region.DefaultTimeZone : null;

    /// <summary>
    /// The next <paramref name="count"/> spawns after <paramref name="now"/>.
    ///
    /// <para>Returns empty rather than guessing when the region has no captured slot table — Europe
    /// and Asia are not filled in yet, and inventing times for them would be worse than showing
    /// nothing.</para>
    /// </summary>
    public IReadOnlyList<BossSpawn> NextSpawns(
        DateTimeOffset now,
        string regionSlug,
        TimeZoneInfo serverZone,
        int count = 3)
    {
        ArgumentNullException.ThrowIfNull(serverZone);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        if (!_regions.TryGetValue(Normalise(regionSlug), out var region) || region.IsEmpty)
        {
            return [];
        }

        var localNow = TimeZoneInfo.ConvertTime(now, serverZone);
        var spawns = new List<BossSpawn>();

        // Walk from yesterday so a slot that is "today" in server time but still ahead of the
        // player's instant is not skipped near a timezone boundary.
        for (var dayOffset = -1; dayOffset <= 8 && spawns.Count < count; dayOffset++)
        {
            var date = localNow.Date.AddDays(dayOffset);

            // Empty days are real — Thursday and Monday carry nothing at all — so this legitimately
            // yields no slots rather than falling back to some default set.
            foreach (var slot in region.SlotsFor(date.DayOfWeek))
            {
                var instant = ToInstant(date, slot.TimeOfDay, serverZone);

                if (instant is not { } spawnAt || spawnAt <= now)
                {
                    continue;
                }

                spawns.Add(new BossSpawn(spawnAt, slot.EventType, spawnAt - now));

                if (spawns.Count >= count)
                {
                    break;
                }
            }
        }

        return spawns.OrderBy(s => s.SpawnsAt).Take(count).ToArray();
    }

    /// <summary>
    /// Converts a server-local wall-clock time to an instant, accounting for daylight saving.
    ///
    /// <para>Returns null for a time that does not exist — the hour skipped when clocks spring
    /// forward. Ambiguous times, when clocks fall back and the hour repeats, resolve to the offset
    /// <see cref="TimeZoneInfo.GetUtcOffset(DateTime)"/> reports, which is deterministic.</para>
    /// </summary>
    private static DateTimeOffset? ToInstant(DateTime date, TimeSpan timeOfDay, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(date.Date.Add(timeOfDay), DateTimeKind.Unspecified);

        if (zone.IsInvalidTime(local))
        {
            return null;
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    /// <summary>
    /// Normalises region names so questlog's slugs and the schedule file's prettier names match.
    /// The live server list says <c>japan-oceania</c>; the bundled file says <c>Asia</c>.
    /// </summary>
    private static string Normalise(string region)
    {
        var trimmed = region.Trim().ToLowerInvariant();

        return trimmed switch
        {
            "japan-oceania" or "japan" or "oceania" or "apac" => "asia",
            "na" or "north-america" or "us" => "americas",
            "eu" => "europe",
            _ => trimmed,
        };
    }

    private sealed record RegionSchedule(
        IReadOnlyDictionary<DayOfWeek, IReadOnlyList<ScheduleSlot>> Week,
        string? DefaultTimeZone)
    {
        public bool IsEmpty => Week.Count == 0 || Week.Values.All(slots => slots.Count == 0);

        public IReadOnlyList<ScheduleSlot> SlotsFor(DayOfWeek day) =>
            Week.TryGetValue(day, out var slots) ? slots : [];
    }

    private sealed record ScheduleSlot(TimeSpan TimeOfDay, string EventType);
}

public sealed record BossSpawn(DateTimeOffset SpawnsAt, string EventType, TimeSpan Until)
{
    public bool IsFieldBoss => EventType.Equals("FieldBosses", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Scheduled guild PvP, not a boss. Sunday's single slot is siege, and labelling it as a boss
    /// would send players somewhere that does not exist.
    /// </summary>
    public bool IsSiege => EventType.Equals("Siege", StringComparison.OrdinalIgnoreCase);

    /// <summary>Short label for the overlay, e.g. "Field Bosses".</summary>
    public string DisplayName => EventType switch
    {
        "FieldBosses" => "Field Bosses",
        "DynamicEvents" => "Dynamic Events",
        "Siege" => "Siege",
        _ => EventType,
    };

    /// <summary>Compact countdown, e.g. <c>1:23:45</c> or <c>18m 04s</c>.</summary>
    public string Countdown(TimeSpan remaining) => remaining switch
    {
        { TotalSeconds: <= 0 } => "now",
        { TotalHours: >= 1 } => $"{(int)remaining.TotalHours}h {remaining.Minutes:00}m",
        { TotalMinutes: >= 1 } => $"{remaining.Minutes}m {remaining.Seconds:00}s",
        _ => $"{remaining.Seconds}s",
    };
}
