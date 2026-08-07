using System.Net.NetworkInformation;

namespace Loadstar.Core.Net;

/// <summary>
/// Whether a request could reach the internet right now, so the app can refuse to send one it knows will
/// fail instead of charging the player a screenshot and a wait for a timeout.
///
/// <para><b>It defaults to ONLINE and biases that way throughout.</b> The two ways of being wrong are not
/// symmetrical: a false "offline" disables the only button that does anything, which is indistinguishable
/// from a broken app and cannot be worked around, while a false "online" costs one error message the app
/// already knows how to show. So the probe has to prove absence, never presence — one failed request is
/// not proof, and <see cref="FailuresBeforeOffline"/> exists for exactly that reason.</para>
///
/// <para><b>The OS signal is trusted immediately, though.</b> "No network interface is up" is a direct
/// answer from the machine rather than an inference from a failed request, and it is the common case —
/// closed laptop, unplugged cable, dropped wifi. That flips to offline with no probe at all.</para>
///
/// <para><b>What it does NOT tell you.</b> That the AI provider is reachable and working. A firewall that
/// permits general traffic and blocks one API host, an expired key, a provider outage — none of those are
/// visible here, and all of them already surface as errors from the request itself. This answers "is there
/// internet", which is the question that makes pressing Ask pointless.</para>
/// </summary>
public sealed class ConnectivityMonitor : IDisposable
{
    /// <summary>
    /// How many probe failures in a row before believing them.
    ///
    /// <para>Two, not one. A single failure is routine — a DNS hiccup, a captive portal mid-handshake, a
    /// laptop's radio settling after a resume — and treating it as proof would flicker the button.</para>
    /// </summary>
    public const int FailuresBeforeOffline = 2;

    private readonly Func<CancellationToken, Task<bool>> _probe;
    private readonly Func<bool> _interfaceUp;

    private int _failures;
    private bool _disposed;

    /// <param name="probe">Performs one reachability check. True means something answered.</param>
    /// <param name="interfaceUp">
    /// Whether the machine has any usable network interface. Separated so it can be faked in tests, and
    /// because it is the one signal worth acting on instantly.
    /// </param>
    public ConnectivityMonitor(
        Func<CancellationToken, Task<bool>> probe,
        Func<bool>? interfaceUp = null)
    {
        ArgumentNullException.ThrowIfNull(probe);

        _probe = probe;
        _interfaceUp = interfaceUp ?? NetworkInterface.GetIsNetworkAvailable;
    }

    /// <summary>Optimistic until something proves otherwise. See the type remarks.</summary>
    public bool IsOnline { get; private set; } = true;

    /// <summary>
    /// Raised when <see cref="IsOnline"/> changes, and only then — subscribers redraw on this, so firing
    /// on every poll would repaint a button several times a minute for no reason.
    ///
    /// <para><b>Raised on whatever thread the refresh ran on.</b> A UI subscriber must marshal.</para>
    /// </summary>
    public event EventHandler<bool>? Changed;

    /// <summary>
    /// Subscribes to the OS network-change notifications.
    ///
    /// <para>Worth doing on top of polling because it is free and instant: unplugging a cable is known
    /// immediately rather than at the next poll, and no request is made to discover it.</para>
    /// </summary>
    public void Start()
    {
        NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
        NetworkChange.NetworkAddressChanged += OnAddressChanged;
    }

    /// <summary>
    /// Checks once and updates <see cref="IsOnline"/>.
    ///
    /// <para>Never throws. A probe that fails for any reason counts as a failure, which is what a caller
    /// polling on a timer needs — an exception escaping into a timer callback takes the app down.</para>
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // No interface is a direct answer, so skip the probe: there is nowhere for it to go, and a
        // DNS timeout would take seconds to tell us what the OS already said.
        if (!SafeInterfaceUp())
        {
            _failures = FailuresBeforeOffline;
            Set(false);

            return;
        }

        bool reachable;

        try
        {
            reachable = await _probe(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down, or the caller moved on. Not evidence about the network.
            return;
        }
        catch
        {
            reachable = false;
        }

        if (reachable)
        {
            // Recovery is instant and unconditional. Making someone wait two successful polls to regain
            // the button would be the same mistake as trusting one failure, in the direction that hurts.
            _failures = 0;
            Set(true);

            return;
        }

        _failures++;

        if (_failures >= FailuresBeforeOffline)
        {
            Set(false);
        }
    }

    private bool SafeInterfaceUp()
    {
        try
        {
            return _interfaceUp();
        }
        catch
        {
            // Cannot tell, so do not claim offline — the biased default again.
            return true;
        }
    }

    private void Set(bool online)
    {
        if (IsOnline == online)
        {
            return;
        }

        IsOnline = online;
        Changed?.Invoke(this, online);
    }

    /// <summary>
    /// The OS reporting no network is believed at once; the OS reporting network is only a reason to
    /// re-probe, because "an interface is up" is a long way from "packets reach the internet" — a captive
    /// portal or a connected-but-dead wifi satisfies it.
    /// </summary>
    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (!e.IsAvailable)
        {
            _failures = FailuresBeforeOffline;
            Set(false);

            return;
        }

        _failures = 0;
        _ = RefreshAsync();
    }

    private void OnAddressChanged(object? sender, EventArgs e) => _ = RefreshAsync();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Static events, so failing to unsubscribe keeps this object — and any window it is redrawing —
        // alive for the life of the process.
        NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
    }
}
