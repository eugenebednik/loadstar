namespace Loadstar.App;

/// <summary>
/// A checkbox whose state is visible on a dark background.
///
/// <para><b>No SetStyle here.</b> An earlier version owner-drew this with
/// <c>UserPaint | AllPaintingInWmPaint</c> and produced exactly the corruption the tab control had:
/// CheckBox also wraps a native control, and taking painting away from it left stale form pixels
/// showing through — the dialog's Cancel button bled through the checkbox row. The same mistake, made
/// twice. Everything here is a property assignment.</para>
///
/// <para><b>Why this is a toggle and not a tick.</b> Two attempts to keep the tick failed on a live
/// machine. The stock glyph comes from the system visual style and ignores <c>ForeColor</c>, so a
/// ticked box rendered as a plain square. <c>Appearance.Normal</c> with <c>FlatStyle.Flat</c> was the
/// second attempt and was reported as showing no tick at all, checked or not. The property that makes
/// state unmistakable — <c>FlatAppearance.CheckedBackColor</c> — is only honoured for
/// <c>Appearance.Button</c>, so the tick and a reliable checked state were mutually exclusive here.</para>
///
/// <para>A control whose state is invisible is not a checkbox, it is a control that looks broken — and
/// this one guards screen capture, the setting where the user most needs to know whether it is on. So
/// the whole control fills with the accent colour when checked, which cannot fail to render because it
/// is a background, not a glyph.</para>
/// </summary>
internal sealed class ThemedCheckBox : CheckBox
{
    public ThemedCheckBox()
    {
        Appearance = Appearance.Button;
        FlatStyle = FlatStyle.Flat;
        TextAlign = ContentAlignment.MiddleLeft;
        AutoSize = false;
        Height = 30;
        Width = 420;

        // Keeps the label off the border now that the control is a filled button rather than a glyph
        // followed by text.
        Padding = new Padding(10, 0, 0, 0);

        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;

        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = Theme.UiFont;

        FlatAppearance.BorderSize = 1;
        FlatAppearance.BorderColor = Theme.Border;
        FlatAppearance.CheckedBackColor = Theme.Accent;
        FlatAppearance.MouseOverBackColor = Theme.Border;
    }
}
