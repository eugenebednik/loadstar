using Loadstar.Core.Capture;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace Loadstar.Capture.Windows;

/// <summary>
/// Captures the game window with Windows Graphics Capture.
///
/// <para>This is the whole reason the project can exist without touching the game. WGC is the
/// public API behind Game Bar, OBS and Discord's screen share: the caller names a window, the
/// compositor copies that window's output into a surface it owns, and the captured application is
/// never involved. No code runs inside the game, nothing is hooked, and no elevation is needed.
/// Contrast the injecting approach — wrapping the game's <c>Present</c> — which is on the
/// forbidden list precisely because Easy Anti-Cheat is built to spot it.</para>
///
/// <para>Two consequences of doing it honestly, both accepted rather than worked around:</para>
/// <list type="bullet">
/// <item>A game in <b>exclusive fullscreen</b> yields no frames. That surfaces as
/// <see cref="CaptureStatus.TimedOut"/> with an explanation pointing at borderless windowed mode.
/// The fix that would make it work is renderer hooking, so there is no fix.</item>
/// <item>Windows draws its own capture border on recent builds. It is left on. Suppressing it is
/// possible, but the posture document promises the user always knows when their screen is being
/// read, and the OS telling them directly is the strongest form of that promise.</item>
/// </list>
/// </summary>
public sealed class WindowsGraphicsCaptureSource : ICaptureSource
{
    /// <summary>
    /// Frames to let through before keeping one. The first frame after a session starts can carry
    /// pre-composition content for a window that just changed; the second is reliable. If only one
    /// arrives before the timeout we use it rather than failing.
    /// </summary>
    private const int FramesToSettle = 2;

    private readonly Lazy<CaptureDevice> _device;
    private bool _disposed;

    public WindowsGraphicsCaptureSource()
    {
        // Deferred so that constructing the source on a machine that cannot capture is harmless —
        // the console app wants to report that cleanly, not crash at startup.
        _device = new Lazy<CaptureDevice>(CaptureDevice.Create, isThreadSafe: true);
    }

    public string Name => "Windows Graphics Capture";

    public bool IsSupported
    {
        get
        {
            try
            {
                return GraphicsCaptureSession.IsSupported();
            }
            catch (TypeLoadException)
            {
                // Covers EntryPointNotFoundException too — both mean the API is not on this build.
                return false;
            }
        }
    }

    public async Task<CaptureResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsSupported)
        {
            return CaptureResult.Fail(
                CaptureStatus.Unsupported,
                "Windows Graphics Capture is not available on this system. It needs Windows 10 " +
                "version 2004 (build 19041) or newer.");
        }

        var window = GameWindowLocator.Find(request.Target);

        if (window is null)
        {
            var rejected = GameWindowLocator.RejectedTitleMatches(request.Target);

            var detail = $"No window matched {request.Target}. Is the game running?";

            if (rejected.Count > 0)
            {
                // Worth naming: this is exactly how a browser tab showing a build guide gets
                // mistaken for the game, and seeing it told the user to configure a process name.
                detail += $" Ignored {rejected.Count} title match(es) belonging to " +
                    string.Join(", ", rejected.Select(w => w.ProcessName).Distinct()) +
                    " — configure the game's process name to target it directly.";
            }

            return CaptureResult.Fail(CaptureStatus.WindowNotFound, detail);
        }

        try
        {
            return await CaptureWindowAsync(window, request, cancellationToken).ConfigureAwait(false);
        }
        catch (CaptureException ex)
        {
            return CaptureResult.Fail(CaptureStatus.Failed, ex.Message);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            return CaptureResult.Fail(
                CaptureStatus.Failed,
                $"Capture failed for \"{window.Title}\": {ex.Message}");
        }
    }

    private async Task<CaptureResult> CaptureWindowAsync(
        GameWindow window,
        CaptureRequest request,
        CancellationToken cancellationToken)
    {
        var item = GraphicsCaptureItemFactory.CreateForWindow(window.Handle);

        // item.Size is the window's own dimensions, which is what the fractional regions in
        // settings are expressed against — so geometry never has to care about DPI or where the
        // window sits on the desktop.
        var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _device.Value.Device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            numberOfBuffers: 2,
            size: item.Size);

        GraphicsCaptureSession? session = null;

        var gate = new object();
        Direct3D11CaptureFrame? latest = null;
        var frameCount = 0;
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnFrameArrived(Direct3D11CaptureFramePool pool, object _)
        {
            var frame = pool.TryGetNextFrame();

            if (frame is null)
            {
                return;
            }

            lock (gate)
            {
                latest?.Dispose();
                latest = frame;
                frameCount++;

                if (frameCount >= FramesToSettle)
                {
                    settled.TrySetResult();
                }
            }
        }

        void OnClosed(GraphicsCaptureItem sender, object _) => closed.TrySetResult();

        try
        {
            framePool.FrameArrived += OnFrameArrived;
            item.Closed += OnClosed;

            session = framePool.CreateCaptureSession(item);
            session.StartCapture();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delay = Task.Delay(request.Timeout, timeout.Token);

            var finished = await Task.WhenAny(settled.Task, closed.Task, delay).ConfigureAwait(false);
            await timeout.CancelAsync().ConfigureAwait(false);

            if (finished == closed.Task)
            {
                return CaptureResult.Fail(
                    CaptureStatus.Failed,
                    $"The window \"{window.Title}\" closed while it was being captured.");
            }

            Direct3D11CaptureFrame? frame;

            lock (gate)
            {
                frame = latest;
                latest = null;
            }

            if (frame is null)
            {
                return CaptureResult.Fail(
                    CaptureStatus.TimedOut,
                    $"\"{window.Title}\" produced no frame within {request.Timeout.TotalSeconds:0.#}s. " +
                    "The window may be minimised, or the game may be in exclusive fullscreen — " +
                    "switch it to borderless windowed mode. Loadstar will not hook the renderer to " +
                    "work around this.");
            }

            using (frame)
            {
                return await FrameEncoder
                    .EncodeAsync(frame, window, request, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            framePool.FrameArrived -= OnFrameArrived;
            item.Closed -= OnClosed;

            lock (gate)
            {
                latest?.Dispose();
                latest = null;
            }

            // Order matters: the session stops producing before the pool it feeds goes away.
            session?.Dispose();
            framePool.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_device.IsValueCreated)
        {
            _device.Value.Dispose();
        }
    }
}
