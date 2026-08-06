using Loadstar.Games.ThroneAndLiberty;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Spawn computation, with the emphasis on the two things that make schedule code go wrong:
/// days that carry nothing, and daylight saving.
/// </summary>
public sealed class BossScheduleTests
{
    /// <summary>
    /// The shape captured from a live client: most days run 17:00 and 20:00, Thursday and Monday
    /// are empty, and Sunday is siege at 18:00.
    /// </summary>
    private const string Weekly = """
        {
          "resetHourLocal": 3,
          "regions": {
            "Americas": {
              "defaultTimeZone": "America/New_York",
              "weeklySlots": {
                "Monday": [],
                "Tuesday":   [ { "time": "17:00", "type": "FieldBosses" }, { "time": "20:00", "type": "FieldBosses" } ],
                "Wednesday": [ { "time": "17:00", "type": "FieldBosses" }, { "time": "20:00", "type": "FieldBosses" } ],
                "Thursday": [],
                "Friday":    [ { "time": "17:00", "type": "FieldBosses" }, { "time": "20:00", "type": "FieldBosses" } ],
                "Saturday":  [ { "time": "17:00", "type": "FieldBosses" }, { "time": "20:00", "type": "FieldBosses" } ],
                "Sunday":    [ { "time": "18:00", "type": "Siege" } ]
              }
            },
            "Asia": { "defaultTimeZone": "Asia/Seoul", "dailySlots": [] }
          }
        }
        """;

