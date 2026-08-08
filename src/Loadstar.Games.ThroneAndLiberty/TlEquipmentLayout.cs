namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// What kind of item each position in the character sheet's equipment grid can hold.
///
/// <para><b>Why position is worth knowing.</b> Identifying a tile by its icon means ranking it against the
/// catalogue, and with no constraint that means all 1,773 items — including every weapon, every piece of
/// food and every artifact, none of which appear in this grid at all. On a real sheet that produced a pair
/// of trousers matched into an earring slot. It also throws away correct answers: acceptance depends on the
/// winner clearing the runner-up by a margin, and a runner-up drawn from ten times too large a pool ties
/// far more often, so a right answer gets discarded as ambiguous.</para>
///
/// <para><b>Deliberately COARSE — armour or accessory, not the exact slot.</b> The grid's first three rows
/// are armour and the rest accessories, which was read directly off a live sheet: head and cloak, chest and
/// gloves, legs and boots. An exact row-and-column to slot-name mapping would be more powerful and would be
/// guesswork — the order of necklace, bracelet, earring, belt and brooch down the lower rows has not been
/// confirmed, and getting one wrong makes every match in that slot impossible rather than merely
/// unconstrained. Coarse and right beats precise and wrong.</para>
///
/// <para>The large win is the same either way: everything that is not worn equipment is excluded, and that
/// is most of the catalogue.</para>
/// </summary>
public static class TlEquipmentLayout
{
    /// <summary>Worn armour. <c>equipmentType</c> values exactly as the catalogue spells them.</summary>
    public static readonly IReadOnlyCollection<string> Armour =
        ["head", "cloak", "chest", "hands", "legs", "feet"];

    /// <summary>
    /// Worn accessories. One <c>ring</c> type covers both ring slots, and <c>brooch</c> belongs here because
    /// that is how the catalogue groups it.
    /// </summary>
    public static readonly IReadOnlyCollection<string> Accessories =
        ["necklace", "bracelet", "ring", "earring", "belt", "brooch"];

    /// <summary>Everything the grid can hold — never a weapon, a consumable or an artifact.</summary>
    public static readonly IReadOnlyCollection<string> Everything = [.. Armour, .. Accessories];

    /// <summary>
    /// Rows of armour at the top of the grid, observed on a live character sheet.
    /// </summary>
    private const int ArmourRows = 3;

    /// <summary>
    /// The categories a tile in this grid row may hold.
    ///
    /// <para>An out-of-range row returns everything rather than nothing, so a detector that finds an
    /// unexpected extra row degrades to an unconstrained search rather than to a guaranteed miss.</para>
    /// </summary>
    public static IReadOnlyCollection<string> CategoriesForRow(int row) => row switch
    {
        < 0 => Everything,
        < ArmourRows => Armour,
        _ => Accessories,
    };
}
