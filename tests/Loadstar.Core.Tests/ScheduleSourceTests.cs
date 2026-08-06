using Loadstar.Games.ThroneAndLiberty;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Where the schedule is fetched from. No network here — these guard the ORDER, which is the part that
/// went wrong and the part a reader is most likely to "tidy" back.
/// </summary>
public sealed class ScheduleSourceTests
{
    /// <summary>
    /// Raw content must be tried FIRST.
    ///
    /// <para>Pages spent three hours serving a stale-but-valid schedule while its deployments failed.
    /// Because the response was a real schedule, validation accepted it and a failure-triggered fallback
    /// never ran — the countdown kept showing the pre-merge "next boss in 33h". Ordering by freshness is
    /// what fixed it, and raw content is fresher by construction: it has no deploy pipeline to fall
    /// behind in.</para>
    /// </summary>
    [Fact]
    public void RawContentIsTriedBeforePages()
    {
        Assert.Equal(2, ScheduleSource.Sources.Count);
        Assert.Equal(ScheduleSource.RawUrl, ScheduleSource.Sources[0]);
        Assert.Equal(ScheduleSource.PublishedUrl, ScheduleSource.Sources[1]);

        // Raw must not depend on Pages infrastructure at all — that is the entire reason it is first.
        Assert.Contains("raw.githubusercontent.com", ScheduleSource.RawUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("github.io", ScheduleSource.RawUrl, StringComparison.Ordinal);
    }

    /// <summary>
    /// Pages is kept rather than dropped: it is the documented URL and it covers a raw-content outage.
    /// Deleting it would trade one single point of failure for another.
    /// </summary>
    [Fact]
    public void PagesRemainsASource()
    {
        Assert.Contains("eugenebednik.github.io", ScheduleSource.PublishedUrl, StringComparison.Ordinal);
        Assert.Contains(ScheduleSource.PublishedUrl, ScheduleSource.Sources);
    }

    /// <summary>Both sources must name the same file, or they are not fallbacks for each other.</summary>
    [Fact]
    public void EverySourcePointsAtTheSameFile()
    {
        Assert.All(ScheduleSource.Sources, url =>
        {
            Assert.EndsWith("boss-schedule.json", url, StringComparison.Ordinal);
            Assert.StartsWith("https://", url, StringComparison.Ordinal);
        });
    }
}
