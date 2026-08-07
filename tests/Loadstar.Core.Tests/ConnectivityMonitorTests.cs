using Loadstar.Core.Net;

using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// The asymmetry is the whole design, so most of these test it: being wrong about OFFLINE disables the only
/// button that does anything and looks like a broken app, while being wrong about ONLINE costs one error
/// message. Every ambiguous case must therefore resolve to online.
/// </summary>
public class ConnectivityMonitorTests
{
    private static ConnectivityMonitor Monitor(
        Func<bool> reachable, Func<bool>? interfaceUp = null) =>
        new(_ => Task.FromResult(reachable()), interfaceUp ?? (() => true));

    [Fact]
    public void ItStartsOnlineBeforeAnythingHasBeenChecked()
    {
        using var monitor = Monitor(() => true);

        Assert.True(monitor.IsOnline, "a monitor that has never probed must not disable the app");
    }

    /// <summary>
    /// One failure is not proof. A DNS hiccup or a radio settling after resume would otherwise flicker
    /// the button off and on.
    /// </summary>
    [Fact]
    public async Task OneFailureIsNotEnoughToGoOffline()
    {
        using var monitor = Monitor(() => false);

        await monitor.RefreshAsync();

        Assert.True(monitor.IsOnline);

        await monitor.RefreshAsync();

        Assert.False(monitor.IsOnline);
    }

    [Fact]
    public async Task RecoveryIsImmediate()
    {
        var reachable = false;
        using var monitor = Monitor(() => reachable);

        await monitor.RefreshAsync();
        await monitor.RefreshAsync();

        Assert.False(monitor.IsOnline);

        reachable = true;
        await monitor.RefreshAsync();

        Assert.True(monitor.IsOnline, "one success must restore the button, not two");
    }

    /// <summary>A near miss must not accumulate: two failures separated by a success are not two in a row.</summary>
    [Fact]
    public async Task FailuresMustBeConsecutive()
    {
        var reachable = false;
        using var monitor = Monitor(() => reachable);

        await monitor.RefreshAsync();

        reachable = true;
        await monitor.RefreshAsync();

        reachable = false;
        await monitor.RefreshAsync();

        Assert.True(monitor.IsOnline, "the failure counter did not reset on success");
    }

    /// <summary>
    /// The one signal trusted instantly. "No interface is up" comes from the machine rather than being
    /// inferred from a failed request, and it is the common case — unplugged cable, dropped wifi.
    /// </summary>
    [Fact]
    public async Task NoNetworkInterfaceGoesOfflineAtOnce()
    {
        using var monitor = Monitor(() => true, interfaceUp: () => false);

        await monitor.RefreshAsync();

        Assert.False(monitor.IsOnline);
    }

    /// <summary>And the probe must not even be attempted — there is nowhere for it to go.</summary>
    [Fact]
    public async Task NoInterfaceSkipsTheProbeEntirely()
    {
        var probed = 0;

        using var monitor = new ConnectivityMonitor(
            _ => { probed++; return Task.FromResult(true); },
            interfaceUp: () => false);

        await monitor.RefreshAsync();

        Assert.Equal(0, probed);
    }

    /// <summary>
    /// A probe that throws is a broken probe as often as it is a broken network, and it must never escape:
    /// this runs from a timer, where an unhandled exception ends the process.
    /// </summary>
    [Fact]
    public async Task AThrowingProbeIsAFailureAndNotACrash()
    {
        using var monitor = new ConnectivityMonitor(
            _ => throw new HttpRequestException("no such host"),
            interfaceUp: () => true);

        await monitor.RefreshAsync();
        await monitor.RefreshAsync();

        Assert.False(monitor.IsOnline);
    }

    /// <summary>
    /// A throwing interface check tells us nothing about the network, so it must resolve to online rather
    /// than disabling the app because a Win32 call misbehaved.
    /// </summary>
    [Fact]
    public async Task AThrowingInterfaceCheckDoesNotDisableTheApp()
    {
        using var monitor = new ConnectivityMonitor(
            _ => Task.FromResult(true),
            interfaceUp: () => throw new InvalidOperationException("WMI is unhappy"));

        await monitor.RefreshAsync();

        Assert.True(monitor.IsOnline);
    }

    /// <summary>Cancellation is shutdown, not evidence — it must not count toward going offline.</summary>
    [Fact]
    public async Task CancellationIsNotAFailure()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        using var monitor = new ConnectivityMonitor(
            ct => Task.FromCanceled<bool>(ct),
            interfaceUp: () => true);

        await monitor.RefreshAsync(cts.Token);
        await monitor.RefreshAsync(cts.Token);

        Assert.True(monitor.IsOnline);
    }

    /// <summary>
    /// Subscribers redraw on this, so it must fire on transitions only. Firing per poll would repaint a
    /// button several times a minute to say nothing changed.
    /// </summary>
    [Fact]
    public async Task ChangedFiresOnTransitionsOnly()
    {
        var reachable = false;
        var events = new List<bool>();

        using var monitor = Monitor(() => reachable);

        monitor.Changed += (_, online) => events.Add(online);

        await monitor.RefreshAsync();
        await monitor.RefreshAsync();
        await monitor.RefreshAsync();
        await monitor.RefreshAsync();

        reachable = true;
        await monitor.RefreshAsync();
        await monitor.RefreshAsync();

        Assert.Equal([false, true], events);
    }
}
