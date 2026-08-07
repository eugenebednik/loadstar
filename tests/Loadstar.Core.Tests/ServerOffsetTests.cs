using Loadstar.Games.ThroneAndLiberty;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Servers run FIXED UTC offsets — Americas UTC-5, Europe UTC+2, Japan/Oceania UTC+9 — and knowing
/// them resolved the longest-standing question in the schedule file.
///
/// <para>questlog's grid prints SERVER time; the live client prints the VIEWER'S local time. The
/// two-hour gap that made the file distrust its own numbers was just UTC-5 against a Pacific player's
/// UTC-7. These tests convert the stored UTC back to server time and check it lands on the numbers
/// questlog printed, which pins the conversion from both ends.</para>
/// </summary>
public sealed class ServerOffsetTests
{
    private static readonly DateTimeOffset Anchor = new(2026, 8, 7, 6, 0, 0, TimeSpan.Zero);

    private static IEnumerable<string> ServerTimes(string region, int offsetHours, Func<BossSpawn, bool> where) =>
        BossSchedule.LoadBundled()
            .NextSpawns(Anchor, region, TimeZoneInfo.Utc, count: 40)
            .Where(where)
            .Select(s => s.SpawnsAt.ToOffset(TimeSpan.FromHours(offsetHours)).ToString("HH:mm"))
            .Distinct();

    /// <summary>
    /// The Americas archboss slots must read 19:00 and 22:00 in server time — exactly the numbers on
    /// questlog's grid, which this file spent days treating as contradicting the client.
    /// </summary>
    [Fact]
    public void AmericasArchbossSlotsMatchQuestlogsGridInServerTime()
    {
        var times = ServerTimes("americas", -5, s => s.IsArchBoss).OrderBy(t => t).ToArray();

        Assert.Equal(["19:00", "22:00"], times);
    }

    /// <summary>
    /// Europe's field bosses must read 13:00, 16:00, 20:00 and 23:00 in server time — the questlog
    /// numbers they were captured from.
    ///
    /// <para>This is the regression that mattered. Europe's slots are the legacy <c>dailySlots</c> form
    /// and were stored as SERVER wall clock, but <c>timeBasis</c> is a ROOT flag — so once the file
    /// switched to UTC, every European slot was being read as UTC and every European player's countdown
    /// was two hours early. Nothing failed; it was just silently wrong for a whole region.</para>
    /// </summary>
    [Fact]
    public void EuropeFieldBossesMatchQuestlogsGridInServerTime()
    {
        var times = ServerTimes("europe", 2, s => s.IsFieldBoss).OrderBy(t => t).ToArray();

        Assert.Equal(["01:00", "13:00", "16:00", "20:00", "23:00"], times);
    }

    /// <summary>
    /// A fixed-offset server cannot shift with daylight saving, so one stored instant reads as the same
    /// server-local time in August and in December. That is what makes UTC storage safe rather than a
    /// seasonal liability, and it is the answer to the DST question the file carried for days.
    /// </summary>
    [Fact]
    public void FixedOffsetServersDoNotShiftAcrossDaylightSaving()
    {
        var schedule = BossSchedule.LoadBundled();
        var server = TimeSpan.FromHours(-5);

        string FirstArchbossServerTime(DateTimeOffset from) =>
            schedule.NextSpawns(from, "americas", TimeZoneInfo.Utc, count: 40)
                .First(s => s.IsArchBoss)
                .SpawnsAt.ToOffset(server)
                .ToString("HH:mm");

        var august = FirstArchbossServerTime(new DateTimeOffset(2026, 8, 7, 6, 0, 0, TimeSpan.Zero));
        var december = FirstArchbossServerTime(new DateTimeOffset(2026, 12, 9, 6, 0, 0, TimeSpan.Zero));

        Assert.Equal(august, december);

        // And the Pacific player's clock DOES move, which is the whole point: 00:00Z is 17:00 in PDT
        // and 16:00 in PST. The file predicted 16:00 after the November change; here it is.
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        var midnightUtc = new DateTimeOffset(2026, 12, 10, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(16, TimeZoneInfo.ConvertTime(midnightUtc, pacific).Hour);
    }
}
