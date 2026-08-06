using System.Reflection;
using System.Text.Json;

namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// Computes world boss and dynamic-event spawn times locally.
///
/// <para>There is no spawn API to poll — Amazon never exposed one, which is why every third-party
/// timer computes the same way. The calendar is a fixed weekly grid, so region + slot table is
/// enough. That makes this offline, rate-limit-free, and immune to a third-party site changing its
/// markup.</para>
///
/// <para><b>The game runs TWO schedules at once</b>, which its map exposes as separate tabs, and a
/// model expressing only one is wrong on most days:</para>
///
/// <list type="bullet">
/// <item>The <b>Daily</b> tab — siege and archbosses, per weekday, with days that carry nothing.
/// Read from <c>weeklySlots</c>.</item>
/// <item>The <b>Hourly</b> tab — the regular field bosses, seven slots <b>every</b> day, composition
/// rotating. Read from <c>hourlySlots</c>.</item>
/// </list>
///
/// <para>Both are live simultaneously, so they are merged into one per-weekday table at parse time
/// and a day with archbosses legitimately yields both. Before the merge, a weekday the Daily tab
/// leaves empty produced a countdown to something two days out — three rows of overlay saying
/// nothing actionable — while seven field bosses were actually spawning that evening.</para>
///
/// <para>See <see cref="TimesAreUtc"/> for how slot times are anchored.</para>
/// </summary>
public sealed class BossSchedule
{
    private readonly IReadOnlyDictionary<string, RegionSchedule> _regions;

    public int ResetHourLocal { get; }

    public string? CapturedAt { get; }

    public string? GamePatch { get; }

    /// <summary>
    /// True when slot times are UTC wall-clock keyed by UTC weekday, which is how they are now stored.
    ///
    /// <para>A boss spawns at ONE instant for the region and every player sees it in their own zone.
    /// Reading slot times in the player's zone made the countdown correct only for players whose
    /// machine happened to match the server — a player on the same Americas server in Eastern time got
    /// a countdown three hours early that looked entirely plausible.</para>
    ///
    /// <para>False keeps the old zone-relative behaviour, so a schedule written before this flag
    /// existed still resolves the way it did. That matters because installs are fetching one.</para>
    /// </summary>
    public bool TimesAreUtc { get; }

    private BossSchedule(
        IReadOnlyDictionary<string, RegionSchedule> regions,
        int resetHourLocal,
        string? capturedAt,
        string? gamePatch,
        bool timesAreUtc)
    {
        _regions = regions;
        ResetHourLocal = resetHourLocal;
        CapturedAt = capturedAt;
        GamePatch = gamePatch;
        TimesAreUtc = timesAreUtc;
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
            root.TryGetProperty("gamePatch", out var patch) ? patch.GetString() : null,
            root.TryGetProperty("timeBasis", out var basis)
                && string.Equals(basis.GetString(), "utc", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reads a region's two slot streams and merges them into one per-weekday table.
    ///
    /// <para><c>weeklySlots</c> is the map's <b>Daily</b> tab — siege and archbosses. It is per
    /// weekday because the schedule genuinely differs by day: a live client shows Thursday and Monday
    /// empty and Sunday running siege at a time no other day uses.</para>
    ///
    /// <para><c>hourlySlots</c> is the map's <b>Hourly</b> tab — the regular field bosses, which run
    /// every single day. It is <b>ADDITIVE</b>, expanded across all seven weekdays and merged on top,
    /// because both tabs are live at once and a day with archbosses shows both.</para>
    ///
    /// <para><c>dailySlots</c> is the older flat form and is a <b>FALLBACK</b> for
    /// <c>weeklySlots</c>, not a third stream — it cannot express an empty day, which is why it was
    /// wrong. Additive versus fallback is the one distinction to keep straight when editing the JSON;
    /// the two names are unhelpfully similar and only <c>hourlySlots</c> stacks.</para>
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
        }
        else if (region.TryGetProperty("dailySlots", out var daily))
        {
            var slots = ReadSlots(daily);

            foreach (var day in Enum.GetValues<DayOfWeek>())
            {
                week[day] = slots;
            }
        }

        if (!region.TryGetProperty("hourlySlots", out var hourly))
        {
            return week;
        }

        var everyDay = ReadSlots(hourly);

        if (everyDay.Count == 0)
        {
            return week;
        }

        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            // Re-sorted, not appended: NextSpawns stops walking as soon as it has enough spawns and
            // therefore depends on each day's slots being in time order. Merging two already-sorted
            // lists without sorting the result would make it return the wrong three.
            //
            // Duplicate times survive deliberately. Sunday 18:00 Pacific is BOTH the weekly siege and
            // an hourly field-boss slot, and collapsing them would hide one real event behind another.
            week[day] = week.TryGetValue(day, out var fromWeekly) && fromWeekly.Count > 0
                ? [.. fromWeekly.Concat(everyDay).OrderBy(s => s.TimeOfDay)]
                : everyDay;
        }

