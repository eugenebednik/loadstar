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

        var sunday = schedule.NextSpawns(LocalNewYork(2026, 8, 9, 6, 0), "americas", NewYork, count: 1);
        Assert.True(Assert.Single(sunday).IsSiege);
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

    [Fact]
    public void BundledAmericasSiegeIsBiweekly()
    {
        var schedule = BossSchedule.LoadBundled();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

        var sieges = schedule
            .NextSpawns(
                new DateTimeOffset(2026, 8, 8, 12, 0, 0, zone.GetUtcOffset(new DateTime(2026, 8, 8))),
                "Americas", zone, count: 40)
            .Where(s => s.IsSiege)
            .Select(s => TimeZoneInfo.ConvertTime(s.SpawnsAt, zone).Date)
            .ToList();

        Assert.Contains(new DateTime(2026, 8, 9), sieges);
        Assert.DoesNotContain(new DateTime(2026, 8, 16), sieges);
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
}
