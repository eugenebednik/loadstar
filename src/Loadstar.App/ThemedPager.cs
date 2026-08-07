namespace Loadstar.App;

/// <summary>
/// A tab strip built from ordinary panels and buttons, replacing <c>TabControl</c>.
///
/// <para><b>Why not a TabControl.</b> Its header strip and the frame around its page are drawn by the
/// native control in the system light style, and neither honours <c>BackColor</c>. On a dark dialog
/// that is a white band across the top and a bright border boxing in the content. Owner-drawing them
/// was tried and is documented in this project as having caused real corruption — both pages
/// compositing in the same place, so an inactive page's controls painted over the active one's and a
/// populated field looked blank. There is nothing left to configure after that.</para>
///
/// <para>So: a header panel of flat buttons, a body panel, and one page visible at a time. No native
/// chrome exists to theme, which means nothing can render in the wrong palette. The selected tab
/// shares the body's colour so the two read as one surface — the usual tab illusion, achieved with
/// two background colours instead of a paint override.</para>
/// </summary>
internal sealed class ThemedPager : Panel
{
    private readonly FlowLayoutPanel _header;
    private readonly List<(Button Tab, Control Page)> _pages = [];

    private int _selected = -1;

    public ThemedPager()
    {
        BackColor = Theme.Background;

        _header = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = Theme.RowHeight(lines: 1, extra: 12),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0),
            Margin = new Padding(0),
            BackColor = Theme.Background,
        };

        Body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            Padding = new Padding(0),
        };

        // Body first: docked children fill around whatever is already docked, so adding the header
        // afterwards keeps it above the body rather than overlapping it.
        Controls.Add(Body);
        Controls.Add(_header);
    }

    /// <summary>
    /// The page host, exposed so <see cref="Theme"/> can theme page contents without walking the
    /// header and restyling the tab buttons as ordinary buttons.
    /// </summary>
    public Panel Body { get; }

    public void AddPage(string title, Control page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var tab = new Button
        {
            Text = title,
            AutoSize = false,
            Width = 150,
            // The tab label is translated, so its height must come from the font rather than a literal.
            // AutoSize is off here on purpose — the tabs are a fixed 150px wide so they line up.
            Height = Theme.RowHeight(lines: 1, extra: 10),
            FlatStyle = FlatStyle.Flat,
            Font = Theme.UiFont,
            Margin = new Padding(0),
            Cursor = Cursors.Hand,
            TabStop = true,
        };

        tab.FlatAppearance.BorderSize = 0;

        page.Dock = DockStyle.Fill;
        page.Visible = false;

        var index = _pages.Count;
        tab.Click += (_, _) => Select(index);

        _pages.Add((tab, page));
        _header.Controls.Add(tab);
        Body.Controls.Add(page);

        if (index == 0)
        {
            Select(0);
        }
    }

    public void Select(int index)
    {
        if (index < 0 || index >= _pages.Count)
        {
            return;
        }

        _selected = index;

        for (var i = 0; i < _pages.Count; i++)
        {
            var (tab, page) = _pages[i];
            var active = i == index;

            page.Visible = active;

            // The active tab takes the body's colour so the two merge into one surface; the rest sit
            // on the window background and read as behind it.
            tab.BackColor = active ? Theme.Surface : Theme.Background;
            tab.ForeColor = active ? Theme.Text : Theme.SubtleText;
            tab.Font = active ? new Font(Theme.UiFont, FontStyle.Bold) : Theme.UiFont;
            tab.FlatAppearance.MouseOverBackColor = active ? Theme.Surface : Theme.Border;
        }
    }

    /// <summary>
    /// Restores the pager's own colours.
    ///
    /// <para><see cref="Theme.Apply"/> walks control trees and would otherwise treat the tab buttons
    /// as ordinary buttons and the body as a plain panel, undoing all of this — the same way it
    /// silently overrode the checkbox for three attempted fixes. Apply calls this instead of
    /// recursing blindly.</para>
    /// </summary>
    public void ApplyTheme()
    {
        BackColor = Theme.Background;
        _header.BackColor = Theme.Background;
        Body.BackColor = Theme.Surface;

        Select(_selected < 0 ? 0 : _selected);
    }
}