        return week;
    }

    /// <summary>
    /// Reads one entry of a slot's <c>bosses</c> array.
    ///
    /// <para><b>Both forms are accepted.</b> A bare string is a name and nothing else, which keeps
    /// every schedule written before the object form valid — including the ones already published. An
    /// object carries what the client's hover tooltip actually hands over:</para>
    ///
    /// <code>
    /// "bosses": [
    ///   { "name": "Ramux", "mode": "pvp", "zone": "Stillreach", "kind": "archboss", "despawnMinutes": 50 },
    ///   "Talus"
    /// ]
    /// </code>
    ///
    /// <para>Everything except the name is optional, and absent is meaningfully different from wrong:
    /// a missing <c>mode</c> means nobody has read the badge yet, which is the honest state until
    /// someone has.</para>
    /// </summary>
    private static ScheduledBoss? ReadBoss(JsonElement entry)
    {
        if (entry.ValueKind == JsonValueKind.String)
        {
            var bare = entry.GetString();

            return string.IsNullOrWhiteSpace(bare) ? null : new ScheduledBoss { Name = bare.Trim() };
        }

        if (entry.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var name = entry.TryGetProperty("name", out var n) ? n.GetString() : null;

        // A nameless entry is discarded rather than kept as an empty row: the whole point of the list
        // is to say who is spawning, and a blank one renders as a stray comma in the overlay.
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new ScheduledBoss
        {
            Name = name.Trim(),
            Mode = Text(entry, "mode"),
            Zone = Text(entry, "zone"),
            Kind = Text(entry, "kind"),
            DespawnMinutes = entry.TryGetProperty("despawnMinutes", out var d) && d.TryGetInt32(out var m) && m > 0
                ? m
                : null,
        };

        static string? Text(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString())
                    ? value.GetString()!.Trim()
                    : null;
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

            if (!TimeSpan.TryParse(time, out var parsed))
            {
                continue;
            }

            // Optional, and absent means weekly — every existing slot keeps its behaviour untouched.
            var period = slot.TryGetProperty("everyDays", out var e) && e.TryGetInt32(out var days) && days > 0
                ? days
                : 7;

            DateOnly? anchor = slot.TryGetProperty("since", out var since)
                && DateOnly.TryParse(since.GetString(), out var parsedAnchor)
                    ? parsedAnchor
                    : null;

            // A slot can hold SEVERAL bosses at one time — the client shows one icon at 17:00 and
            // five at 20:00 — so this is a list, not a name. Empty or absent means "not yet
            // identified", and the countdown falls back to the event type rather than inventing one.
            var bosses = slot.TryGetProperty("bosses", out var b) && b.ValueKind == JsonValueKind.Array
                ? b.EnumerateArray().Select(ReadBoss).OfType<ScheduledBoss>().ToArray()
                : [];

            // Contest mode on the SLOT, which is a different claim from mode on a boss. The two daily
            // guild slots (18:30 and 21:30 Pacific) are guild PvP at a fixed time while the boss that
            // fills them rotates, so the mode is known and the name is not. Putting it here is what
            // lets the countdown say "Guild Boss" without inventing which boss it is.
            var mode = slot.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString()
                : null;

            slots.Add(new ScheduleSlot(parsed, type ?? "Unknown", period, anchor, bosses, mode));
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

        // UTC data is walked in UTC — dates, weekdays and times all in the same basis. Mixing them is
        // the whole hazard here: 17:00 Pacific Friday is 00:00 UTC Saturday, so reading a UTC time
        // against a local weekday lands a day out while still looking like a plausible countdown.
        var localNow = TimesAreUtc ? now.ToUniversalTime() : TimeZoneInfo.ConvertTime(now, serverZone);
        var spawns = new List<BossSpawn>();

        // Walk from yesterday so a slot that is "today" in server time but still ahead of the
        // player's instant is not skipped near a timezone boundary.
        // Sixteen days, not eight. A biweekly slot can sit up to 13 days out — an off-week Sunday
        // with siege as the only entry produced NOTHING, because the walk ended before the next
        // occurrence. Caught by a test rather than in the field. The bound must exceed the longest
        // recurrence period in the data, so raising a period means raising this too.
        for (var dayOffset = -1; dayOffset <= 16 && spawns.Count < count; dayOffset++)
        {
            var date = localNow.Date.AddDays(dayOffset);

            // Empty days are real — Thursday and Monday carry nothing at all — so this legitimately
            // yields no slots rather than falling back to some default set.
            foreach (var slot in region.SlotsFor(date.DayOfWeek))
            {
                if (!slot.OccursOn(DateOnly.FromDateTime(date)))
                {
                    continue;
                }

                // No zone conversion for UTC data, and no DST question either — a UTC wall clock is
                // already an instant. The zone path stays for pre-timeBasis schedules.
                var instant = TimesAreUtc
                    ? new DateTimeOffset(date.Add(slot.TimeOfDay), TimeSpan.Zero)
                    : ToInstant(date, slot.TimeOfDay, serverZone);

                if (instant is not { } spawnAt || spawnAt <= now)
                {
                    continue;
                }

                spawns.Add(new BossSpawn(spawnAt, slot.EventType, spawnAt - now, slot.Bosses, slot.Mode));

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

    /// <summary>
    /// One scheduled slot.
    ///
    /// <para><paramref name="PeriodDays"/> and <paramref name="Anchor"/> express a recurrence longer
    /// than a week. <b>Nothing in the shipped data uses them.</b> They were added for a siege believed
    /// to run on alternating Sundays; that reading came from a misread capture and siege is weekly.
    /// Kept because they are tested and a longer cycle is plausible for something else — but a slot
    /// that carries them is making a claim about several weeks of observation.</para>
    ///
    /// <para><paramref name="Mode"/> is the contest mode of the OCCURRENCE, for slots whose mode is
    /// known while their boss is not.</para>
    /// </summary>
    private sealed record ScheduleSlot(
        TimeSpan TimeOfDay,
        string EventType,
        int PeriodDays = 7,
        DateOnly? Anchor = null,
        IReadOnlyList<ScheduledBoss>? Bosses = null,
        string? Mode = null)
    {
        /// <summary>
        /// Whether this slot happens on <paramref name="date"/>. Weekly slots (no anchor) always do —
        /// the weekday lookup has already decided that. Anchored slots must land on the cycle.
        /// </summary>
        public bool OccursOn(DateOnly date)
        {
            if (Anchor is not { } anchor || PeriodDays <= 1)
            {
                return true;
            }

            // Positive modulo, so a date BEFORE the anchor still resolves correctly rather than
            // producing a negative remainder that never equals zero.
            var delta = date.DayNumber - anchor.DayNumber;

            return ((delta % PeriodDays) + PeriodDays) % PeriodDays == 0;
        }
    }
}

/// <summary>
/// One boss in a schedule slot, as the client's hover tooltip describes it:
/// <c>[Guild] Ramux / Stillreach | Monster Lv. 60 / Despawns after 50min.</c>
///
/// <para>Only <see cref="Name"/> is required. Everything else is null when nobody has read it yet, and
/// <b>absent is deliberately different from wrong</b> — a missing <see cref="Mode"/> means the badge
/// has not been checked, which is the honest state until it has. Filling these in with plausible
/// guesses is the failure this whole type exists to avoid: a player cannot tell a wrong boss from a
/// right one until they have travelled somewhere and found nothing.</para>
/// </summary>
public sealed record ScheduledBoss
{
    public required string Name { get; init; }

    /// <summary>
    /// Contest mode, read from the badge in the icon's bottom-right corner: a shield is PvP (the
    /// <c>[Guild]</c> contest), a dove is open. Null when unread.
    ///
    /// <para>It belongs to the OCCURRENCE, not the boss. Ramux was observed guild-contested on
    /// 2026-08-05 and open on 2026-08-12 — same weekday, same time, adjacent weeks. So this must never
    /// be copied from one occurrence onto another.</para>
    /// </summary>
    public string? Mode { get; init; }

    /// <summary>Where it spawns, e.g. <c>Stillreach</c>. The player has to travel there, so a
    /// countdown that names the zone is worth more than one that names only the boss.</summary>
    public string? Zone { get; init; }

    /// <summary>
    /// <c>archboss</c> where known. Archbosses appear INSIDE field-boss slots — Ramux was found among
    /// the icons of a <c>FieldBosses</c> 20:00 slot — so the slot's own type does not describe what is
    /// spawning and this is the only place that distinction lives.
    /// </summary>
    public string? Kind { get; init; }

    /// <summary>How long it stays up. The window to actually arrive, which the spawn instant alone
    /// does not give.</summary>
    public int? DespawnMinutes { get; init; }

    /// <summary>
    /// Whether this is a guild-only PvP contest. Matches both the badge vocabulary (<c>pvp</c>) and the
    /// tooltip's (<c>guild</c>), since the capture may reasonably record either.
    /// </summary>
    public bool IsGuildContest => IsGuildMode(Mode);

    /// <summary>
    /// The mode vocabulary, shared with the slot-level mode on <see cref="BossSpawn"/> so a guild
    /// contest is recognised identically whether the capture recorded it per boss or per slot.
    /// </summary>
    internal static bool IsGuildMode(string? mode) =>
        mode is not null
        && (mode.Equals("pvp", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("guild", StringComparison.OrdinalIgnoreCase));

    public bool IsArchboss => Kind?.Equals("archboss", StringComparison.OrdinalIgnoreCase) ?? false;

    /// <summary>Name, with the zone when known — what a countdown row should say.</summary>
    public override string ToString() => Zone is null ? Name : $"{Name} — {Zone}";
}

public sealed record BossSpawn(
    DateTimeOffset SpawnsAt,
    string EventType,
    TimeSpan Until,
    IReadOnlyList<ScheduledBoss>? Bosses = null,
    string? Mode = null)
{
    /// <summary>Bosses for this slot, empty when the schedule has not identified them yet.</summary>
    public IReadOnlyList<ScheduledBoss> Named => Bosses ?? [];

    /// <summary>Just the names, which is what the overlay label is built from.</summary>
    public IReadOnlyList<string> Names => Named.Select(b => b.Name).ToArray();

    /// <summary>
    /// True when this occurrence is a guild contest — either the slot itself is one, or a boss in it is.
    ///
    /// <para>The single most actionable thing the mode badge buys: a player without a guild cannot
    /// take part at all, so telling them to travel wastes their evening. It also flips preparation
    /// advice to the PvP stat axis. Unknown mode is NOT treated as guild — absent means unread, and
    /// guessing here is the error that matters.</para>
    ///
    /// <para>The slot-level source is why this is more than a scan of <see cref="Named"/>: the two
    /// daily guild slots have a known mode and a rotating, unnamed boss.</para>
    /// </summary>
    public bool HasGuildContest => ScheduledBoss.IsGuildMode(Mode) || Named.Any(b => b.IsGuildContest);

    public bool IsFieldBoss => EventType.Equals("FieldBosses", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The Daily tab's stream. Kept distinct from <see cref="IsFieldBoss"/> because both streams run at
    /// once, and two rows that both read "Field Bosses" hide the one worth organising for.
    /// </summary>
    public bool IsArchBoss => EventType.Equals("ArchBosses", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Scheduled guild PvP, not a boss. Sunday's single slot is siege, and labelling it as a boss
    /// would send players somewhere that does not exist.
    /// </summary>
    public bool IsSiege => EventType.Equals("Siege", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Filler from the legacy questlog table, and the one event type not worth travelling for. Only the
    /// old <c>dailySlots</c> form produces these.
    /// </summary>
    public bool IsDynamicEvent => EventType.Equals("DynamicEvents", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Label for the overlay. Names when the schedule has them — several joined, since a slot can hold
    /// more than one boss — and the generic event type when it does not. Never a guess: an unnamed slot
    /// reads "Field Bosses", which is true, rather than a plausible boss that is not spawning.
    /// </summary>
    public string DisplayName
    {
        get
        {
            if (Names.Count == 0)
            {
                return GenericName;
            }

            var named = string.Join(", ", Names);

            // The marker uses the client's own tooltip vocabulary, and it is not decoration: a player
            // without a guild is locked out, so an unmarked row sends them across the map for a contest
            // they cannot enter.
            return HasGuildContest ? $"{named} [Guild]" : named;
        }
    }

    /// <summary>The event-type label, used when no bosses are named.</summary>
    public string GenericName => EventType switch
    {
        // A guild slot names itself even with no boss identified, because "which boss" is the part
        // nobody has read while "guild only" is the part that decides whether to set out at all.
        "FieldBosses" => HasGuildContest ? "Guild Boss" : "Field Bosses",
        "ArchBosses" => "Arch Bosses",
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
