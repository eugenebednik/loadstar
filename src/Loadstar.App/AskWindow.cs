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
    /// <summary>
    /// The starter questions, in the player's own language.
    ///
    /// <para>A property rather than a static field: <see cref="Strings"/> resolves against the language
    /// selected in settings, and a static initialiser would freeze whichever language happened to be
    /// current when the type was first touched. That is the kind of bug that only shows up after
    /// someone changes language and reopens the dialog.</para>
    /// </summary>
    private static string[] StarterQuestions =>
    [
        Strings.Get("ask.starter.stats"),
        Strings.Get("ask.starter.gear"),
        Strings.Get("ask.starter.next"),
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

        var heading = CreateHeading(Strings.Get("ask.title"));

        var caption = new Label
        {
            Text = string.Format(Strings.Get("ask.caption"), windowTitle),
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
            PlaceholderText = Strings.Get("ask.placeholder"),
        };

        // CLICKABLE LABELS, NOT BUTTONS, and stacked rather than in a row.
        //
        // These were secondary-styled Buttons laid out horizontally, which read as a tab strip —
        // three rectangles in a row above a content area is the tab idiom whether or not that was the
        // intent, and it invited being clicked as a mode switch rather than as text to insert.
        //
        // The row also forced truncation. Three questions of ~45 characters do not fit across 760px as
        // buttons, so each was cut to 31 characters and given an ellipsis: "What should I set my stat
        // poin…". A suggestion you cannot read is not a suggestion. Stacking them vertically gives each
        // its full width, so the text is complete and Shorten() is gone.
        var suggestions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 0, 0, 8),
            BackColor = Theme.Background,
        };

        // Recent questions first, then starters — so the box is useful on first run and faster on
        // every run after it.
        foreach (var suggestion in recentQuestions.Concat(StarterQuestions).Distinct().Take(3))
        {
            var link = new LinkLabel
            {
                Text = suggestion,
                AutoSize = true,
                Tag = suggestion,
                Margin = new Padding(2, 0, 0, 4),
                // A pathologically long recent question wraps rather than pushing the dialog wider.
                MaximumSize = new Size(ClientSize.Width - 48, 0),
                Cursor = Cursors.Hand,
            };

            // Colours and hover behaviour come from Theme.Apply's LinkLabel case, so every link in the
            // app looks the same and none of them carries its own palette.
            link.LinkClicked += (_, _) =>
            {
                _question.Text = (string)link.Tag!;
                _question.Focus();
                _question.SelectionStart = _question.TextLength;
            };

            suggestions.Controls.Add(link);
        }

        // These four strings were already translated into all nine languages and the dialog simply
        // never asked for them, so a player with Russian selected got an English dialog. Wiring them
        // up costs nothing and was the whole gap.
        var ask = new Button { Text = Strings.Get("ask.send"), DialogResult = DialogResult.OK };
        var cancel = new Button { Text = Strings.Get("common.cancel"), DialogResult = DialogResult.Cancel };

        Theme.MakePrimary(ask);
        Theme.MakeSecondary(cancel);

        // Taller than before, because the stacked suggestions occupy three lines where the old chip row
        // occupied one. Sized so the text box keeps roughly the height it had rather than being
        // squeezed — the last thing this dialog needs is a cramped box the user is meant to type into.
        var lower = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 210,
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
