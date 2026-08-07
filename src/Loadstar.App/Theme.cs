using Microsoft.Win32;

namespace Loadstar.App;

/// <summary>
/// Light and dark palettes, chosen from the user's Windows preference.
///
/// <para>Read from <c>AppsUseLightTheme</c> under the Personalize key — the same value the shell and
/// every well-behaved Windows app uses. This is reading a display preference for our own windows;
/// nothing is written, and no system setting is modified.</para>
///
/// <para>Detection is deliberately forgiving: a missing or unreadable key falls back to light, which
/// is what Windows itself defaults to, rather than throwing on a machine with an unusual policy.</para>
/// </summary>
internal static class Theme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsDark { get; private set; } = DetectDark();

    /// <summary>Re-reads the preference, so a theme switch applies to windows opened afterwards.</summary>
    public static void Refresh() => IsDark = DetectDark();

    private static bool DetectDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);

            // 0 = dark, 1 = light. Absent means the machine has never been switched, i.e. light.
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    public static Color Background => IsDark ? Color.FromArgb(32, 33, 36) : Color.FromArgb(245, 246, 248);

    /// <summary>Panels and inputs, one step raised from the window background.</summary>
    public static Color Surface => IsDark ? Color.FromArgb(43, 45, 49) : Color.White;

    public static Color Border => IsDark ? Color.FromArgb(58, 61, 66) : Color.FromArgb(216, 219, 224);

    public static Color Text => IsDark ? Color.FromArgb(232, 234, 237) : Color.FromArgb(31, 34, 38);

    public static Color SubtleText => IsDark ? Color.FromArgb(154, 160, 166) : Color.FromArgb(95, 99, 104);

    /// <summary>The gold from the tray star, used for the primary action.</summary>
    public static Color Accent => IsDark ? Color.FromArgb(255, 214, 102) : Color.FromArgb(179, 132, 20);

    public static Color AccentText => IsDark ? Color.FromArgb(28, 30, 34) : Color.White;

    /// <summary>Backdrop behind the screenshot preview — always dark so the image reads well.</summary>
    public static Color PreviewBackdrop => Color.FromArgb(24, 24, 28);

    public static Font UiFont { get; } = CreateFont("Segoe UI Variable Text", "Segoe UI", 9.75f);

    public static Font HeadingFont { get; } = CreateFont("Segoe UI Variable Display", "Segoe UI", 13f, FontStyle.Regular);

    public static Font MonoFont { get; } = CreateFont("Cascadia Mono", "Consolas", 9.5f);

    /// <summary>
    /// Falls back when a font is missing. Requesting an unavailable family silently substitutes
    /// something arbitrary, so the preferred name is verified before use.
    /// </summary>
    private static Font CreateFont(string preferred, string fallback, float size, FontStyle style = FontStyle.Regular)
    {
        try
        {
            var font = new Font(preferred, size, style);
            return font.Name.Equals(preferred, StringComparison.OrdinalIgnoreCase)
                ? font
                : new Font(fallback, size, style);
        }
        catch (ArgumentException)
        {
            return new Font(fallback, size, style);
        }
    }

    /// <summary>Styles a button as the primary action.</summary>
    public static void MakePrimary(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Accent;
        button.ForeColor = AccentText;
        button.FlatAppearance.BorderSize = 0;
        button.Font = new Font(UiFont, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    /// <summary>Styles a button as a secondary action.</summary>
    public static void MakeSecondary(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Surface;
        button.ForeColor = Text;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 1;
        button.Font = UiFont;
        button.Cursor = Cursors.Hand;
    }

    /// <summary>
    /// Applies the palette to a control and everything under it.
    ///
    /// <para>Walks the tree rather than relying on inheritance because several WinForms controls —
    /// TextBox, ComboBox, ListBox — do not inherit BackColor from their parent, and would otherwise
    /// stay white inside a dark window.</para>
    /// </summary>
    public static void Apply(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);

        root.BackColor = root is Form ? Background : root.BackColor;
        root.ForeColor = Text;

        foreach (Control child in root.Controls)
        {
            switch (child)
            {
                case TextBox textBox:
                    textBox.BackColor = Surface;
                    textBox.ForeColor = Text;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    textBox.Font = textBox.Multiline && textBox.ReadOnly ? MonoFont : UiFont;
                    break;

                case ComboBox combo:
                    // Changing FlatStyle recreates the handle and clears Text, which silently blanked
                    // the stored process name every time the dialog was themed. Preserve and restore.
                    var editableText = combo.Text;

                    combo.BackColor = Surface;
                    combo.ForeColor = Text;
                    combo.FlatStyle = FlatStyle.Flat;
                    combo.Font = UiFont;

                    if (combo.DropDownStyle != ComboBoxStyle.DropDownList && combo.Text != editableText)
                    {
                        combo.Text = editableText;
                    }

                    break;

                case ListBox list:
                    list.BackColor = Surface;
                    list.ForeColor = Text;
                    list.BorderStyle = BorderStyle.FixedSingle;
                    list.Font = UiFont;
                    break;

                // BEFORE the Button case, which it derives from and would otherwise match. MakeSecondary
                // sets Font = UiFont, and an IconButton's whole label is a glyph from an icon font — so
                // the generic path renders it as an empty rectangle. Same failure mode as the checkbox
                // above: a constructor setting silently overwritten from here at OnShown.
                case IconButton icon:
                    icon.BackColor = Surface;
                    icon.ForeColor = SubtleText;
                    icon.FlatStyle = FlatStyle.Flat;
                    icon.FlatAppearance.BorderSize = 0;
                    icon.FlatAppearance.MouseOverBackColor = Accent;
                    icon.Cursor = Cursors.Hand;
                    break;

                case Button button:
                    if (button.BackColor != Accent)
                    {
                        MakeSecondary(button);
                    }

                    break;

                case CheckBox check:
                    check.ForeColor = Text;
                    check.Font = UiFont;
                    check.BackColor = Color.Transparent;

                    // FLATSTYLE IS DELIBERATELY NOT SET. It used to be forced to Flat here, on the
                    // theory that a Flat box draws itself from these colours while the system glyph
                    // would render a ticked box as an empty white square.
                    //
                    // Flat is worse: it draws no tick AT ALL. Proven rather than argued — with a
                    // stored value of true the box rendered pixel-identical to a stored value of
                    // false, and clicking appeared to do nothing because Checked was toggling
                    // invisibly. It was reported three times as "the checkbox is uncheckable".
                    //
                    // This line also silently defeated every fix attempted in ThemedCheckBox's own
                    // constructor, because Apply runs from OnShown — after construction. If a
                    // checkbox ever looks wrong again, look here FIRST, not at the control.
                    break;

                // BEFORE the Label case, which it derives from and would otherwise match. That
                // inheritance is exactly how the checkbox spent three fixes being "uncheckable": a
                // control fell into a base-class case here and had its constructor settings quietly
                // overwritten at OnShown. A LinkLabel's link text is drawn with LinkColor rather than
                // ForeColor, so the Label case would not visibly break it today — it would just leave
                // link colours as whatever each window happened to set, which is how they drift.
                case LinkLabel link:
                    link.Font = UiFont;
                    link.BackColor = Color.Transparent;
                    link.ForeColor = SubtleText;
                    link.LinkColor = Accent;
                    link.ActiveLinkColor = Text;
                    link.VisitedLinkColor = Accent;
                    link.LinkBehavior = LinkBehavior.HoverUnderline;
                    break;

                case Label label:
                    label.ForeColor = label.Font.Size > 11 ? Text : SubtleText;
                    label.BackColor = Color.Transparent;
                    break;

                case PictureBox picture:
                    picture.BackColor = PreviewBackdrop;
                    break;

                // Before the Panel case, which it would otherwise match. The pager owns its own
                // colours, and its tab buttons are Buttons that must NOT be restyled as ordinary
                // secondary buttons. Page contents still get themed; the pager's chrome is then put
                // back. `continue` skips the generic recursion below, which is the whole point.
                case ThemedPager pager:
                    Apply(pager.Body);
                    pager.ApplyTheme();
                    continue;

                case Panel or TableLayoutPanel or FlowLayoutPanel:
                    child.BackColor = Background;
                    child.ForeColor = Text;
                    break;
            }

            if (child.HasChildren)
            {
                Apply(child);
            }
        }
    }
}
