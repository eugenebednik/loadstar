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
        ClientSize = new Size(250, 104);
        BackColor = Color.FromArgb(18, 20, 26);
        Opacity = Math.Clamp(opacity, 0.2, 1.0);
        DoubleBuffered = true;

        Cursor = _locked ? Cursors.Default : Cursors.SizeAll;

        _tick = new System.Windows.Forms.Timer { Interval = 1000 };
        _tick.Tick += (_, _) => Invalidate();
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
            DrawLine(g, "Boss timer", 12, Theme.SubtleText);
            DrawLine(g, "Pick a server in Settings", 34, Theme.SubtleText);
            return;
        }

        var y = 10;
        // Corrected time — the overlay does its own countdown arithmetic on every repaint, so reading
        // the raw system clock here would undo the correction applied upstream.
        var now = Loadstar.Core.Time.TimeSync.Now;

        foreach (var spawn in _current.Take(3))
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

            DrawLine(g, spawn.DisplayName, y, colour);
            DrawLine(g, spawn.Countdown(remaining), y, colour, rightAlign: true);

            y += 26;
        }

        if (!_locked)
        {
            DrawLine(g, "drag to move", Height - 20, Color.FromArgb(120, 124, 134));
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
