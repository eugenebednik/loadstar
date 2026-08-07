using System.Drawing.Drawing2D;
using Loadstar.Games.ThroneAndLiberty;

namespace Loadstar.App;

/// <summary>
/// The translucent, always-on-top boss countdown.
///
/// <para>An ordinary desktop window — layered, topmost, and click-through once locked — which is
/// exactly what docs/anti-cheat-posture.md permits: "a separate top-level window, Topmost, layered,
/// with <c>WS_EX_TRANSPARENT</c> and <c>WS_EX_NOACTIVATE</c> for click-through. The compositor puts
/// it above the game the same way it would put Notepad above the game." Nothing is drawn into the
/// game's swap chain and no present call is hooked. The extended styles come from
/// <see cref="CreateParams"/> rather than a P/Invoke, so this adds nothing to the native surface.</para>
///
/// <para><b>Unlocked by default, so it can be positioned.</b> Click-through and draggable are
/// mutually exclusive — a window that ignores the mouse cannot be picked up. The first version was
/// permanently click-through and therefore impossible to move. So it starts draggable, and a "Lock
/// position" toggle turns on click-through once the player is happy with where it sits.</para>
///
/// <para><b>Translucency uses form Opacity, not a transparency key.</b> A colour key is binary — a
/// pixel is either fully there or fully gone — so painting an alpha-blended panel onto one produced
/// a muddy blend against the key colour rather than a see-through panel. Form opacity layers the
/// whole window properly, and a rounded <see cref="Control.Region"/> gives clean corners by making
/// them genuinely outside the window.</para>
///
/// <para>In exclusive fullscreen this simply will not draw. That is a compositor rule, and the only
/// way around it is hooking the present chain, which is forbidden — so the documented fix stays
/// "run the game in borderless windowed".</para>
/// </summary>
internal sealed class BossOverlay : Form
{
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const int CornerRadius = 10;

    // LAYOUT IS MEASURED, NOT GUESSED.
    //
    // These were literals — 26px rows, the drag hint at Height-20, a 104px window — chosen against one
    // machine's font metrics. On a 150% display this font's line height is 28.4px, so rows at a 26px
    // pitch overlapped and the drag hint, drawn at y=84, needed 84..112 inside a 104px window. It was cut
    // by 8.4px in EVERY language; the Russian strings only made it more obvious.
    //
    // So the row pitch and the window height now come from measuring the font. Only the horizontal
    // paddings stay fixed, because those are deliberate whitespace rather than a function of the text.
    private const int MaxRows = 3;
    private const int SidePadding = 14;
    private const int TopPadding = 8;
    private const int BottomPadding = 8;

    /// <summary>Breathing room between one row's text and the next.</summary>
    private const int RowSpacing = 4;

    /// <summary>Gap kept between a row's label and its right-aligned countdown.</summary>
    private const int RowGap = 18;

    /// <summary>Never smaller than this, so it still reads as a widget when a countdown says "42s".</summary>
    private const int MinWidth = 250;

    /// <summary>And never wider than this: it is an overlay on someone's game, not a panel.</summary>
    private const int MaxWidth = 560;

    /// <summary>
    /// Measured line height for the UI font, cached because measuring needs a Graphics and this is read
    /// on every paint. Set by <see cref="FitToContent"/> before it is used.
    /// </summary>
    private int _lineHeight = 20;

    /// <summary>Vertical distance from one row's top to the next.</summary>
    private int RowHeight => _lineHeight + RowSpacing;

    private readonly System.Windows.Forms.Timer _tick;
    private readonly Func<IReadOnlyList<BossSpawn>> _spawns;
    private readonly Action<Point> _onMoved;

    private IReadOnlyList<BossSpawn> _current = [];
    private bool _locked;
    private Point _dragOrigin;
    private bool _dragging;

