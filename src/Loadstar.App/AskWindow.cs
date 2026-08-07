using Loadstar.Core.Capture;

namespace Loadstar.App;

/// <summary>
/// The prompt box shown after a hotkey capture: "what do you want to know about these screens?"
///
/// <para>This is the interaction the whole product turns on. A periodic advisor can only ever answer
/// "what's my best next step"; letting the player type the question is what makes
/// "what should I set my stat points to" and "best way to progress the gear I have equipped"
/// different queries against the same screenshot.</para>
///
/// <para>The screenshots are taken <em>before</em> this window opens, deliberately. Opening a dialog
/// first would put Loadstar's own window over the game and capture that instead of what the player
/// was looking at — which is also why adding a screen closes this window rather than capturing from
/// inside it.</para>
///
/// <para><b>Up to four screens, not one.</b> The advice regularly needs screens that cannot be open at
/// the same time, and it used to have to ask the player to go and look somewhere else and try again —
/// so it never saw two of them together. Each queued screen is shown as a thumbnail that can be
/// removed, because "here is what is about to be sent" is only an honest capture indicator if the
/// answer can still be no.</para>
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

    /// <summary>Result meaning "capture another screen and come back", from the button or the hotkey.</summary>
    public const DialogResult AddAnother = DialogResult.Ignore;

    private readonly TextBox _question;

    /// <summary>
    /// All queued screens at once, side by side, rather than one big preview plus a thumbnail strip.
    ///
    /// <para>A layout container and not absolute positions: the strip it replaces placed thumbnails by
    /// hand inside a plain Panel, which meant every count needed its own arithmetic. A
    /// TableLayoutPanel sizes the cells itself and cannot be off by a margin.</para>
    /// </summary>
    private readonly TableLayoutPanel _grid;

    private readonly Label _queued;

    /// <summary>
    /// Names what the delete button does. An icon-only control is unlabelled by definition, and a trash
    /// can on the one control that discards a capture deserves to be unambiguous.
    /// </summary>
    private readonly ToolTip _tips = new();

    /// <summary>The working set. Deletions happen here, and <see cref="Kept"/> is what survived.</summary>
    private readonly List<CapturedFrame> _frames;

    /// <summary>Decoded previews, indexed alongside <see cref="_frames"/>. Owned and disposed here.</summary>
    private readonly List<Image> _images = [];

    private int _selected;

    public string Question => _question.Text.Trim();

    /// <summary>
    /// The screens the player did not delete, in order. The caller adopts this rather than tracking
    /// indexes — index bookkeeping across a dialog that can both add and remove is how off-by-ones get in.
    /// </summary>
    public IReadOnlyList<CapturedFrame> Kept => _frames;

    /// <param name="frames">The queued screens, oldest first. At least one.</param>
    /// <param name="hotkeyDisplay">
    /// The capture hotkey, named in the label. The hotkey is the fast way to add a screen and it is
    /// invisible unless said out loud — a player who has to find the Add button every time will stop at one.
    /// </param>
    /// <param name="initialQuestion">
    /// Text to start with, used to carry a typed question across a RETAKE or an added screen. Losing what
    /// someone wrote because they fixed the screenshot would make those buttons feel like a punishment.
    /// </param>
    public AskWindow(
        IReadOnlyList<CapturedFrame> frames,
        string windowTitle,
        string hotkeyDisplay,
        IReadOnlyList<string> recentQuestions,
        string? initialQuestion = null)
    {
        ArgumentNullException.ThrowIfNull(frames);

        if (frames.Count == 0)
        {
            throw new ArgumentException("The ask window needs at least one screen to show.", nameof(frames));
        }

        _frames = [.. frames];
        _selected = _frames.Count - 1;

        Text = "Loadstar";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(820, 780);
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

        // The single most useful thing this dialog can say. The capture fires on the hotkey, so it
        // catches whatever was on screen — and the open world answers almost nothing, while the
        // character sheet carries item level per slot, Gear Score and the Equipment watermark. Players
        // hit the hotkey from wherever they happen to be standing and then wonder why the advice is
        // thin; saying so where the preview is visible is the moment it lands.
        var hint = new Label
        {
            Text = Strings.Get("ask.hint"),
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(16, 0, 16, 4),
            ForeColor = Theme.Accent,
            BackColor = Color.Transparent,
        };

        // Showing exactly what will be sent is the capture indicator the posture document requires,
        // in its most literal form: the user sees every image before any of them leave the machine.
        _grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
        };

        // TWO LINES, not one. At one line the Russian string was clipped mid-sentence — it is a whole
        // sentence in every language and several are longer than the English.
        _queued = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 38,
            ForeColor = Theme.SubtleText,
            BackColor = Color.Transparent,
            Tag = hotkeyDisplay,
        };

        var previewFrame = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 8, 16, 8),
            BackColor = Theme.Background,
        };

        previewFrame.Controls.Add(_grid);
        previewFrame.Controls.Add(_queued);

        _question = new TextBox
        {
            Multiline = true,
            Dock = DockStyle.Fill,
            Font = Theme.UiFont,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = Strings.Get("ask.placeholder"),
            Text = initialQuestion ?? string.Empty,
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

        // RETAKE, as DialogResult.Retry. The caller loops on it rather than the dialog re-capturing
        // itself, because the capture has to happen with this window out of the way — the same reason
        // the first capture happens before the dialog opens at all.
        //
        // Retake REPLACES the queue and Add appends to it. Both are needed: retake is for a screenshot
        // of the wrong screen, where keeping it would send the wrong screen alongside the right one.
        var retake = new Button { Text = Strings.Get("ask.retake"), DialogResult = DialogResult.Retry };

        var add = new Button { Text = Strings.Get("ask.shots.add"), DialogResult = AddAnother };

        Theme.MakePrimary(ask);
        Theme.MakeSecondary(cancel);
        Theme.MakeSecondary(retake);
        Theme.MakeSecondary(add);

        // Taller than before, because the stacked suggestions occupy three lines where the old chip row
        // occupied one. Sized so the text box keeps roughly the height it had rather than being
        // squeezed — the last thing this dialog needs is a cramped box the user is meant to type into.
        var lower = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 190,
            Padding = new Padding(16, 4, 16, 4),
            BackColor = Theme.Background,
        };

        lower.Controls.Add(_question);
        lower.Controls.Add(suggestions);

        Controls.Add(previewFrame);
        Controls.Add(lower);
        // RightToLeft flow: first argument lands rightmost. Ask keeps the corner, and the two capture
        // actions sit next to Cancel so a stray click near the primary action cannot fire one.
        Controls.Add(CreateActionBar(ask, add, retake, cancel));
        Controls.Add(hint);
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

        Rebuild();

        Shown += (_, _) =>
        {
            Activate();
            _question.Focus();
            // Caret after any restored text, so a carried-over question can be edited rather than
            // replaced by the first keystroke.
            _question.SelectionStart = _question.TextLength;
        };
    }

    /// <summary>
    /// Redraws every cell from <see cref="_frames"/>.
    ///
    /// <para>Rebuilds wholesale rather than patching, because a delete renumbers every screen after it
    /// and reusing controls means keeping their indexes in step with the list. Four cells is nothing to
    /// rebuild; correctness is worth more than the redraw.</para>
    /// </summary>
    private void Rebuild()
    {
        // NEVER BRANCH ON Control.Visible HERE. Its getter is EFFECTIVE visibility — it walks the parent
        // chain — so during the constructor, with the form not yet shown, it reads false however it was
        // just set. An earlier version guarded the cell loop with it and silently built nothing: the
        // dialog showed one screen no matter how many were queued, and the strip rendered as an empty
        // band. Branch on the frame count, which is what the decision is actually about.
        var count = _frames.Count;

        // ORDER MATTERS. Everything pointing at the old images has to be torn down before the images go,
        // or a control is left holding a disposed Bitmap. Controls.Clear() detaches without disposing, so
        // the cells are disposed explicitly — otherwise every rebuild leaks a PictureBox per screen.
        while (_grid.Controls.Count > 0)
        {
            var stale = _grid.Controls[0];

            _grid.Controls.RemoveAt(0);
            stale.Dispose();
        }

        foreach (var image in _images)
        {
            image.Dispose();
        }

        _images.Clear();

        foreach (var frame in _frames)
        {
            using var stream = new MemoryStream(frame.Png);

            // Copied off the stream on purpose: Image.FromStream keeps the stream alive for the image's
            // lifetime, and a disposed MemoryStream behind a live Bitmap throws on first paint.
            using var decoded = Image.FromStream(stream);

            _images.Add(new Bitmap(decoded));
        }

        // One screen fills the area; two sit side by side; three or four go two-by-two. A fixed 2x2 would
        // waste half the space on the common single-screen case, and a single row would make four screens
        // tall thin slivers that letterbox down to nothing.
        var columns = count == 1 ? 1 : 2;
        var rows = count <= 2 ? 1 : 2;

        _grid.ColumnStyles.Clear();
        _grid.RowStyles.Clear();
        _grid.ColumnCount = columns;
        _grid.RowCount = rows;

        for (var c = 0; c < columns; c++)
        {
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
        }

        for (var r = 0; r < rows; r++)
        {
            _grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));
        }

        for (var i = 0; i < count; i++)
        {
            _grid.Controls.Add(CreateCell(i), i % columns, i / columns);
        }

        // Re-themed here, not only by ThemedForm.OnShown. That runs once, and every rebuild after a
        // delete creates controls it will never see — so a cell rebuilt mid-dialog would render in the
        // system palette next to a dark dialog. Cheap, and idempotent.
        Theme.Apply(_grid);

        _queued.Text = count >= PendingCaptures.Maximum
            ? string.Format(Strings.Get("ask.shots.replacing"), count)
            : string.Format(
                Strings.Get("ask.shots.queued"), count, PendingCaptures.Maximum, (string)_queued.Tag!);
    }

    /// <summary>
    /// One screen: a header carrying its number and a delete button, and the image below it.
    ///
    /// <para>The delete button sits in a DOCKED header rather than floating over the image. Overlaying it
    /// would mean positioning by hand against a control whose size the grid decides, and it would cover
    /// part of the very thing the player is being shown to check.</para>
    /// </summary>
    private Control CreateCell(int index)
    {
        var cell = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(4),
            BackColor = Theme.Background,
        };

        var image = new PictureBox
        {
            Image = _images[index],
            SizeMode = PictureBoxSizeMode.Zoom,
            Dock = DockStyle.Fill,
            BackColor = Theme.PreviewBackdrop,
        };

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 22,
            BackColor = Theme.Background,
        };

        var ordinal = new Label
        {
            Text = string.Format(Strings.Get("ask.shots.ordinal"), index + 1),
            Dock = DockStyle.Left,
            AutoSize = true,
            ForeColor = Theme.SubtleText,
            BackColor = Color.Transparent,
        };

        var remove = new IconButton
        {
            Text = TrashGlyph,
            Font = TrashFont,
            Dock = DockStyle.Right,
            Width = 36,
        };

        _tips.SetToolTip(remove, Strings.Get("ask.shots.remove"));

        // Never to zero. Ask with nothing attached is not a state this dialog can be in, and Retake
        // already covers "replace the only screen I have" — so the button is absent rather than disabled
        // when there is one screen, because a disabled control invites working out how to enable it.
        remove.Visible = _frames.Count > 1;

        remove.Click += (_, _) =>
        {
            if (_frames.Count <= 1)
            {
                return;
            }

            _frames.RemoveAt(index);

            // DEFERRED. Rebuild disposes this very button, and disposing a control while its own click
            // handler is on the stack leaves WinForms touching a dead control on the way out — mouse
            // capture release, focus bookkeeping. BeginInvoke runs the rebuild after the click unwinds.
            BeginInvoke(Rebuild);
        };

        header.Controls.Add(ordinal);
        header.Controls.Add(remove);

        // Image first so the docked header takes its band off the top and the image fills what is left.
        cell.Controls.Add(image);
        cell.Controls.Add(header);

        return cell;
    }

    /// <summary>
    /// A trash can from Segoe MDL2 Assets, or a plain cross where that font is missing.
    ///
    /// <para>Probed rather than assumed. MDL2 ships with Windows 10 and later, which this app already
    /// requires, but a missing icon font renders as a tofu box — and an unreadable delete button on the
    /// control that discards a screenshot is worth two lines to avoid.</para>
    /// </summary>
    private static readonly bool HasIconFont = FontFamily.Families
        .Any(f => f.Name is "Segoe Fluent Icons" or "Segoe MDL2 Assets");

    private static string TrashGlyph => HasIconFont ? "\uE74D" : "\u2715";

    /// <summary>
    /// Created once, not per cell. As a property returning <c>new Font(...)</c> this minted a font for
    /// every cell of every rebuild and disposed none of them — a GDI handle leak that grows with how much
    /// the player uses the feature.
    /// </summary>
    private static readonly Font TrashFont = HasIconFont
        ? new Font(
            FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons")
                ? "Segoe Fluent Icons"
                : "Segoe MDL2 Assets",
            11f)
        : Theme.UiFont;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // The previews are clones of the captures; releasing them here keeps a long session from
            // accumulating full-resolution bitmaps. The cells go first, so nothing is holding a disposed
            // image at paint time.
            _grid.Controls.Clear();
            _tips.Dispose();

            foreach (var image in _images)
            {
                image.Dispose();
            }

            _images.Clear();
        }

        base.Dispose(disposing);
    }
}
