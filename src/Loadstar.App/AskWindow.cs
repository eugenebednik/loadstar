using Loadstar.Core.Capture;
using Loadstar.Core.Net;

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

    /// <summary>
    /// Whether a request could reach the internet, checked while this dialog is open.
    ///
    /// <para>Gating Ask here rather than letting the request fail saves the player the wait: the capture is
    /// already taken and the question already typed, and a timeout several seconds later teaches nothing
    /// the app could not have said up front.</para>
    ///
    /// <para><b>Polled only while this window is up.</b> The tray sits open for hours and the OS
    /// network-change events cover the whole session for free; a request every few seconds forever would be
    /// thousands a day to answer a question nobody is asking.</para>
    /// </summary>
    private readonly ConnectivityMonitor _connectivity;

    private readonly System.Windows.Forms.Timer _connectivityPoll = new() { Interval = 8000 };

    private readonly Button _ask;

    /// <summary>
    /// Why Ask is unavailable, shown directly above it. One label for both reasons rather than one each:
    /// they are mutually exclusive in practice and two stacked warnings would just push the layout around.
    /// </summary>
    private readonly Label _blocked;

    /// <summary>
    /// Holds <see cref="_blocked"/> and, when offered, the Open Settings button. Visibility is toggled on
    /// this rather than the label, so the space collapses entirely when nothing is wrong.
    /// </summary>
    private readonly Panel _blockedRow;

    /// <summary>
    /// Opens Settings and reports whether a key exists afterwards, or <c>null</c> when the caller has no
    /// Settings to offer (the <c>--ask</c> harness).
    ///
    /// <para><b>A delegate rather than constructing SettingsWindow here.</b> This dialog would otherwise need
    /// the settings store, the secret store and the provider catalogue purely to render one button, and every
    /// test that opens an ask window would need all three.</para>
    /// </summary>
    private readonly Func<bool>? _fixKey;

    private readonly Button? _openSettings;

    /// <summary>
    /// The provider that has no API key, or <c>null</c> when one is stored. Not readonly: adding the key
    /// through <see cref="_openSettings"/> clears it, which is the whole point of offering the button.
    /// </summary>
    private string? _missingKeyFor;

    /// <summary>
    /// Last known reachability. Starts <c>true</c>, matching <see cref="ConnectivityMonitor"/>'s bias: the
    /// cost of wrongly allowing a send is one failed request, and the cost of wrongly blocking it is an app
    /// that cannot be used at all. Assume working until told otherwise.
    /// </summary>
    private bool _online = true;

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
    /// <param name="probe">
    /// Overrides the reachability check. Exists so the offline state can actually be LOOKED AT — see
    /// <c>--ask --offline</c> in Program. Gating a button on a condition that only occurs when the wifi
    /// really drops is a condition nobody ever verifies, and an unverified disabled button is how an app
    /// ships that cannot be used at all.
    /// </param>
    public AskWindow(
        IReadOnlyList<CapturedFrame> frames,
        string windowTitle,
        string hotkeyDisplay,
        IReadOnlyList<string> recentQuestions,
        string? initialQuestion = null,
        Func<CancellationToken, Task<bool>>? probe = null,
        string? missingKeyFor = null,
        Func<bool>? fixKey = null)
    {
        ArgumentNullException.ThrowIfNull(frames);

        _connectivity = new ConnectivityMonitor(probe ?? InternetProbe.IsReachableAsync);
        _missingKeyFor = missingKeyFor;
        _fixKey = fixKey;

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
            Height = Theme.RowHeight(lines: 1, extra: 4),
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
            Height = Theme.RowHeight(lines: 2, extra: 8),
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
        // sentence in every language and several are longer than the English. MEASURED rather than a
        // literal, because that is what made it wrong in the first place.
        _queued = new Label
        {
            Dock = DockStyle.Bottom,
            Height = Theme.RowHeight(lines: 2, extra: 6),
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

        _ask = ask;

        // Above the action bar, so the explanation is next to the button it is about rather than buried in
        // the body. Hidden when nothing is wrong — a permanent "you are connected" line is noise.
        //
        // Text is set by ApplyGate rather than here, because there are now two reasons Ask can be off and
        // which one applies is not known until the first poll.
        _blocked = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 0, 8, 0),
            ForeColor = Theme.Warning,
            BackColor = Color.Transparent,
        };

        // Only when there is a key to add AND somewhere to add it. A missing key is the one blocker the
        // player can fix from here, and the alternative is cancelling — which throws away the screens they
        // just assembled in game and the question they typed.
        if (missingKeyFor is not null && fixKey is not null)
        {
            _openSettings = new Button { Text = Strings.Get("ask.opensettings"), Dock = DockStyle.Right, Width = 200 };
            _openSettings.Click += (_, _) => OnOpenSettings();
        }

        // ITS OWN ROW, with the button beside the text rather than in the action bar below. The action bar
        // is a FlowLayoutPanel and four buttons already fill the window: a fifth wrapped onto a second row
        // that the bar is not tall enough to show, so the button existed, was enabled, and was invisible.
        // Verified by screenshot, which is the only way that class of bug gets caught.
        //
        // Beside the explanation is also simply the right place — it is the fix for what the sentence says.
        _blockedRow = new Panel
        {
            Dock = DockStyle.Bottom,
            // Three lines, not two. The sentence has to fit beside a 200px button, and German and Russian
            // both run about half again as long as the English it was sized against.
            Height = Theme.RowHeight(lines: 3, extra: 10),
            BackColor = Color.Transparent,
            Visible = false,
        };

        // ORDER MATTERS, and it is the reverse of what reads naturally. WinForms docks the LAST-added
        // sibling first, so adding the Fill label last gave it the whole row and the Right-docked button
        // then painted on top of the sentence, cutting it mid-word. The button has to go in afterwards to
        // reserve its strip before the label fills what is left.
        _blockedRow.Controls.Add(_blocked);

        if (_openSettings is not null)
        {
            _blockedRow.Controls.Add(_openSettings);
        }
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

        if (_openSettings is not null)
        {
            Theme.MakeSecondary(_openSettings);
        }

        // Taller than before, because the stacked suggestions occupy three lines where the old chip row
        // occupied one. Sized so the text box keeps roughly the height it had rather than being
        // squeezed — the last thing this dialog needs is a cramped box the user is meant to type into.
        //
        // MEASURED: three suggestion links, each of which can wrap to two lines in a language with longer
        // words, plus four lines of room to type. A literal here squeezed the box that the whole dialog
        // exists to let someone type into.
        var lower = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = Theme.RowHeight(lines: 9, extra: 24),
            Padding = new Padding(16, 4, 16, 4),
            BackColor = Theme.Background,
        };

        lower.Controls.Add(_question);
        lower.Controls.Add(suggestions);

        Controls.Add(previewFrame);
        Controls.Add(lower);
        // RightToLeft flow: first argument lands rightmost. Ask keeps the corner, and the two capture
        // actions sit next to Cancel so a stray click near the primary action cannot fire one.
        // BEFORE the action bar, so it sits above it. Docked controls added later end up closer to the
        // edge, so adding this afterwards put it below the buttons and half of it off the bottom of the
        // window — the notice explaining why Ask was greyed out was itself clipped.
        Controls.Add(_blockedRow);
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
                // Gated like the button. A shortcut that sends while the button that does the same thing
                // is greyed out is the kind of inconsistency that gets reported as "it sometimes works".
                if (_ask.Enabled)
                {
                    DialogResult = DialogResult.OK;
                }

                e.SuppressKeyPress = true;
            }
        };

        Rebuild();

        // Marshalled: the monitor raises this from whatever thread the probe completed on, and touching a
        // control from a background thread is an InvalidOperationException at best.
        _connectivity.Changed += (_, online) =>
        {
            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke(() =>
                {
                    _online = online;
                    ApplyGate();
                });
            }
        };

        _connectivityPoll.Tick += (_, _) => _ = _connectivity.RefreshAsync();

        Shown += (_, _) =>
        {
            // BEFORE the connectivity check, so a dialog opened with no API key says so on the first frame
            // rather than looking usable until a probe comes back. The missing key is known synchronously
            // and does not need waiting for.
            ApplyGate();

            _connectivity.Start();
            _connectivityPoll.Start();

            // One check now, so a dialog opened on a dead connection says so immediately instead of after
            // the first poll interval.
            _ = _connectivity.RefreshAsync();

            Activate();
            _question.Focus();
            // Caret after any restored text, so a carried-over question can be edited rather than
            // replaced by the first keystroke.
            _question.SelectionStart = _question.TextLength;
        };
    }

    /// <summary>
    /// Enables or disables Ask, and says why.
    ///
    /// <para>Two things can block it — no API key, and no connection — and this is the single place that
    /// decides. They were separate before, and a second reason bolted on beside the first is how a button
    /// ends up enabled because one check said yes while the other said no.</para>
    ///
    /// <para><b>The missing key wins when both apply.</b> Connectivity comes back on its own and the key
    /// never does, so naming the connection first would send someone to wait for a problem that was not
    /// theirs to fix.</para>
    ///
    /// <para>Only Ask is gated. Retake and Add work perfectly well offline and without a key — the capture is
    /// local — and disabling them would strand someone mid-flow with no way to finish assembling their
    /// screens.</para>
    /// </summary>
    private void ApplyGate()
    {
        var reason = _missingKeyFor is not null
            ? string.Format(Strings.Get("ask.nokey"), _missingKeyFor)
            : _online ? null : Strings.Get("ask.offline");

        var allowed = reason is null;

        // Not just Enabled: a flat button with an explicit BackColor keeps painting it when disabled, so
        // this is what actually greys it out. See Theme.SetPrimaryEnabled.
        Theme.SetPrimaryEnabled(_ask, allowed);

        _blocked.Text = reason ?? string.Empty;
        _blockedRow.Visible = !allowed;

        if (_openSettings is not null)
        {
            // Vanishes once the key is in, because from then on it is just a way to lose your place.
            _openSettings.Visible = _missingKeyFor is not null;
        }

        // Set HERE and not only in the constructor. Theme.Apply's Label case forces every small label to
        // SubtleText at OnShown, so a warning colour assigned during construction is gone by the time
        // anyone sees it — the same overwrite that made the delete button an empty rectangle. This runs
        // after the theme walk and on every transition, so it is the assignment that survives.
        _blocked.ForeColor = Theme.Warning;

        _tips.SetToolTip(_ask, reason ?? string.Empty);

        // Ctrl+Enter bypasses the button entirely, so the form-level accept has to go too.
        AcceptButton = allowed ? _ask : null;
    }

    /// <summary>
    /// Opens Settings so the key can be added without losing the screens, then re-gates on what came back.
    ///
    /// <para>The caller owns the check, because "is there a key for the selected provider" needs the secret
    /// store and the provider that is selected NOW — the player may well have changed provider in the dialog
    /// they were just handed, which is how this state is most often reached in the first place.</para>
    /// </summary>
    private void OnOpenSettings()
    {
        if (_fixKey is null)
        {
            return;
        }

        if (_fixKey())
        {
            _missingKeyFor = null;
        }

        ApplyGate();

        // Focus back where they were typing, rather than leaving it on a button that may have just vanished.
        _question.Focus();
        _question.SelectionStart = _question.TextLength;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Before the dialog result is consumed, so no probe is in flight while the caller is already
        // capturing again.
        _connectivityPoll.Stop();

        base.OnFormClosing(e);
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

        // MEASURED, and around the icon font rather than the UI font — the trash glyph is the tallest
        // thing in this row. At a literal 22px the button was taller than the band that held it, so the
        // image directly below appeared to slice the icon in half. The extra is a deliberate gap so the
        // glyph does not sit flush against the preview.
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = Theme.RowHeight(lines: 1, extra: 10, font: TrashFont),
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
            _connectivityPoll.Dispose();

            // Unsubscribes from the STATIC NetworkChange events. Without this the monitor, and this whole
            // window through its Changed handler, stay alive for the life of the process.
            _connectivity.Dispose();

            foreach (var image in _images)
            {
                image.Dispose();
            }

            _images.Clear();
        }

        base.Dispose(disposing);
    }
}
