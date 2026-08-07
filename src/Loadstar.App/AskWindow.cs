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
    private readonly PictureBox _preview;
    private readonly Panel _strip;
    private readonly Label _queued;

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
        ClientSize = new Size(760, 700);
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
        // in its most literal form: the user sees the images before they leave the machine.
        _preview = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            Dock = DockStyle.Fill,
            BackColor = Theme.PreviewBackdrop,
        };

        // Hidden at one screen, and its height genuinely reclaimed — an invisible docked control is
        // excluded from layout, so the single-screen case keeps a preview 88px taller rather than
        // reserving space for a strip it will never show. There is no layout jump to protect against
        // either way: adding a screen closes this dialog and opens a new one.
        _strip = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 88,
            BackColor = Theme.Background,
        };

        _queued = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 20,
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

        previewFrame.Controls.Add(_preview);
        previewFrame.Controls.Add(_queued);
        previewFrame.Controls.Add(_strip);

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
            Height = 210,
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
    /// Redraws the thumbnail strip and the big preview from <see cref="_frames"/>.
    ///
    /// <para>Rebuilds wholesale rather than patching, because a delete renumbers every thumbnail after it
    /// and reusing controls means keeping their indexes in step with the list. The strip is at most four
    /// small controls; correctness is worth more than the redraw.</para>
    /// </summary>
    private void Rebuild()
    {
        // ORDER MATTERS. Everything pointing at the old images has to be torn down before the images go,
        // or a control is left holding a disposed Bitmap. Controls.Clear() detaches without disposing, so
        // the thumbnails are disposed explicitly — otherwise every rebuild leaks four PictureBoxes.
        _preview.Image = null;

        while (_strip.Controls.Count > 0)
        {
            var stale = _strip.Controls[0];

            _strip.Controls.RemoveAt(0);
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

        _selected = Math.Clamp(_selected, 0, _frames.Count - 1);
        _preview.Image = _images[_selected];

        // Only worth showing once there is a choice to make. A one-item strip is a delete button that
        // must be disabled next to a thumbnail identical to the preview above it.
        _strip.Visible = _frames.Count > 1;

        if (_strip.Visible)
        {
            for (var i = 0; i < _frames.Count; i++)
            {
                _strip.Controls.Add(CreateThumbnail(i));
            }
        }

        // Re-themed here, not only by ThemedForm.OnShown. That runs once, and every rebuild after a
        // delete creates controls it will never see — so a strip rebuilt mid-dialog would render in the
        // system palette next to a dark dialog. Cheap, and idempotent.
        if (_strip.Visible)
        {
            Theme.Apply(_strip);
        }

        _queued.Text = _frames.Count >= PendingCaptures.Maximum
            ? string.Format(Strings.Get("ask.shots.replacing"), _frames.Count)
            : string.Format(
                Strings.Get("ask.shots.queued"),
                _frames.Count,
                PendingCaptures.Maximum,
                (string)_queued.Tag!);
    }

    private Control CreateThumbnail(int index)
    {
        var holder = new Panel
        {
            Width = 132,
            Height = 80,
            Left = 16 + (index * 140),
            Top = 4,
            BackColor = index == _selected ? Theme.Accent : Theme.Background,
            Padding = new Padding(2),
        };

        var thumb = new PictureBox
        {
            Image = _images[index],
            SizeMode = PictureBoxSizeMode.Zoom,
            Dock = DockStyle.Fill,
            BackColor = Theme.PreviewBackdrop,
            Cursor = Cursors.Hand,
        };

        // DEFERRED, not immediate. Rebuild disposes this very PictureBox, and disposing a control while
        // its own click handler is still on the stack leaves WinForms touching a dead control on the way
        // out — mouse capture release, focus bookkeeping. BeginInvoke posts the rebuild so it runs after
        // the click has finished unwinding.
        thumb.Click += (_, _) =>
        {
            _selected = index;
            BeginInvoke(Rebuild);
        };

        // The delete control. A LinkLabel rather than a Button so it reads as an action on the thumbnail
        // instead of a fifth dialog button competing with Ask.
        var remove = new LinkLabel
        {
            Text = "✕",
            AutoSize = true,
            Cursor = Cursors.Hand,
            BackColor = Theme.Background,
            Padding = new Padding(3, 1, 3, 1),
        };

        remove.LinkClicked += (_, _) =>
        {
            // Never to zero. Ask with nothing attached is not a state this dialog can be in, and Retake
            // already covers "replace the only screen I have".
            if (_frames.Count <= 1)
            {
                return;
            }

            _frames.RemoveAt(index);

            if (_selected >= _frames.Count)
            {
                _selected = _frames.Count - 1;
            }

            // Deferred for the same reason as the thumbnail click: this LinkLabel is one of the controls
            // Rebuild disposes.
            BeginInvoke(Rebuild);
        };

        var ordinal = new Label
        {
            Text = (index + 1).ToString(),
            AutoSize = true,
            BackColor = Theme.Background,
            ForeColor = Theme.SubtleText,
            Padding = new Padding(3, 1, 3, 1),
        };

        holder.Controls.Add(thumb);

        // Added after the picture box and positioned last, so they sit above it rather than behind.
        holder.Controls.Add(remove);
        holder.Controls.Add(ordinal);

        remove.BringToFront();
        ordinal.BringToFront();

        holder.Layout += (_, _) =>
        {
            remove.Left = holder.ClientSize.Width - remove.Width - 3;
            remove.Top = 3;
            ordinal.Left = 3;
            ordinal.Top = holder.ClientSize.Height - ordinal.Height - 3;
        };

        return holder;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // The previews are clones of the captures; releasing them here keeps a long session from
            // accumulating full-resolution bitmaps. Cleared from the PictureBox first so nothing is
            // holding a disposed image at paint time.
            _preview.Image = null;

            foreach (var image in _images)
            {
                image.Dispose();
            }

            _images.Clear();
        }

        base.Dispose(disposing);
    }
}