    private static TimeZoneInfo NewYork => TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static DateTimeOffset LocalNewYork(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, NewYork.GetUtcOffset(local));
    }

    [Fact]
    public void EmptyDaysAreSkippedRatherThanFilledWithADefault()
    {
        // Wednesday 21:00 — past both of Wednesday's slots. Thursday is empty, so the next spawn is
        // Friday 17:00. A flat "same slots every day" model would wrongly answer Thursday 17:00.
        var schedule = BossSchedule.Parse(Weekly);

        var next = schedule.NextSpawns(LocalNewYork(2026, 8, 5, 21, 0), "Americas", NewYork, count: 1);

        var spawn = Assert.Single(next);
        Assert.Equal(DayOfWeek.Friday, TimeZoneInfo.ConvertTime(spawn.SpawnsAt, NewYork).DayOfWeek);
        Assert.Equal(17, TimeZoneInfo.ConvertTime(spawn.SpawnsAt, NewYork).Hour);
    }

    [Fact]
    public void SundayIsSiegeNotAFieldBoss()
    {
        // Labelling siege as a boss would send the player somewhere that does not exist.
        var schedule = BossSchedule.Parse(Weekly);

        var next = schedule.NextSpawns(LocalNewYork(2026, 8, 9, 6, 0), "Americas", NewYork, count: 1);

        var spawn = Assert.Single(next);
        Assert.True(spawn.IsSiege);
        Assert.False(spawn.IsFieldBoss);
        Assert.Equal("Siege", spawn.DisplayName);
        Assert.Equal(18, TimeZoneInfo.ConvertTime(spawn.SpawnsAt, NewYork).Hour);
    }

    [Fact]
    public void TwoConsecutiveEmptyDaysAreCrossed()
    {
        // Sunday 19:00 is past siege; Monday is empty, so the next is Tuesday 17:00.
        var schedule = BossSchedule.Parse(Weekly);

        var next = schedule.NextSpawns(LocalNewYork(2026, 8, 9, 19, 0), "Americas", NewYork, count: 1);

        var local = TimeZoneInfo.ConvertTime(Assert.Single(next).SpawnsAt, NewYork);
        Assert.Equal(DayOfWeek.Tuesday, local.DayOfWeek);
        Assert.Equal(17, local.Hour);
    }

    [Fact]
    public void SpawnsComeBackInChronologicalOrder()
    {
        var schedule = BossSchedule.Parse(Weekly);

        var next = schedule.NextSpawns(LocalNewYork(2026, 8, 4, 12, 0), "Americas", NewYork, count: 3);

        Assert.Equal(3, next.Count);
        Assert.True(next[0].SpawnsAt < next[1].SpawnsAt);
        Assert.True(next[1].SpawnsAt < next[2].SpawnsAt);
        Assert.All(next, s => Assert.True(s.Until > TimeSpan.Zero));
    }

    [Fact]
    public void RegionWithNoCapturedDataYieldsNothingRatherThanGuessing()
    {
        // Asia has no source. Inventing times would be worse than an empty countdown.
        var schedule = BossSchedule.Parse(Weekly);

        Assert.Empty(schedule.NextSpawns(DateTimeOffset.Now, "Asia", NewYork));
        Assert.False(schedule.HasSchedule("Asia"));
        Assert.True(schedule.HasSchedule("Americas"));
    }

    [Fact]
    public void QuestlogRegionSlugsResolveToTheScheduleFileNames()
    {
        // The live server list says "americas" and "japan-oceania"; this file says "Americas" and
        // "Asia". They have to meet somewhere or the countdown silently finds no region.
        var schedule = BossSchedule.Parse(Weekly);

        Assert.True(schedule.HasSchedule("americas"));
        Assert.False(schedule.HasSchedule("japan-oceania"));
        Assert.NotNull(schedule.DefaultTimeZone("japan-oceania"));
    }

    [Fact]
    public void SpawnsAreStableAcrossADaylightSavingTransition()
    {
        // US clocks go forward on 2026-03-08. A slot stored as wall-clock 17:00 must stay 17:00
        // local on both sides; storing computed UTC instants is what breaks this.
        var schedule = BossSchedule.Parse(Weekly);

        var before = schedule.NextSpawns(LocalNewYork(2026, 3, 6, 21, 0), "Americas", NewYork, count: 1);
        var after = schedule.NextSpawns(LocalNewYork(2026, 3, 9, 21, 0), "Americas", NewYork, count: 1);

        // Each side's next slot is a 17:00 one. The point is that the wall-clock hour is identical
        // on both sides of the transition — a schedule stored as UTC instants would drift by an
        // hour here, which is why the table stores wall clock plus a zone instead.
        Assert.Equal(17, TimeZoneInfo.ConvertTime(Assert.Single(before).SpawnsAt, NewYork).Hour);
        Assert.Equal(17, TimeZoneInfo.ConvertTime(Assert.Single(after).SpawnsAt, NewYork).Hour);
    }

    [Fact]
    public void FlatDailySlotsStillWorkAsAFallback()
    {
        // Older data without weeklySlots must keep working, expanded across every day.
        const string flat = """
            { "regions": { "Europe": { "dailySlots": [ { "time": "13:00", "type": "FieldBosses" } ] } } }
            """;

        var schedule = BossSchedule.Parse(flat);

        Assert.True(schedule.HasSchedule("Europe"));
        Assert.Equal(3, schedule.NextSpawns(DateTimeOffset.Now, "eu", NewYork, count: 3).Count);
    }

    [Fact]
    public void BundledScheduleLoadsAndCarriesTheCapturedAmericasWeek()
    {
        // Guards the embedded resource wiring — a schedule that fails to load would leave the
        // countdown permanently blank with no obvious cause.
        var schedule = BossSchedule.LoadBundled();

        Assert.True(schedule.HasSchedule("americas"));
        Assert.Equal(3, schedule.ResetHourLocal);

        // Siege is somewhere in Sunday's spawns, not necessarily the FIRST of them. This asked for
        // count: 1 until the two-stream merge, when the hourly field bosses at 11:00 and 14:00 Pacific
        // started landing between Sunday morning and the 18:00 siege — which is the merge working, not
        // a regression.
        var sunday = schedule.NextSpawns(LocalNewYork(2026, 8, 9, 6, 0), "americas", NewYork, count: 10);

        Assert.Contains(sunday, s => s.IsSiege);
    }

    [Fact]
    public void CountdownFormattingDegradesSensibly()
    {
        var spawn = new BossSpawn(DateTimeOffset.Now, "FieldBosses", TimeSpan.Zero);

        Assert.Equal("2h 05m", spawn.Countdown(TimeSpan.FromMinutes(125)));
        Assert.Equal("18m 04s", spawn.Countdown(new TimeSpan(0, 18, 4)));
        Assert.Equal("42s", spawn.Countdown(TimeSpan.FromSeconds(42)));
        Assert.Equal("now", spawn.Countdown(TimeSpan.Zero));
    }
    /// <summary>
    /// Siege runs on ALTERNATING Sundays, so a weekday-keyed slot alone is wrong every other week.
    /// Observed in a live client: 09/08 siege, 16/08 empty, 23/08 siege.
    /// </summary>
    [Fact]
    public void BiweeklySlotSkipsTheOffWeek()
    {
        const string json = """
            {
              "resetHourLocal": 3,
              "regions": {
                "Americas": {
                  "defaultTimeZone": "America/Los_Angeles",
                  "weeklySlots": {
                    "Sunday": [
                      { "time": "18:00", "type": "Siege", "everyDays": 14, "since": "2026-08-09" }
                    ]
                  }
                }
              }
            }
            """;

        var schedule = BossSchedule.Parse(json);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

        // From the Saturday before each Sunday, the next spawn should land on the ON weeks only.
        var fromAug8 = schedule.NextSpawns(
            new DateTimeOffset(2026, 8, 8, 12, 0, 0, zone.GetUtcOffset(new DateTime(2026, 8, 8))),
            "Americas", zone, count: 3);

        Assert.NotEmpty(fromAug8);

        var dates = fromAug8.Select(s => TimeZoneInfo.ConvertTime(s.SpawnsAt, zone).Date).ToList();

        // 09/08 and 23/08 are siege; 16/08 must not appear at all.
        Assert.Contains(new DateTime(2026, 8, 9), dates);
        Assert.DoesNotContain(new DateTime(2026, 8, 16), dates);
        Assert.Contains(new DateTime(2026, 8, 23), dates);
        Assert.All(fromAug8, s => Assert.True(s.IsSiege));
    }

    /// <summary>A slot with no recurrence fields stays weekly — every existing entry must be unaffected.</summary>
    [Fact]
    public void SlotWithoutRecurrenceStaysWeekly()
    {
        const string json = """
            {
              "resetHourLocal": 3,
              "regions": {
                "Americas": {
                  "defaultTimeZone": "America/Los_Angeles",
                  "weeklySlots": { "Sunday": [ { "time": "18:00", "type": "Siege" } ] }
                }
              }
            }
            """;

        var schedule = BossSchedule.Parse(json);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

        var spawns = schedule.NextSpawns(
            new DateTimeOffset(2026, 8, 8, 12, 0, 0, zone.GetUtcOffset(new DateTime(2026, 8, 8))),
            "Americas", zone, count: 3);

        var dates = spawns.Select(s => TimeZoneInfo.ConvertTime(s.SpawnsAt, zone).Date).ToList();

        // Consecutive Sundays, because nothing asked for a longer cycle.
        Assert.Contains(new DateTime(2026, 8, 9), dates);
        Assert.Contains(new DateTime(2026, 8, 16), dates);
    }

    /// <summary>
    /// Siege is WEEKLY. This test asserted the opposite until 2026-08-06, because a capture read
    /// 16/08 Sun as an empty row and the conclusion was published. Kept pointing at the bundled data
    /// so the same mistake cannot be reintroduced quietly.
    /// </summary>
    [Fact]
    public void BundledAmericasSiegeIsWeekly()
    {
        var schedule = BossSchedule.LoadBundled();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

        // count: 200, up from 40. The merged schedule yields eight or nine spawns a day, so 40 no
        // longer reaches past the first Sunday and the test failed on the very data it was written to
        // protect. The count has to exceed the walk window's worth of spawns, not a guessed few.
        var sieges = schedule
            .NextSpawns(
                new DateTimeOffset(2026, 8, 8, 12, 0, 0, zone.GetUtcOffset(new DateTime(2026, 8, 8))),
                "Americas", zone, count: 200)
            .Where(s => s.IsSiege)
            .Select(s => TimeZoneInfo.ConvertTime(s.SpawnsAt, zone).Date)
            .ToList();

        Assert.Contains(new DateTime(2026, 8, 9), sieges);
        Assert.Contains(new DateTime(2026, 8, 16), sieges);
        Assert.Contains(new DateTime(2026, 8, 23), sieges);
    }
    /// <summary>
    /// One slot, several bosses — the client shows five icons at 20:00. Names win over the generic
    /// label; an empty or absent list falls back to "Field Bosses" rather than inventing one.
    /// </summary>
    [Fact]
    public void SlotCanNameSeveralBossesAndFallsBackWhenItDoesNot()
    {
        const string json = """
            {
              "resetHourLocal": 3,
              "regions": {
                "Americas": {
                  "defaultTimeZone": "America/Los_Angeles",
                  "weeklySlots": {
                    "Wednesday": [
                      { "time": "17:00", "type": "FieldBosses", "bosses": ["Cordy", "Deluzhnoa"] },
                      { "time": "20:00", "type": "FieldBosses" }
                    ]
                  }
                }
              }
            }
            """;

        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        var spawns = BossSchedule.Parse(json).NextSpawns(
            new DateTimeOffset(2026, 8, 5, 8, 0, 0, zone.GetUtcOffset(new DateTime(2026, 8, 5))),
            "Americas", zone, count: 2);

        Assert.Equal(2, spawns.Count);

        Assert.Equal(["Cordy", "Deluzhnoa"], spawns[0].Names);
        Assert.Equal("Cordy, Deluzhnoa", spawns[0].DisplayName);

        Assert.Empty(spawns[1].Names);
        Assert.Equal("Field Bosses", spawns[1].DisplayName);
    }
    /// <summary>
    /// The object form carries what the tooltip gives; a bare string still parses as a name. Both must
    /// work, because schedules published before the object existed are being fetched by installs now.
    /// </summary>
    [Fact]
    public void BossEntriesAcceptObjectsAndBareStrings()
    {
        const string json = """
            {
              "resetHourLocal": 3,
              "regions": {
                "Americas": {
                  "defaultTimeZone": "America/Los_Angeles",
                  "weeklySlots": {
                    "Wednesday": [
                      { "time": "20:00", "type": "FieldBosses", "bosses": [
                          { "name": "Ramux", "mode": "pvp", "zone": "Stillreach",
                            "kind": "archboss", "despawnMinutes": 50 },
                          "Talus",
                          { "name": "  " }
                        ] }
                    ]
                  }
                }
              }
            }
            """;

        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        // count: 1 — the slot is weekly, so a larger count returns every Wednesday in the walk window.
        var spawn = Assert.Single(BossSchedule.Parse(json).NextSpawns(
            new DateTimeOffset(2026, 8, 5, 8, 0, 0, zone.GetUtcOffset(new DateTime(2026, 8, 5))),
            "Americas", zone, count: 1));

        // The nameless entry is dropped rather than kept as a blank row.
        Assert.Equal(2, spawn.Named.Count);
        Assert.Equal(["Ramux", "Talus"], spawn.Names);

        // The [Guild] marker is part of the label, not just a flag on the model. This assertion read
        // "Ramux, Talus" until the two-stream merge, which documented the gap rather than the
        // behaviour: a guildless player shown that row travels to a contest they are locked out of.
        Assert.Equal("Ramux, Talus [Guild]", spawn.DisplayName);

        var ramux = spawn.Named[0];
        Assert.Equal("Stillreach", ramux.Zone);
        Assert.Equal(50, ramux.DespawnMinutes);
        Assert.True(ramux.IsArchboss);
        Assert.True(ramux.IsGuildContest);
        Assert.Equal("Ramux — Stillreach", ramux.ToString());

        // Bare string: name only, and crucially NOT inferred to be a guild contest.
        var talus = spawn.Named[1];
        Assert.Null(talus.Mode);
        Assert.Null(talus.Zone);
        Assert.False(talus.IsGuildContest);

        Assert.True(spawn.HasGuildContest);
    }

    /// <summary>
    /// Unread mode must never read as a guild contest. Absent means nobody checked the badge, and
    /// inferring PvP from silence would tell a guildless player to skip a boss open to them.
    /// </summary>
    [Fact]
    public void UnreadModeIsNotTreatedAsGuildContest()
    {
        const string json = """
            {
              "regions": {
                "Americas": {
                  "defaultTimeZone": "America/Los_Angeles",
                  "weeklySlots": {
                    "Wednesday": [ { "time": "20:00", "type": "FieldBosses", "bosses": ["Talus"] } ]
                  }
                }
              }
            }
            """;

        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        // count: 1 — the slot is weekly, so a larger count returns every Wednesday in the walk window.
        var spawn = Assert.Single(BossSchedule.Parse(json).NextSpawns(
            new DateTimeOffset(2026, 8, 5, 8, 0, 0, zone.GetUtcOffset(new DateTime(2026, 8, 5))),
            "Americas", zone, count: 1));

        Assert.False(spawn.HasGuildContest);
    }
    /// <summary>
    /// The point of storing UTC: a boss spawns at ONE instant, so two players in different timezones
    /// must resolve the same instant from the same data.
    ///
    /// <para>Before this, slot times were read in the caller's zone, so a Pacific player and an Eastern
    /// player on the same Americas server got instants three hours apart — and the Eastern one was
    /// three hours early while looking entirely plausible.</para>
    /// </summary>
    [Fact]
    public void UtcTimesResolveToTheSameInstantInEveryTimezone()
    {
        const string json = """
            {
              "timeBasis": "utc",
              "regions": {
                "Americas": {
                  "weeklySlots": {
                    "Saturday": [ { "time": "00:00", "type": "FieldBosses", "localPst": "17:00" } ]
                  }
                }
              }
            }
            """;

        var schedule = BossSchedule.Parse(json);
        Assert.True(schedule.TimesAreUtc);

        // The same moment, expressed from two machines.
        var moment = new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

        var fromPacific = schedule.NextSpawns(
            moment, "Americas", TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"), count: 1);
        var fromEastern = schedule.NextSpawns(
            moment, "Americas", TimeZoneInfo.FindSystemTimeZoneById("America/New_York"), count: 1);
        var fromTokyo = schedule.NextSpawns(
            moment, "Americas", TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"), count: 1);

        var expected = new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(expected, Assert.Single(fromPacific).SpawnsAt);
        Assert.Equal(expected, Assert.Single(fromEastern).SpawnsAt);
        Assert.Equal(expected, Assert.Single(fromTokyo).SpawnsAt);

        // And that instant is Friday 17:00 Pacific, which is what the client showed.
        var pacific = TimeZoneInfo.ConvertTime(
            expected, TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"));

        Assert.Equal(DayOfWeek.Friday, pacific.DayOfWeek);
        Assert.Equal(17, pacific.Hour);
    }

    /// <summary>
    /// A schedule with no <c>timeBasis</c> keeps resolving against the caller's zone. Installs are
    /// fetching such a file right now, so changing its meaning would break their timer.
    /// </summary>
    [Fact]
    public void ScheduleWithoutTimeBasisStaysZoneRelative()
    {
        const string json = """
            {
              "regions": {
                "Americas": {
                  "weeklySlots": { "Friday": [ { "time": "17:00", "type": "FieldBosses" } ] }
                }
              }
            }
            """;

        var schedule = BossSchedule.Parse(json);
        Assert.False(schedule.TimesAreUtc);

        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        var spawn = Assert.Single(schedule.NextSpawns(
            new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero), "Americas", zone, count: 1));

        // 17:00 in the supplied zone, exactly as before.
        Assert.Equal(17, TimeZoneInfo.ConvertTime(spawn.SpawnsAt, zone).Hour);
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    /// <summary>
    /// The two streams are concurrent, not alternatives. The game splits them across the map's Daily
    /// and Hourly tabs and a day with archbosses shows both, so the merged table must too.
    /// </summary>
    [Fact]
    public void BothStreamsAppearOnADayThatHasArchbosses()
    {
        const string json = """
            {
              "timeBasis": "utc",
              "regions": {
                "Americas": {
                  "hourlySlots": [ { "time": "04:00", "type": "FieldBosses" } ],
                  "weeklySlots": {
                    "Wednesday": [ { "time": "03:00", "type": "ArchBosses" } ]
                  }
                }
              }
            }
            """;

        // Wednesday 2026-08-12, before either slot.
        var spawns = BossSchedule.Parse(json).NextSpawns(Utc(2026, 8, 12), "Americas", TimeZoneInfo.Utc, count: 2);

        Assert.Equal(2, spawns.Count);

        // Chronological, and the archboss row is distinguishable from the field-boss one.
        Assert.Equal(Utc(2026, 8, 12, 3, 0), spawns[0].SpawnsAt);
        Assert.True(spawns[0].IsArchBoss);
        Assert.Equal("Arch Bosses", spawns[0].DisplayName);

        Assert.Equal(Utc(2026, 8, 12, 4, 0), spawns[1].SpawnsAt);
        Assert.True(spawns[1].IsFieldBoss);
        Assert.Equal("Field Bosses", spawns[1].DisplayName);
    }

    /// <summary>
    /// THE BUG THE MERGE FIXES, in the exact shape it was reported: "there is a third field boss that
    /// says it is in 48h 27m. This is not correct, there would be more bosses today."
    ///
    /// <para>The Daily tab leaves Thursday and Monday (Pacific) empty, so before the merge a Thursday
    /// morning found nothing until Friday evening — a countdown a day and a half out, while seven field
    /// bosses were in fact spawning that same evening.</para>
    /// </summary>
    [Fact]
    public void HourlyStreamFillsWeekdaysTheDailyTabLeavesEmpty()
    {
        var schedule = BossSchedule.LoadBundled();
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

        // Thursday 2026-08-06, 09:00 Pacific — a day with no archbosses and no siege.
        var next = schedule.NextSpawns(Utc(2026, 8, 6, 16, 0), "Americas", pacific, count: 1);

        var spawn = Assert.Single(next);
        var local = TimeZoneInfo.ConvertTime(spawn.SpawnsAt, pacific);

        Assert.Equal(DayOfWeek.Thursday, local.DayOfWeek);
        Assert.Equal(11, local.Hour);

        // Two hours out, not thirty-two. That difference is the whole point.
        Assert.True(spawn.Until < TimeSpan.FromHours(3), $"next spawn was {spawn.Until} away");
    }

    /// <summary>
    /// A guild slot's mode is known while its boss is not — the two daily guild slots sit at fixed
    /// times and rotate their occupant. So mode has to live on the slot, and the label has to say
    /// "guild" without inventing a name.
    /// </summary>
    [Fact]
    public void GuildSlotIsMarkedWithoutNamingABoss()
    {
        const string json = """
            {
              "timeBasis": "utc",
              "regions": {
                "Americas": {
                  "hourlySlots": [
                    { "time": "01:00", "type": "FieldBosses" },
                    { "time": "01:30", "type": "FieldBosses", "mode": "guild" }
                  ]
                }
              }
            }
            """;

        var spawns = BossSchedule.Parse(json).NextSpawns(Utc(2026, 8, 12), "Americas", TimeZoneInfo.Utc, count: 2);

        Assert.Equal(2, spawns.Count);

        // The peace slot must NOT pick up the guild marker from its neighbour.
        Assert.False(spawns[0].HasGuildContest);
        Assert.Equal("Field Bosses", spawns[0].DisplayName);

        // The guild slot names itself, with no boss identified and none invented.
        Assert.True(spawns[1].HasGuildContest);
        Assert.Empty(spawns[1].Names);
        Assert.Equal("Guild Boss", spawns[1].DisplayName);
    }

    /// <summary>The hourly stream stands alone — a region may have it and no weekday table at all.</summary>
    [Fact]
    public void HourlySlotsWorkWithoutAWeeklyTable()
    {
        const string json = """
            {
              "timeBasis": "utc",
              "regions": {
                "Americas": { "hourlySlots": [ { "time": "04:00", "type": "FieldBosses" } ] }
              }
            }
            """;

        var schedule = BossSchedule.Parse(json);

        Assert.True(schedule.HasSchedule("Americas"));
        Assert.Contains("americas", schedule.PopulatedRegions);

        // Every day, so three consecutive days.
        var spawns = schedule.NextSpawns(Utc(2026, 8, 12), "Americas", TimeZoneInfo.Utc, count: 3);

        Assert.Equal([Utc(2026, 8, 12, 4, 0), Utc(2026, 8, 13, 4, 0), Utc(2026, 8, 14, 4, 0)],
            spawns.Select(s => s.SpawnsAt));
    }

    /// <summary>
    /// The bundled hourly stream: seven slots on every single day, verified on a UTC day the weekday
    /// table leaves empty so nothing else can be inflating the count.
    /// </summary>
    [Fact]
    public void BundledHourlyStreamRunsSevenSlotsEveryDay()
    {
        var schedule = BossSchedule.LoadBundled();

        // UTC Tuesday 2026-08-11 carries no weeklySlots entries at all.
        var day = schedule
            .NextSpawns(Utc(2026, 8, 11), "Americas", TimeZoneInfo.Utc, count: 12)
            .Where(s => s.SpawnsAt < Utc(2026, 8, 12))
            .ToList();

        Assert.Equal(7, day.Count);
        Assert.All(day, s => Assert.True(s.IsFieldBoss));
        Assert.DoesNotContain(day, s => s.IsArchBoss);

        // Exactly two of the seven are guild contests: 18:30 and 21:30 Pacific.
        Assert.Equal(2, day.Count(s => s.HasGuildContest));
        Assert.Equal([new TimeSpan(1, 30, 0), new TimeSpan(4, 30, 0)],
            day.Where(s => s.HasGuildContest).Select(s => s.SpawnsAt.TimeOfDay));
    }

    /// <summary>
    /// Sunday 18:00 Pacific is BOTH the weekly siege and an hourly field-boss slot, so one instant
    /// carries two events. Collapsing them by time would hide a real spawn behind another.
    /// </summary>
    [Fact]
    public void SiegeAndAnHourlySlotShareOneInstantWithoutCollapsing()
    {
        var schedule = BossSchedule.LoadBundled();
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

        // Monday 01:00 UTC is Sunday 18:00 Pacific.
        var both = schedule.NextSpawns(Utc(2026, 8, 10), "Americas", pacific, count: 2);

        Assert.Equal(2, both.Count);
        Assert.All(both, s => Assert.Equal(Utc(2026, 8, 10, 1, 0), s.SpawnsAt));

        Assert.Contains(both, s => s.IsSiege);
        Assert.Contains(both, s => s.IsFieldBoss);

        // And it really is Sunday evening for the player.
        var local = TimeZoneInfo.ConvertTime(both[0].SpawnsAt, pacific);
        Assert.Equal(DayOfWeek.Sunday, local.DayOfWeek);
        Assert.Equal(18, local.Hour);
    }

    /// <summary>
    /// The Daily tab carries only siege and archbosses, so its slots are typed ArchBosses. They were
    /// FieldBosses until the merge, which was wrong on its own terms and — once both streams showed at
    /// once — left two identical-looking rows with no way to tell which was worth organising for.
    /// </summary>
    [Fact]
    public void BundledDailyTabSlotsAreTypedAsArchbosses()
    {
        var schedule = BossSchedule.LoadBundled();
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

        var archbosses = schedule
            .NextSpawns(Utc(2026, 8, 11), "Americas", pacific, count: 40)
            .Where(s => s.IsArchBoss)
            .ToList();

        Assert.NotEmpty(archbosses);
        Assert.All(archbosses, s => Assert.Equal("Arch Bosses", s.DisplayName));

        // 17:00 and 20:00 Pacific, which is what the client shows.
        Assert.All(archbosses, s =>
            Assert.Contains(TimeZoneInfo.ConvertTime(s.SpawnsAt, pacific).Hour, new[] { 17, 20 }));
    }

    /// <summary>
    /// A merged day's slots must come out in time order. NextSpawns stops walking as soon as it has
    /// enough, so concatenating two sorted lists without re-sorting returns the wrong three.
    /// </summary>
    [Fact]
    public void MergedDayIsReSortedNotAppended()
    {
        const string json = """
            {
              "timeBasis": "utc",
              "regions": {
                "Americas": {
                  "hourlySlots": [ { "time": "01:00", "type": "FieldBosses" } ],
                  "weeklySlots": {
                    "Wednesday": [ { "time": "23:00", "type": "ArchBosses" } ]
                  }
                }
              }
            }
            """;

        // The weekday slot is LATE in the day and the hourly one is early, so appending would put
        // 23:00 before 01:00 and the first spawn returned would be the wrong one.
        var spawns = BossSchedule.Parse(json).NextSpawns(Utc(2026, 8, 12), "Americas", TimeZoneInfo.Utc, count: 1);

        var spawn = Assert.Single(spawns);
        Assert.Equal(Utc(2026, 8, 12, 1, 0), spawn.SpawnsAt);
        Assert.True(spawn.IsFieldBoss);
    }

    /// <summary>
    /// A dated slot happens on exactly its listed days and nowhere else — that is the whole point, and
    /// it is what makes monthly events expressible without a "last Sunday of the month" rule that the
    /// UTC weekday roll would break once a year.
    /// </summary>
    [Fact]
    public void DatedSlotHappensOnlyOnItsListedDates()
    {
        const string json = """
            {
              "timeBasis": "utc",
              "regions": {
                "Americas": {
                  "datedSlots": [
                    { "time": "00:30", "type": "TaxDelivery", "mode": "guild",
                      "dates": ["2026-08-31", "2026-09-28"] }
                  ]
                }
              }
            }
            """;

        var schedule = BossSchedule.Parse(json);

        // The NEXT occurrence is findable from any point before it, which is the property that matters:
        // the walk bound only has to exceed the gap between consecutive occurrences, not span the whole
        // list. Asking for two monthly events at once genuinely does exceed one window, and no caller
        // needs that.
        Assert.Equal(
            Utc(2026, 8, 31, 0, 30),
            Assert.Single(schedule.NextSpawns(Utc(2026, 8, 1), "Americas", TimeZoneInfo.Utc, count: 3)).SpawnsAt);

        Assert.Equal(
            Utc(2026, 9, 28, 0, 30),
            Assert.Single(schedule.NextSpawns(Utc(2026, 9, 1), "Americas", TimeZoneInfo.Utc, count: 3)).SpawnsAt);

        // And once the list is exhausted the event STOPS, rather than repeating on a guessed cadence.
        // That is the deliberate failure mode: a wrong date on a PvP event is a guild turning up to
        // nothing, so it goes quiet and waits to be recaptured.
        Assert.Empty(schedule.NextSpawns(Utc(2026, 10, 1), "Americas", TimeZoneInfo.Utc, count: 1));
    }

    /// <summary>
    /// Dated slots are checked on EVERY weekday. Filing them under a weekday key would mean working out
    /// which UTC weekday a Pacific date lands on, and getting that wrong is a whole-day error that
    /// still looks plausible.
    /// </summary>
    [Fact]
    public void DatedSlotsAreFoundWhicheverWeekdayTheDateFallsOn()
    {
        const string json = """
            {
              "timeBasis": "utc",
              "regions": {
                "Americas": {
                  "weeklySlots": { "Monday": [], "Tuesday": [], "Wednesday": [], "Thursday": [],
                                   "Friday": [], "Saturday": [], "Sunday": [] },
                  "datedSlots": [
                    { "time": "12:00", "type": "GuildRaid",
                      "dates": ["2026-08-12", "2026-08-13", "2026-08-14", "2026-08-15"] }
                  ]
                }
              }
            }
            """;

        // Wednesday through Saturday, from an explicitly empty weekday table.
        var spawns = BossSchedule.Parse(json).NextSpawns(Utc(2026, 8, 12), "Americas", TimeZoneInfo.Utc, count: 4);

        Assert.Equal(4, spawns.Count);
        Assert.Equal(
            [DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday],
            spawns.Select(s => s.SpawnsAt.DayOfWeek));
    }

    /// <summary>
    /// An event type the code has never heard of still renders as words. This is what lets the schedule
    /// introduce a type without a release — the file is published on the game's cadence, not the app's.
    /// </summary>
    [Theory]
    [InlineData("TaxDelivery", "Tax Delivery")]
    [InlineData("GuildRaid", "Guild Raid")]
    [InlineData("DimensionalTrial", "Dimensional Trial")]
    [InlineData("Siege", "Siege")]
    [InlineData("PvPEvent", "Pv PEvent")]
    public void UnknownEventTypesRenderAsWordsRatherThanRaw(string type, string expected)
    {
        // The PvPEvent row is the LIMIT, asserted so it is not mistaken for a capability: a run of
        // capitals is genuinely ambiguous — "PvP" then "Event" is not recoverable from casing alone —
        // and the humaniser does not guess. Any type whose spacing matters needs an explicit case in
        // GenericName. Name types plainly (TaxDelivery, GuildRaid) and this never comes up.
        Assert.Equal(expected, new BossSpawn(DateTimeOffset.Now, type, TimeSpan.Zero).GenericName);
    }

    /// <summary>
    /// The bundled tax delivery: a guild PvP event at 17:30 Pacific, ahead of the 18:00 siege, on the
    /// dates captured rather than on a monthly rule.
    /// </summary>
    [Fact]
    public void BundledTaxDeliveryIsAGuildEventBeforeTheSiege()
    {
        var schedule = BossSchedule.LoadBundled();
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

        var tax = schedule
            .NextSpawns(Utc(2026, 8, 30), "Americas", pacific, count: 40)
            .Where(s => s.EventType == "TaxDelivery")
            .ToList();

        var first = Assert.Single(tax);
        var local = TimeZoneInfo.ConvertTime(first.SpawnsAt, pacific);

        // 17:30 Pacific on a Sunday — the last Sunday of August 2026.
        Assert.Equal(DayOfWeek.Sunday, local.DayOfWeek);
        Assert.Equal(new DateTime(2026, 8, 30), local.Date);
        Assert.Equal(new TimeSpan(17, 30, 0), local.TimeOfDay);

        // PvP, and labelled without a code change for the type.
        Assert.True(first.HasGuildContest);
        Assert.Equal("Tax Delivery", first.DisplayName);
    }
}
