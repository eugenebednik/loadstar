using Loadstar.Core.Configuration;

namespace Loadstar.App;

/// <summary>
/// A hidden window that owns the global hotkey registrations.
///
/// <para><c>RegisterHotKey</c> delivers <c>WM_HOTKEY</c> to a window's message queue, so something
/// has to have a queue and pump it. A tray app has no visible window, hence this one — created,
/// never shown, and never given a taskbar entry.</para>
///
/// <para>Registration failure is expected and handled rather than thrown: another application may
/// already own the combination, and the right response is to tell the user which one failed so they
/// can pick another, not to refuse to start.</para>
/// </summary>
internal sealed class HotkeyHost : Form
{
    private readonly Dictionary<int, Action> _handlers = [];
    private int _nextId = 1;

    public HotkeyHost()
    {
        // Never visible. CreateHandle is forced so a message queue exists before anything registers.
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
        Opacity = 0;

        CreateHandle();
    }

    /// <summary>
    /// Registers a hotkey. Returns null on success, or a human-readable reason on failure.
    /// </summary>
    public string? TryRegister(Hotkey hotkey, Action onPressed)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        ArgumentNullException.ThrowIfNull(onPressed);

        var id = _nextId++;

        // NoRepeat: holding the combination should fire one capture, not a stream of them.
        var modifiers = hotkey.Modifiers | (uint)NativeMethods.HotkeyModifiers.NoRepeat;

        if (!NativeMethods.RegisterHotKey(Handle, id, modifiers, hotkey.VirtualKey))
        {
            return $"{hotkey.Display} is already in use by another application.";
        }

        _handlers[id] = onPressed;
        return null;
    }

    /// <summary>
    /// Releases every registration but keeps the window alive.
    ///
    /// <para>This exists because re-registering after a settings change must NOT dispose the host:
    /// the window owns the message queue the registrations are delivered to, so disposing it and
    /// then registering again throws <see cref="ObjectDisposedException"/> on the next
    /// <see cref="Control.Handle"/> access. That was a real crash on every Save.</para>
    /// </summary>
    public void UnregisterAll()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        foreach (var id in _handlers.Keys)
        {
            NativeMethods.UnregisterHotKey(Handle, id);
        }

        _handlers.Clear();

        // Ids are not reused. Windows keys registrations by (window, id), and recycling an id that
        // a pending message still refers to would deliver the old hotkey to the new handler.
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY &&
            _handlers.TryGetValue((int)m.WParam, out var handler))
        {
            // Never let a handler exception escape into the message loop — an unhandled one here
            // takes down the whole tray app, and the user's only symptom is that Loadstar vanished.
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                TrayApplication.ReportError("Hotkey handler failed", ex);
            }

            return;
        }

        base.WndProc(ref m);
    }

    protected override void SetVisibleCore(bool value)
    {
        // Swallows any attempt to show this window, including the framework's own on first run.
        base.SetVisibleCore(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && IsHandleCreated)
        {
            foreach (var id in _handlers.Keys)
            {
                NativeMethods.UnregisterHotKey(Handle, id);
            }

            _handlers.Clear();
        }

        base.Dispose(disposing);
    }
}
