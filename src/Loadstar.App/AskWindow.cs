namespace Loadstar.App;

/// <summary>
/// The prompt box shown after a hotkey capture: "what do you want to know about this screen?"
///
/// <para>This is the interaction the whole product turns on. A periodic advisor can only ever answer
/// "what's my best next step"; letting the player type the question is what makes
/// "what should I set my stat points to" and "best way to progress the gear I have equipped"
/// different queries against the same screenshot.</para>
///
/// <para>The screenshot is taken <em>before</em> this window opens, deliberately. Opening a dialog
/// first would put Loadstar's own window over the game and capture that instead of what the player
/// was looking at.</para>
/// </summary>
internal sealed class AskWindow : ThemedForm
{
    private static readonly string[] StarterQuestions =
    [
        "What should I set my stat points to?",
        "Best way to progress the gear I have equipped?",
        "What's the highest-value thing to do next?",
    ];

    private readonly TextBox _question;
    private readonly PictureBox _preview;

    public string Question => _question.Text.Trim();

    public AskWindow(Image preview, string windowTitle, IReadOnlyList<string> recentQuestions)
    {
        Text = "Loadstar";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(760, 620);
        TopMost = true;

        var heading = CreateHeading("Ask about this screen");

        var caption = new Label
        {
            Text = $"Captured from \"{windowTitle}\" — this exact image is what gets sent.",
            Dock = DockStyle.Top,
            Height = 24,
            Padding = new Padding(16, 0, 16, 0),
            AutoEllipsis = true,
            ForeColor = Theme.SubtleText,
        };

        // Showing exactly what will be sent is the capture indicator the posture document requires,
        // in its most literal form: the user sees the image before it leaves the machine.
        _preview = new PictureBox
        {
            Image = preview,
            SizeMode = PictureBoxSizeMode.Zoom,
            Dock = DockStyle.Fill,
            BackColor = Theme.PreviewBackdrop,
        };

        var previewFrame = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 8, 16, 8),
            BackColor = Theme.Background,
        };

        previewFrame.Controls.Add(_preview);

        _question = new TextBox
        {
            Multiline = true,
            Dock = DockStyle.Fill,
            Font = Theme.UiFont,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "Ask anything, or leave blank for a general review.  (Ctrl+Enter to send)",
        };

        var suggestions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            Padding = new Padding(0, 0, 0, 6),
            WrapContents = false,
            AutoScroll = false,
            BackColor = Theme.Background,
        };

        // Recent questions first, then starters — so the box is useful on first run and faster on
        // every run after it.
        foreach (var suggestion in recentQuestions.Concat(StarterQuestions).Distinct().Take(3))
        {
            var chip = new Button
            {
                Text = Shorten(suggestion),
                AutoSize = true,
                Tag = suggestion,
                Height = 26,
                Margin = new Padding(0, 0, 6, 0),
            };

            Theme.MakeSecondary(chip);
            chip.Click += (_, _) => { _question.Text = (string)chip.Tag!; _question.SelectionStart = _question.TextLength; };
            suggestions.Controls.Add(chip);
        }

        var ask = new Button { Text = "Ask", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        Theme.MakePrimary(ask);
        Theme.MakeSecondary(cancel);

        var lower = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 150,
            Padding = new Padding(16, 4, 16, 4),
            BackColor = Theme.Background,
        };

        lower.Controls.Add(_question);
        lower.Controls.Add(suggestions);

        Controls.Add(previewFrame);
        Controls.Add(lower);
        Controls.Add(CreateActionBar(ask, cancel));
        Controls.Add(caption);
        Controls.Add(heading);

        AcceptButton = ask;
        CancelButton = cancel;

        // Ctrl+Enter submits, so a multiline box does not trap the user into reaching for the mouse.
        _question.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter)
            {
                DialogResult = DialogResult.OK;
                e.SuppressKeyPress = true;
            }
        };

        Shown += (_, _) => { Activate(); _question.Focus(); };
    }

    private static string Shorten(string value) =>
        value.Length <= 34 ? value : value[..31] + "…";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // The preview owns a clone of the capture; releasing it here keeps a long session from
            // accumulating full-resolution bitmaps.
            _preview.Image?.Dispose();
        }

        base.Dispose(disposing);
    }
}