    public BossOverlay(Func<IReadOnlyList<BossSpawn>> spawns, Point location, double opacity, bool locked, Action<Point> onMoved)
    {
        _spawns = spawns ?? throw new ArgumentNullException(nameof(spawns));
        _onMoved = onMoved ?? throw new ArgumentNullException(nameof(onMoved));
        _locked = locked;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Location = location;
        // Replaced immediately by FitToContent, which measures the real text. This is only a
        // sensible size for the first frame.
        ClientSize = new Size(MinWidth, 104);
        BackColor = Color.FromArgb(18, 20, 26);
        Opacity = Math.Clamp(opacity, 0.2, 1.0);
        DoubleBuffered = true;

        Cursor = _locked ? Cursors.Default : Cursors.SizeAll;

        _tick = new System.Windows.Forms.Timer { Interval = 1000 };
        _tick.Tick += (_, _) =>
        {
            FitToContent();
            Invalidate();
        };
        _tick.Start();
    }

    /// <summary>
    /// Click-through, and therefore immovable. Toggling recreates the handle because extended
    /// styles are only read when the window is created.
    /// </summary>
    public bool Locked
    {
        get => _locked;
        set
        {
            if (_locked == value)
            {
                return;
            }

            _locked = value;
            Cursor = value ? Cursors.Default : Cursors.SizeAll;

            // The drag hint occupies a line only while unlocked, so the window has to shrink or grow
            // with it rather than leave a dead band at the bottom.
            FitToContent();

            if (IsHandleCreated)
            {
                RecreateHandle();
            }

            Invalidate();
        }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;

            // NOACTIVATE always: the overlay must never take focus from the game, locked or not.
            // TOOLWINDOW keeps it out of Alt-Tab. TRANSPARENT only when locked, because it is what
            // makes the window ignore the mouse — and an ignored window cannot be dragged.
            parameters.ExStyle |= WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;

            if (_locked)
            {
                parameters.ExStyle |= WS_EX_TRANSPARENT;
            }

            return parameters;
        }
    }

    /// <summary>Never take focus, even if something tries to activate it.</summary>
    protected override bool ShowWithoutActivation => true;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // Before the first tick, so the widget is never briefly the wrong size on screen.
        FitToContent();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        // A real rounded window shape. The corners are outside the window rather than painted
        // transparent, so they composite cleanly over anything behind them.
        using var path = RoundedRect(new Rectangle(0, 0, Width, Height), CornerRadius);
        Region?.Dispose();
        Region = new Region(path);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!_locked && e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _dragOrigin = e.Location;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_dragging)
        {
            Location = new Point(Location.X + e.X - _dragOrigin.X, Location.Y + e.Y - _dragOrigin.Y);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (_dragging)
        {
            _dragging = false;

            // Persist immediately: a position that resets on restart is worse than no dragging.
            _onMoved(Location);
        }
    }

    public void Refresh(IReadOnlyList<BossSpawn> spawns)
    {
        _current = spawns;
        Invalidate();
    }

    /// <summary>
    /// Grows the window to fit its own text.
    ///
    /// <para><b>A fixed 250x104 was sized against English and clipped everything longer.</b> "drag to
    /// move" became "перетащите, чтобы пере" with the rest off the right edge, and the localised event
    /// labels — "Динамические события", "Выберите сервер в настройках" — did the same. Nine languages
    /// cannot share one guessed width.</para>
    ///
    /// <para>Measured rather than merely widened, because the countdown is right-aligned: the label and
    /// the time must not collide, and how much room each needs depends on the language AND on whether a
    /// row currently reads "2h 05m" or "42s". Clamped so it stays a small overlay rather than growing to
    /// span the screen on a long boss list.</para>
    ///
    /// <para>Called from the tick, not from OnPaint — changing size inside a paint handler re-enters
    /// layout and repaints, which is its own class of bug.</para>
    /// </summary>
    private void FitToContent()
    {
        var spawns = _spawns();
        var now = Loadstar.Core.Time.TimeSync.Now;

        using var graphics = CreateGraphics();

        // The number everything else derives from. "Xg" spans an ascender and a descender, so it
        // measures a full line rather than the tallest glyph in whatever text happens to be showing.
        _lineHeight = (int)Math.Ceiling(graphics.MeasureString("Xg", Theme.UiFont).Height);

        var widest = 0f;

        if (spawns.Count == 0)
        {
            foreach (var key in new[] { "overlay.title", "overlay.pickServer" })
            {
                widest = Math.Max(widest, graphics.MeasureString(Strings.Get(key), Theme.UiFont).Width);
            }
        }
        else
        {
            foreach (var spawn in spawns.Take(MaxRows))
            {
                // Label and right-aligned countdown share the row, so the requirement is their sum plus
                // a gap that keeps them visibly separate.
                var label = graphics.MeasureString(BossLabels.DisplayName(spawn), Theme.UiFont).Width;
                var time = graphics.MeasureString(spawn.Countdown(spawn.SpawnsAt - now), Theme.UiFont).Width;

                widest = Math.Max(widest, label + time + RowGap);
            }
        }

        if (!_locked)
        {
            widest = Math.Max(widest, graphics.MeasureString(Strings.Get("overlay.drag"), Theme.UiFont).Width);
        }

        var rows = Math.Max(spawns.Count == 0 ? 2 : Math.Min(spawns.Count, MaxRows), 1);

        var width = (int)Math.Ceiling(widest) + (SidePadding * 2);

        // The drag hint is a full line of text plus its own spacing, and it only exists while unlocked —
        // reserving room for it when locked left a dead band at the bottom.
        var height = TopPadding + (rows * RowHeight) + BottomPadding
            + (_locked ? 0 : _lineHeight + RowSpacing);

        var target = new Size(Math.Clamp(width, MinWidth, MaxWidth), height);

        // Only when it actually changed: assigning ClientSize unconditionally would relayout and
        // repaint every second for nothing.
        if (ClientSize != target)
        {
            ClientSize = target;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        _current = _spawns();

        // A hairline border, brighter while unlocked so it reads as grabbable.
        using var border = new Pen(_locked ? Color.FromArgb(70, 74, 84) : Theme.Accent, 1f);
        using var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius);
        g.DrawPath(border, path);

        if (_current.Count == 0)
        {
            DrawLine(g, Strings.Get("overlay.title"), TopPadding, Theme.SubtleText);
            DrawLine(g, Strings.Get("overlay.pickServer"), TopPadding + RowHeight, Theme.SubtleText);
            return;
        }

        var y = TopPadding;
        // Corrected time — the overlay does its own countdown arithmetic on every repaint, so reading
        // the raw system clock here would undo the correction applied upstream.
        var now = Loadstar.Core.Time.TimeSync.Now;

        foreach (var spawn in _current.Take(MaxRows))
        {
            var remaining = spawn.SpawnsAt - now;

            // Imminent goes red regardless of type; otherwise everything worth travelling for takes
            // the accent and only dynamic events stay muted.
            //
            // Stated as "not a dynamic event" rather than as a list of the types that qualify. The
            // list version had to be extended every time the schedule gained a type, and forgetting
            // to meant a real spawn quietly rendering as muted filler — which is what happened to
            // archbosses when the two streams were merged.
            var colour = remaining <= TimeSpan.FromMinutes(5)
                ? Color.FromArgb(255, 120, 110)
                : spawn.IsDynamicEvent ? Color.Gainsboro : Theme.Accent;

            // Localised here rather than on BossSpawn: the game-knowledge layer holds the event
            // type as data and cannot see Strings. Boss NAMES pass through untranslated, because
            // the player has to find them on their own screen.
            DrawLine(g, BossLabels.DisplayName(spawn), y, colour);
            DrawLine(g, spawn.Countdown(remaining), y, colour, rightAlign: true);

            y += RowHeight;
        }

        if (!_locked)
        {
            DrawLine(
                g,
                Strings.Get("overlay.drag"),
                Height - BottomPadding - _lineHeight,
                Color.FromArgb(120, 124, 134));
        }
    }

    private void DrawLine(Graphics g, string text, int y, Color colour, bool rightAlign = false)
    {
        var size = g.MeasureString(text, Theme.UiFont);
        var x = rightAlign ? Width - size.Width - 14 : 14;

        // A one-pixel dark offset keeps light text legible over a bright game background without a
        // full outline pass.
        using var shadow = new SolidBrush(Color.FromArgb(190, 0, 0, 0));
        g.DrawString(text, Theme.UiFont, shadow, x + 1, y + 1);

        using var brush = new SolidBrush(colour);
        g.DrawString(text, Theme.UiFont, brush, x, y);
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;

        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }

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
