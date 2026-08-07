namespace Loadstar.App;

/// <summary>
/// Base form that adopts the Windows light/dark preference, including the title bar.
///
/// <para>The title bar is the part that has to be done by hand: WinForms styles the client area and
/// nothing else, so a dark form left alone gets a white caption and reads as a broken window rather
/// than a themed one.</para>
/// </summary>
internal class ThemedForm : Form
{
    protected ThemedForm()
    {
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UiFont;
        AutoScaleMode = AutoScaleMode.Dpi;

        // Composites the form off-screen and blits it once, instead of letting each child paint into a
        // visible window. Without it the theme pass is watchable: controls change colour one at a time.
        DoubleBuffered = true;

        // Without this every window — and so the taskbar button — shows the stock WinForms icon,
        // which reads as an unfinished application regardless of how the rest of it looks.
        Icon = AppIcon.Shared;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTitleBarTheme();
    }

    /// <summary>
    /// Themes the tree BEFORE the window is painted.
    ///
    /// <para><b>This used to be <c>OnShown</c>, and that was the white flash.</b> <c>OnShown</c> fires
    /// after the form is displayed, so the window appeared in the system's own colours — a white client
    /// area with white text boxes — and then recoloured itself control by control while the user watched.
    /// On the settings window, which has the most controls, it read as the dialog loading slowly.</para>
    ///
    /// <para><c>OnLoad</c> satisfies the original reason for <c>OnShown</c> unchanged: it still runs after
    /// the derived constructor has finished building the tree, so controls added there are still covered.
    /// It simply runs before the first paint instead of after it.</para>
    ///
    /// <para>Layout is suspended across the walk because recolouring a control invalidates it, and doing
    /// that to fifty controls one at a time is fifty layout passes for one visible result.</para>
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        SuspendLayout();

        try
        {
            Theme.Apply(this);
        }
        finally
        {
            ResumeLayout(performLayout: true);
        }
    }

    private void ApplyTitleBarTheme()
    {
        var dark = Theme.IsDark ? 1 : 0;

        // The attribute id changed between Windows 10 releases. Try the current one, then the
        // legacy one; both return a failure HRESULT on builds that know neither, which is harmless
        // and simply leaves the default caption.
        if (NativeMethods.DwmSetWindowAttribute(
                Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
        {
            NativeMethods.DwmSetWindowAttribute(
                Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY, ref dark, sizeof(int));
        }
    }

    /// <summary>Adds a consistent header, so the windows read as one application.</summary>
    protected static Label CreateHeading(string text) => new()
    {
        Text = text,
        Font = Theme.HeadingFont,
        ForeColor = Theme.Text,
        AutoSize = false,
        Dock = DockStyle.Top,
        // MEASURED against the HEADING font, which is the largest in the app and so the first to
        // overflow a literal. Every window in the app uses this, which is why one wrong number here
        // reads as "text is clipped all over the place".
        Height = Theme.RowHeight(lines: 1, extra: 14, font: Theme.HeadingFont),
        Padding = new Padding(16, 10, 16, 0),
        BackColor = Color.Transparent,
    };

    /// <summary>A right-aligned action bar with consistent spacing.</summary>
    protected static FlowLayoutPanel CreateActionBar(params Button[] buttons)
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            // The buttons below set MinimumSize 32 and Padding 10 top and bottom, so 52 is the floor and
            // 56 left four pixels of slack. Measured instead, because at 150% scaling the font grows and
            // the literal does not — the row then clips the buttons it exists to hold.
            Height = Theme.RowHeight(lines: 1, extra: 26),
            Padding = new Padding(12, 10, 12, 10),
            BackColor = Theme.Background,
        };

        foreach (var button in buttons)
        {
            // AutoSize rather than a fixed width: "Save" fits 96px and "Сохранить" does not, and
            // the truncation only shows up in whichever language nobody tested. GrowAndShrink with a
            // minimum keeps short labels from producing a cramped button.
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Padding = new Padding(14, 0, 14, 0);
            button.MinimumSize = new Size(96, 32);
            button.Margin = new Padding(6, 0, 0, 0);
            bar.Controls.Add(button);
        }

        return bar;
    }
}
