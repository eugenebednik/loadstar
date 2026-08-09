namespace Loadstar.App;

/// <summary>
/// Shows a message box that is actually visible over a fullscreen game.
///
/// <para><b>Why this exists.</b> <c>MessageBox.Show</c> with no owner parents itself to the active window,
/// which during play is the game — so the dialog opens BEHIND it and the player sees nothing at all. The
/// symptom is indistinguishable from the app silently doing nothing, and it was reported exactly that way:
/// "the window keeps coming back and no error is shown." The error was being shown; it was underneath
/// Throne and Liberty.</para>
///
/// <para><b>Why an owner form rather than <c>MessageBoxOptions</c>.</b> There is no topmost flag on
/// <see cref="MessageBox"/>. Passing an owner is the only supported way to control its z-order, so the owner
/// has to be a real <see cref="Form"/> that is itself <c>TopMost</c>. It is never shown and never painted —
/// it exists solely to be the thing the message box sits on top of. Same reason <c>ResultWindow</c> sets
/// <c>TopMost</c>: a foreground-locked process cannot be raised by asking politely, and
/// <c>SetForegroundWindow</c>/<c>Activate</c> quietly fail.</para>
///
/// <para><b>Size 0 at position (-32000, -32000)</b> so that even if a compositor or accessibility tool
/// decides to render it, there is nothing on screen and it is off every monitor.</para>
/// </summary>
internal static class Modal
{
    public static DialogResult Show(
        string text,
        string title,
        MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None)
    {
        // ShowInTaskbar false so the invisible owner never appears in the taskbar or Alt+Tab, which would
        // be a ghost entry the user cannot activate or close.
        using var owner = new Form
        {
            TopMost = true,
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            Size = new Size(0, 0),
        };

        // Shown, because an unshown form has no window handle and cannot own anything — MessageBox would
        // fall back to the active window, which is the bug this class exists to avoid.
        owner.Show();

        try
        {
            return MessageBox.Show(owner, text, title, buttons, icon);
        }
        finally
        {
            owner.Hide();
        }
    }
}
