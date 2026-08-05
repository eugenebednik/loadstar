namespace Loadstar.App;

/// <summary>
/// A TabControl whose headers respect the theme.
///
/// <para>WinForms draws tab headers with the system visual style and ignores <c>BackColor</c>, so a
/// dark dialog gets a strip of white tabs. Owner-drawing the headers is the only fix without a
/// third-party control library.</para>
///
/// <para><b>Deliberately minimal.</b> TabControl wraps a native Win32 control, and it turned out to
/// be far more fragile than it looks: overriding <c>OnPaintBackground</c>, calling
/// <c>SetStyle(AllPaintingInWmPaint)</c>, and resizing <c>ItemSize</c> from layout events each
/// caused visible corruption — at worst both tab pages compositing in the same place, so the
/// inactive page's controls painted over the active one's and a populated field looked blank.
/// Everything beyond <c>DrawMode</c> plus <see cref="OnDrawItem"/> has been removed. Do not add
/// paint or style overrides here without checking the pages still clip.</para>
/// </summary>
internal sealed class ThemedTabControl : TabControl
{
    public ThemedTabControl()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;

        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(150, 32);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var page = TabPages[e.Index];
        var selected = e.Index == SelectedIndex;
        var bounds = e.Bounds;

        using var background = new SolidBrush(selected ? Theme.Surface : Theme.Background);
        e.Graphics.FillRectangle(background, bounds);

        // The strip to the right of the last tab gets no draw event of its own, so the native control
        // paints it — a white band across the top of a dark dialog. Filling it from here keeps the fix
        // inside the one override this class already has; adding OnPaintBackground is what previously
        // caused both tab pages to composite in the same place.
        //
        // TabSizeMode.FillToRight was tried first and made it worse: the tabs shrank to their text
        // instead of expanding, leaving even more bare strip.
        if (e.Index == TabCount - 1 && bounds.Right < Width)
        {
            using var strip = new SolidBrush(Theme.Background);
            e.Graphics.FillRectangle(strip, bounds.Right, bounds.Top, Width - bounds.Right, bounds.Height);
        }

        // A bright underline marks the active tab, which reads clearly without needing a border
        // treatment that would fight the flat styling elsewhere.
        if (selected)
        {
            using var accent = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(accent, bounds.Left, bounds.Bottom - 3, bounds.Width, 3);
        }

        TextRenderer.DrawText(
            e.Graphics,
            page.Text,
            selected ? new Font(Theme.UiFont, FontStyle.Bold) : Theme.UiFont,
            bounds,
            selected ? Theme.Text : Theme.SubtleText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
