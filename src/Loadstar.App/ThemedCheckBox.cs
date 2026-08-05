namespace Loadstar.App;

/// <summary>
/// A standard Windows checkbox with a label that reads on a dark dialog. Deliberately almost
/// unconfigured.
///
/// <para><b>No SetStyle, no OnPaint.</b> An earlier version owner-drew this and produced the same
/// corruption the tab control had — CheckBox wraps a native control, and taking painting away from it
/// left stale form pixels showing through, with the dialog's Cancel button bleeding into the checkbox
/// row.</para>
///
/// <para><b>Read this before adding a property.</b> Three attempts failed here, and every one failed
/// by configuring more:</para>
/// <list type="number">
/// <item><c>FlatStyle.Flat</c> + <c>Appearance.Normal</c> — no tick drawn, checked or not.</item>
/// <item><c>Appearance.Button</c> + <c>CheckedBackColor</c> — state finally visible, but it was a
/// toggle button rather than a checkbox, and the accent fill made the label unreadable.</item>
/// <item><c>FlatStyle.Standard</c> + explicit <c>BackColor</c> + fixed size + both alignments set —
/// still no tick. Proven by a controlled test: a stored value of <c>false</c> rendered pixel-identical
/// to <c>true</c>.</item>
/// </list>
///
/// <para>So the fix is subtraction. <c>Appearance</c>, <c>FlatStyle</c>, <c>UseVisualStyleBackColor</c>,
/// <c>CheckAlign</c> and <c>TextAlign</c> are all left at their defaults, which is what every WinForms
/// app does and what actually gets a themed tick. <c>AutoSize</c> stays on so the control measures
/// itself around the glyph instead of being forced to a height the renderer has to lay out inside.
/// Only the label colour and the font are set, because those are the only things the dark dialog
/// genuinely needs.</para>
/// </summary>
internal sealed class ThemedCheckBox : CheckBox
{
    public ThemedCheckBox()
    {
        AutoSize = true;
        Cursor = Cursors.Hand;

        // Transparent rather than Theme.Surface: the parent already paints the dialog, and forcing an
        // opaque background here is part of what pushed earlier versions off the themed render path.
        BackColor = Color.Transparent;

        ForeColor = Theme.Text;
        Font = Theme.UiFont;
    }
}
