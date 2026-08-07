using System.Net;
using System.Net.Http.Headers;
using Loadstar.Core.Time;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Clock correction, and mostly the cases where it must REFUSE to correct.
///
/// <para>The first version of TimeSync asked one time service and believed it. It reported the
/// development machine 21 minutes fast; three independent hosts then agreed the machine was accurate to
/// under a second, and Cloudflare put it at 93 milliseconds. The service was wrong. Shipped, that version
/// would have injected a 21-minute error into a correct clock — so the tests that matter here are the
/// ones proving a single confident source cannot do that.</para>
///
/// <para>No real network: a stub handler stands in for every host.</para>
/// </summary>
public sealed class TimeSyncTests : IDisposable
{
    public TimeSyncTests() => TimeSync.Reset();

    public void Dispose() => TimeSync.Reset();

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(respond(request));
    }

    /// <summary>A reply carrying a Date header, which is how the corroborating hosts are read.</summary>
    private static HttpResponseMessage WithDate(DateTimeOffset served)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) };
        response.Headers.Date = served;
        return response;
    }

    /// <summary>Cloudflare's trace body, whose <c>ts</c> field carries milliseconds.</summary>
    private static HttpResponseMessage WithTrace(DateTimeOffset served) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"fl=1a2b3c\nh=cloudflare.com\nip=203.0.113.7\nts={served.ToUnixTimeMilliseconds() / 1000.0:0.000}\nvisit_scheme=https"),
        };

    /// <summary>Every source agrees on <paramref name="served"/>.</summary>
    private static HttpClient AllSaying(DateTimeOffset served) =>
        new(new Stub(r => r.RequestUri!.AbsoluteUri.Contains("cdn-cgi/trace", StringComparison.Ordinal)
            ? WithTrace(served)
            : WithDate(served)));

    [Fact]
    public async Task AnAccurateClockIsLeftAlone()
    {
        using var http = AllSaying(DateTimeOffset.UtcNow);

        await TimeSync.SynchroniseAsync(http);

        Assert.False(TimeSync.IsClockNoticeablyWrong);
        Assert.Equal(TimeSpan.Zero, TimeSync.Offset);

        // Four sources answered even though no correction was needed — that is the healthy state, and
        // distinguishing it from "nothing answered" is why AgreeingSources exists.
        Assert.Equal(4, TimeSync.AgreeingSources);
    }

    /// <summary>
    /// A clock that really is wrong, with every source agreeing, IS corrected. Positive offset means the
    /// machine is behind.
    /// </summary>
    [Fact]
    public async Task AClockAllSourcesAgreeIsWrongGetsCorrected()
    {
        var real = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(9);
        using var http = AllSaying(real);

        await TimeSync.SynchroniseAsync(http);

        Assert.True(TimeSync.IsSynced);
        Assert.True(TimeSync.IsClockNoticeablyWrong);
        Assert.InRange(TimeSync.Offset.TotalMinutes, 8.5, 9.5);

        // And the corrected clock reads real time rather than the machine's.
        Assert.True((TimeSync.UtcNow - real).Duration() < TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// THE REGRESSION THAT MATTERS. One source claiming a huge error, while the others say the clock is
    /// fine, must change nothing. This is precisely what happened for real: timeapi.io said 21 minutes,
    /// Google, Microsoft and Cloudflare said zero.
    /// </summary>
    [Fact]
    public async Task OneLyingSourceCannotMoveTheClock()
    {
        var now = DateTimeOffset.UtcNow;

        using var http = new HttpClient(new Stub(r =>
            r.RequestUri!.AbsoluteUri.Contains("cdn-cgi/trace", StringComparison.Ordinal)
                ? WithTrace(now - TimeSpan.FromMinutes(21) - TimeSpan.FromSeconds(27))   // the liar
                : WithDate(now)));                                                        // the honest majority

        await TimeSync.SynchroniseAsync(http);

        // Sources disagree far beyond tolerance, so nothing is applied at all.
        Assert.False(TimeSync.IsSynced);
        Assert.Equal(TimeSpan.Zero, TimeSync.Offset);
        Assert.True((TimeSync.UtcNow - now).Duration() < TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// THE HEADLINE FALLBACK: no internet means system time, quietly. Offline is normal for a desktop app
    /// and a countdown must never wait on a time server.
    /// </summary>
    [Fact]
    public async Task WithNoInternetItFallsBackToSystemTime()
    {
        using var http = new HttpClient(new Stub(_ => throw new HttpRequestException("no network")));

        await TimeSync.SynchroniseAsync(http);

        Assert.False(TimeSync.IsSynced);
        Assert.Equal(TimeSpan.Zero, TimeSync.Offset);
        Assert.Equal(0, TimeSync.AgreeingSources);
        Assert.True((TimeSync.UtcNow - DateTimeOffset.UtcNow).Duration() < TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Too few answers is the same as none. Two hosts could agree by coincidence, and there is no median
    /// worth the name, so the threshold is three.
    /// </summary>
    [Fact]
    public async Task TooFewSourcesMeansNoCorrection()
    {
        var real = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(9);
        var answered = 0;

        using var http = new HttpClient(new Stub(r =>
        {
            // Only the first two calls succeed.
            if (++answered > 2)
            {
                throw new HttpRequestException("unreachable");
            }

            return r.RequestUri!.AbsoluteUri.Contains("cdn-cgi/trace", StringComparison.Ordinal)
                ? WithTrace(real)
                : WithDate(real);
        }));

        await TimeSync.SynchroniseAsync(http);

        Assert.False(TimeSync.IsSynced);
    }

    /// <summary>
    /// An absurd reading is a bad measurement, not a very wrong clock. Adopting one would put every
    /// countdown out by days while looking authoritative.
    /// </summary>
    [Fact]
    public async Task ImplausibleReadingsAreDiscarded()
    {
        using var http = AllSaying(DateTimeOffset.UtcNow.AddDays(-40));

        await TimeSync.SynchroniseAsync(http);

        Assert.False(TimeSync.IsSynced);
        Assert.Equal(0, TimeSync.AgreeingSources);
    }

    /// <summary>A malformed trace body must not be mistaken for a time.</summary>
    [Fact]
    public async Task AMalformedCloudflareBodyIsIgnoredWithoutBreakingTheRest()
    {
        var now = DateTimeOffset.UtcNow;

        using var http = new HttpClient(new Stub(r =>
            r.RequestUri!.AbsoluteUri.Contains("cdn-cgi/trace", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ts=not-a-number\nfl=x") }
                : WithDate(now)));

        await TimeSync.SynchroniseAsync(http);

        // The three Date-header hosts still form a quorum on their own.
        Assert.Equal(3, TimeSync.AgreeingSources);
        Assert.False(TimeSync.IsClockNoticeablyWrong);
    }

    /// <summary>
    /// A correction survives the player fixing their clock mid-session, because time is advanced from a
    /// captured instant by a monotonic stopwatch rather than by adding an offset to the system clock. A
    /// stored offset would be applied on top of the now-correct clock and make it wrong again.
    /// </summary>
    [Fact]
    public async Task CorrectedTimeAdvancesMonotonically()
    {
        using var http = AllSaying(DateTimeOffset.UtcNow + TimeSpan.FromMinutes(9));

        await TimeSync.SynchroniseAsync(http);
        Assert.True(TimeSync.IsSynced);

        var first = TimeSync.UtcNow;
        await Task.Delay(50);
        var second = TimeSync.UtcNow;

        Assert.True(second > first, "corrected time did not advance");
        Assert.True(second - first < TimeSpan.FromSeconds(5));
    }

    /// <summary>Reset returns to system time, so a stale correction cannot outlive its session.</summary>
    [Fact]
    public async Task ResetReturnsToSystemTime()
    {
        using var http = AllSaying(DateTimeOffset.UtcNow + TimeSpan.FromMinutes(9));

        await TimeSync.SynchroniseAsync(http);
        Assert.True(TimeSync.IsSynced);

        TimeSync.Reset();

        Assert.False(TimeSync.IsSynced);
        Assert.Equal(TimeSpan.Zero, TimeSync.Offset);
        Assert.True((TimeSync.UtcNow - DateTimeOffset.UtcNow).Duration() < TimeSpan.FromSeconds(5));
    }
}
