using System.Runtime.InteropServices;

namespace Loadstar.App;

/// <summary>
/// The shell's entire native surface: two functions, both for global hotkeys.
///
/// <para>docs/anti-cheat-posture.md permits exactly this — "Global hotkeys via
/// <c>RegisterHotKey</c>, which routes through the OS, not the game." The distinction that makes it
/// allowed is direction: the OS notifies <em>us</em> when the user presses a combination. Nothing is
/// sent to the game, no game window is addressed, and no input is synthesised.</para>
///
/// <para>Adding <c>user32</c> here deliberately trips <c>AntiCheatPostureTests</c>, which keeps a
/// module allowlist and a recorded baseline. That is the intended workflow rather than an
/// inconvenience: widening the native surface is meant to show up as an explicit diff a reviewer has
/// to approve. What must never appear alongside these is the rest of <c>user32</c> — <c>SendInput</c>,
/// <c>PostMessage</c>, <c>SendMessage</c>, <c>SetWindowsHookEx</c>, <c>SetForegroundWindow</c> — all
/// of which are on the test's denylist and stay there.</para>
/// </summary>
internal static class NativeMethods
{
    internal const int WM_HOTKEY = 0x0312;

    /// <summary>
    /// The shutdown messages an installer or Windows Restart Manager uses to ask an app to close.
    ///
    /// <para>Constants only — no new P/Invoke, so nothing here widens the native surface the anti-cheat
    /// posture test guards. Loadstar RECEIVES these; it never sends them, which is the distinction that
    /// matters: <c>SendMessage</c> and <c>PostMessage</c> are on that test's denylist precisely because
    /// sending synthetic messages to another process is what gets accounts flagged.</para>
    /// </summary>
    internal const int WM_CLOSE = 0x0010;

    internal const int WM_QUERYENDSESSION = 0x0011;

    internal const int WM_ENDSESSION = 0x0016;

    [Flags]
    internal enum HotkeyModifiers : uint
    {
        None = 0,
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Win = 0x0008,

        /// <summary>Stops the hotkey auto-repeating while the key is held down.</summary>
        NoRepeat = 0x4000,
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // -----------------------------------------------------------------------
    // dwmapi — title bar colour only.
    //
    // WinForms styles a window's client area but not its title bar, so a dark form otherwise gets a
    // white caption and looks broken. This is the documented way to ask the desktop compositor to
    // draw OUR OWN window's caption dark. It touches nothing but Loadstar's windows and reads
    // nothing back.
    // -----------------------------------------------------------------------

    /// <summary>DWMWA_USE_IMMERSIVE_DARK_MODE. 19 on Windows 10 builds before 20H1, 20 after.</summary>
    internal const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    internal const int DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY = 19;

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
