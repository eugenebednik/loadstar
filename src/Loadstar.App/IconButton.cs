namespace Loadstar.App;

/// <summary>
/// A button whose entire label is a glyph from an icon font.
///
/// <para>It exists to be a distinct TYPE, so <see cref="Theme"/> can recognise it. The theme walk styles
/// ordinary buttons through <see cref="Theme.MakeSecondary"/>, which sets <c>Font = UiFont</c> — and for a
/// control whose label is <c></c> from Segoe MDL2 Assets, replacing the font renders the label as
/// nothing at all. The first version of the delete button was a plain <see cref="Button"/> and shipped as
/// four empty grey rectangles.</para>
///
/// <para>The same trap as the checkbox and the link label before it: a control configured in a
/// constructor, quietly overwritten at <c>OnShown</c>. The fix is the same one — a case of its own, ahead
/// of the base type's.</para>
/// </summary>
internal sealed class IconButton : Button
{
    public IconButton()
    {
        FlatStyle = FlatStyle.Flat;
        Cursor = Cursors.Hand;
        FlatAppearance.BorderSize = 0;
        TabStop = false;
    }
}
