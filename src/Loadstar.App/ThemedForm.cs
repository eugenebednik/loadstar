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

        // Without this every window — and so the taskbar button — shows the stock WinForms icon,
        // which reads as an unfinished application regardless of how the rest of it looks.
        Icon = AppIcon.Shared;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTitleBarTheme();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // Applied after the tree is built so controls added in a derived constructor are covered.
        Theme.Apply(this);
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
        Height = 42,
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
            Height = 56,
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
