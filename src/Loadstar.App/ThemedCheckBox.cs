namespace Loadstar.App;

/// <summary>
/// A checkbox whose state is visible on a dark background.
///
/// <para>The stock glyph is drawn by the system visual style and ignores <c>ForeColor</c>, so on a
/// dark dialog a ticked box rendered as a plain white square — indistinguishable from unchecked.
/// That matters beyond looks: a setting that was genuinely on looked off, inviting the user to
/// "fix" it and turn off something already correct.</para>
///
/// <para><b>No SetStyle here.</b> An earlier version owner-drew this with
/// <c>UserPaint | AllPaintingInWmPaint</c> and produced exactly the corruption the tab control had:
/// CheckBox also wraps a native control, and taking painting away from it left stale form pixels
/// showing through — the dialog's Cancel button bled through the checkbox row. The same mistake,
/// made twice. <c>FlatStyle.Flat</c> gets an unambiguous look using only properties the control
/// supports, which is the whole point of this class.</para>
///
/// <para>Note the constructor keeps <c>Appearance.Normal</c> deliberately — see the comment there.
/// This paragraph used to claim the fix was <c>Appearance.Button</c>, which the code has never done;
/// a stale comment recommending the opposite of the code is worse than no comment, particularly in a
/// file that exists to stop someone repeating a mistake.</para>
/// </summary>
internal sealed class ThemedCheckBox : CheckBox
{
    public ThemedCheckBox()
    {
        // Normal appearance keeps it a checkbox with a tick, not a toggle button. FlatStyle.Flat is
        // what makes WinForms draw the box itself from these colours instead of deferring to the
        // system visual style, which is the whole problem on a dark background.
        Appearance = Appearance.Normal;
        FlatStyle = FlatStyle.Flat;
        TextAlign = ContentAlignment.MiddleLeft;
        CheckAlign = ContentAlignment.MiddleLeft;
        AutoSize = false;
        Height = 30;
        Width = 420;
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
