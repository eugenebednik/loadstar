namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// Which slot each position in the character sheet's equipment grid is, and therefore what kind of item can
/// be in it.
///
/// <para><b>Why position is worth knowing.</b> Identifying a tile by its icon means ranking it against the
/// catalogue, and with no constraint that means all 1,773 items — including every weapon, consumable and
/// artifact, none of which appear in this grid at all. Unconstrained, a pair of trousers was matched into an
/// earring slot. It also throws away correct answers: acceptance depends on the winner clearing the
/// runner-up by a margin, and a runner-up drawn from too large a pool ties far more often, so a right answer
/// gets discarded as ambiguous.</para>
///
/// <para><b>The order is stated by the product owner, not inferred.</b> An earlier version of this file
/// filtered coarsely — armour in the top rows, accessories below — precisely because the order of necklace,
/// bracelet, earring, belt and brooch was unconfirmed, and one wrong entry makes every match in that slot
/// impossible rather than merely unconstrained. It is confirmed now, and it immediately graded the two
/// identifications the icon matcher had produced: the bracelet slot had matched a bracer and ring 1 had
/// matched a ring, both correct.</para>
///
/// <para><b>Reading order is top to bottom, left to right</b>, which is the order the slot locator returns,
/// so the position in that list is the index into <see cref="Order"/>.</para>
/// </summary>
public static class TlEquipmentLayout
{
    /// <summary>
    /// The thirteen slots in grid order. Values are <c>equipmentType</c> exactly as the catalogue spells
    /// them, which is what makes them usable as a filter without translation.
    /// </summary>
    public static readonly IReadOnlyList<string> Order =
    [
        "head",     // 1
        "cloak",    // 2
        "chest",    // 3
        "hands",    // 4  gloves
        "legs",     // 5  trousers
        "feet",     // 6  shoes
        "necklace", // 7
        "bracelet", // 8
        "ring",     // 9  ring 1
        "ring",     // 10 ring 2
        "earring",  // 11
        "belt",     // 12
        "brooch",   // 13
    ];

    /// <summary>Every category the grid can hold — never a weapon, consumable or artifact.</summary>
    public static readonly IReadOnlyCollection<string> Everything =
        [.. Order.Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// The category the tile at <paramref name="index"/> holds, as a single-item collection ready to pass to
    /// the icon matcher.
    ///
    /// <para>An index outside the grid returns EVERYTHING rather than nothing. That matters: if detection
    /// ever finds a fourteenth tile, an unconstrained search still has a chance of being right, while an
    /// empty candidate set guarantees a miss. Degrade, never fail shut.</para>
    /// </summary>
    public static IReadOnlyCollection<string> CategoriesForIndex(int index) =>
        index >= 0 && index < Order.Count ? [Order[index]] : Everything;

    /// <summary>
    /// The slot name for a tile, for showing the player which slot an item was read from.
    /// </summary>
    public static string? SlotNameForIndex(int index) =>
        index >= 0 && index < Order.Count ? Order[index] : null;

    /// <summary>
    /// Rows of armour at the top, kept because it is the honest fallback when only the ROW is known — a grid
    /// whose tile count disagrees with the thirteen above cannot be indexed reliably, but its rows still
    /// separate armour from accessories.
    /// </summary>
    public static IReadOnlyCollection<string> CategoriesForRow(int row) => row switch
    {
        < 0 => Everything,
        < 3 => ["head", "cloak", "chest", "hands", "legs", "feet"],
        _ => ["necklace", "bracelet", "ring", "earring", "belt", "brooch"],
    };
}
