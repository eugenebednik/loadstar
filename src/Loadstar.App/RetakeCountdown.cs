namespace Loadstar.App;

/// <summary>
/// The gap between "retake this" and the shutter firing: a small topmost panel that counts down while
/// the player switches back to the game and opens the screen they need.
///
/// <para><b>Why a delay at all.</b> The capture happens before the Ask dialog opens, because opening a
/// window first would put Loadstar over the game. A retake inherits that ordering, so the dialog has to
/// get out of the way and the player needs a moment to bring the game forward and navigate — the whole
/// reason they are retaking is that the wrong screen was showing.</para>
///
/// <para><b>Why it is visible rather than a silent sleep.</b> A countdown the player cannot see is
/// indistinguishable from the app having hung, and they have just been told the previous capture was
/// wrong — the last thing that moment needs is ambiguity. It also names the screen to open, so the
/// instruction is in front of them while they act on it rather than in a dialog that has closed.</para>
///
/// <para>Escape or a click cancels. The player who clicks Retake by accident should not be made to sit
/// through it.</para>
/// </summary>
internal sealed class RetakeCountdown : Form
{
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    /// <summary>
    /// Six seconds. Long enough to alt-tab and open a menu, short enough not to feel like a punishment.
    /// A player who needs longer can retake again, which is cheap now — that is the point of the feature.
    /// </summary>
    internal const int Seconds = 6;

    private readonly System.Windows.Forms.Timer _tick;
    private readonly Label _counter;
    private int _remaining = Seconds;

    /// <summary>What the player should open before the capture fires, or null for a generic prompt.</summary>
    public RetakeCountdown(string? whatToOpen)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(420, 96);
        BackColor = Theme.Background;
        DoubleBuffered = true;

        // Top centre of the primary screen: out of the way of the character sheet, which the player is
        // about to open, and clear of the bottom-left corner where chat sits.
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(screen.X + ((screen.Width - ClientSize.Width) / 2), screen.Y + 80);

        var instruction = new Label
        {
            Text = string.IsNullOrWhiteSpace(whatToOpen)
                ? Strings.Get("retake.generic")
                : string.Format(Strings.Get("retake.open"), whatToOpen),
            Dock = DockStyle.Top,
            // Two lines and measured: this is a whole sentence, it is BOLD so its line height exceeds the
            // UI font's, and it names a screen whose name varies in length. The German and Russian
            // versions both wrap.
            Height = Theme.RowHeight(lines: 2, extra: 14),
            Padding = new Padding(16, 12, 16, 0),
            ForeColor = Theme.Text,
            Font = new Font(Theme.UiFont, FontStyle.Bold),
            BackColor = Color.Transparent,
        };

        _counter = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 0, 16, 8),
            ForeColor = Theme.SubtleText,
            BackColor = Color.Transparent,
        };

        UpdateCounter();

        Controls.Add(_counter);
        Controls.Add(instruction);

        _tick = new System.Windows.Forms.Timer { Interval = 1000 };
        _tick.Tick += (_, _) =>
        {
            _remaining--;

            if (_remaining <= 0)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            UpdateCounter();
        };

        // Either gesture cancels. Clicking is the obvious one; Escape is the one a keyboard user reaches
        // for, and this window never takes focus so it has to be caught at the form level.
        Click += (_, _) => Cancel();
        _counter.Click += (_, _) => Cancel();
        instruction.Click += (_, _) => Cancel();
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Cancel();
            }
        };

        Shown += (_, _) => _tick.Start();
    }

    private void Cancel()
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void UpdateCounter() =>
        _counter.Text = string.Format(Strings.Get("retake.counting"), _remaining);

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;

            // NOACTIVATE so showing this does not steal focus from the game the player is about to
            // navigate — taking focus would be actively counterproductive here. TOOLWINDOW keeps it out
            // of Alt-Tab, which matters because Alt-Tab is exactly what they are about to press.
            parameters.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;

            return parameters;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tick.Stop();
            _tick.Dispose();
        }

        base.Dispose(disposing);
    }
}
