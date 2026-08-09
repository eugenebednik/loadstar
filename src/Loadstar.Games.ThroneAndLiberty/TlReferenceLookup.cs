using System.Text;

namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// Resolves the reference rows a request actually needs, instead of carrying the whole catalogue.
///
/// <para><b>Why this exists.</b> The always-loaded knowledge pack reached ~13,200 tokens and the whole
/// prompt ~23,400, against a ceiling that is about ATTENTION rather than cost — past some size the model
/// gets worse at the specific rules that matter. So data that only some questions touch has to be looked
/// up rather than carried. The equipment catalogue is 1,773 items and 10.4 MB; a build names 27 of them.</para>
///
/// <para><b>What it fixes.</b> The catalogue was parsed, tested and never loaded, so the prompt had to say
/// "item ids are opaque catalogue keys, do not translate them into display names you are not certain of",
/// and a pinned build read as 27 lines of <c>belt_aa_t3_normal_004</c>. Neither the model nor the player
/// could say what the build was aiming at.</para>
///
/// <para><b>It also disambiguates the slot names, which is worth as much as the names themselves.</b> The
/// slots called <c>attack</c>, <c>defense</c>, <c>utility</c>, <c>hp-recovery</c> and <c>mana-recovery</c>
/// hold FOOD, not gear — five of a build's 27 entries. Unresolved, <c>attack: Usable_Food_Result_008_kA</c>
/// invites reading a consumable as a weapon. Resolved, it says "Rare BBQ Platter" and the mistake is not
/// available.</para>
///
/// <para><b>Scope.</b> Names and item level ranges only. Set membership looks like it belongs here and does
/// not: <see cref="TraitReference"/> already carries set NAMES and the piece-count thresholds with their
/// actual bonus text, and <see cref="DerivedTargets"/> already renders "2 more for the 4-piece bonus" from
/// them. A second set block keyed on raw set ids would be the same information, worse.</para>
/// </summary>
public static class TlReferenceLookup
{
    /// <summary>
    /// What one equipped item is, as a suffix for the line that already prints its slot and id.
    ///
    /// <para>Inline rather than a parallel table: the prompt already lists slot, id, runes and traits per
    /// item, and a second table keyed by slot would make the model join two lists to answer one question.
    /// Returns an empty string when the id is unresolved or absent, so the caller keeps its existing
    /// output unchanged.</para>
    /// </summary>
    public static string Describe(string? itemId, EquipmentCatalog? catalog)
    {
        if (catalog is null || string.IsNullOrWhiteSpace(itemId))
        {
            return string.Empty;
        }

        var item = catalog.Find(itemId);

        if (item is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        builder.Append(" — ").Append(item.Name);

        var levels = DescribeLevels(item);

        if (levels is not null)
        {
            builder.Append(" (").Append(levels).Append(')');
        }

        return builder.ToString();
    }

    /// <summary>
    /// The item level range the item itself supports, or null when it has none worth printing.
    ///
    /// <para><b>Deliberately a RANGE and never a single maximum.</b> Almost every T4 item reports levels
    /// 51–80, so a column headed "max item level" would print 80 for most of a build and read as the
    /// build's target — which it is not; it is the item's ceiling. The ceiling is genuinely useful in the
    /// other direction: a T3 weapon that stops at 50 is what makes "is a T4 upgrade actually better"
    /// answerable, and that item is in this build.</para>
    ///
    /// <para>Food reports level 0, which means nothing, so it is suppressed rather than printed.</para>
    /// </summary>
    /// <summary>
    /// What a piece is capable of, beyond what the build has chosen for it.
    ///
    /// <para><b>The gap between the two is the actionable part.</b> Since 4.0.0 gear drops with NO traits and
    /// they are unlocked with stones, so the catalogue's trait list is the menu and the build's list is a
    /// selection from it. Naming the options a piece still has turns "your gear is fine" into a specific,
    /// priceable next action on equipment the player already wears.</para>
    ///
    /// <para>Also carries the stat range — what the piece gives at its floor against its ceiling — so the
    /// headroom in a slot is a subtraction rather than a guess. That matters most for the pieces this game
    /// caps early: a weapon that stops at item level 50 cannot be raised however much is spent.</para>
    ///
    /// <para><b>Only the traits the build has NOT taken, and at most four of them.</b> Both halves are budget
    /// decisions with a correctness benefit. Naming the ones already chosen is noise — they are listed on the
    /// line above — and computing the real set difference is both shorter and more accurate than a count. The
    /// cap exists because this line repeats for 27 items: the unabridged version put the assembled prompt at
    /// ~25,300 tokens against a 25,000 tripwire, which is the point at which the model measurably gets worse
    /// at the rules that matter.</para>
    ///
    /// <para>Values are omitted for the same reason. The names are what let the model ask for the tooltip
    /// that carries the numbers.</para>
    /// </summary>
    public static string DescribeCapability(
        string? itemId,
        EquipmentCatalog? catalog,
        IEnumerable<string>? chosenTraits = null)
    {
        if (catalog is null || string.IsNullOrWhiteSpace(itemId))
        {
            return string.Empty;
        }

        var item = catalog.Find(itemId);

        if (item is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();

        // The primary defensive or offensive number, floor to ceiling. One stat, chosen as the largest,
        // because listing every stat per item is what the budget cannot afford.
        if (item.StatsAtFloor.Count > 0 && item.StatsAtCeiling.Count > 0)
        {
            var headline = item.StatsAtCeiling.OrderByDescending(pair => pair.Value).First();

            if (item.StatsAtFloor.TryGetValue(headline.Key, out var floor) && floor != headline.Value)
            {
                parts.Add($"{headline.Key} {floor}→{headline.Value}");
            }
        }

        // The genuine set difference, not a subtraction of counts. A build can carry a trait the catalogue
        // does not list for that piece — heroic picks come from a different field — so counting would
        // under-report the options while naming cannot.
        var taken = new HashSet<string>(chosenTraits ?? [], StringComparer.OrdinalIgnoreCase);
        var spare = item.TraitOptions.Keys.Where(name => !taken.Contains(name)).ToArray();

        if (item.TraitOptions.Count > 0)
        {
            if (spare.Length == 0)
            {
                parts.Add($"all {item.TraitOptions.Count} trait options taken");
            }
            else
            {
                var listed = string.Join(", ", spare.Take(4));
                var rest = spare.Length > 4 ? $" +{spare.Length - 4}" : string.Empty;

                parts.Add($"{spare.Length} of {item.TraitOptions.Count} traits free: {listed}{rest}");
            }
        }

        return parts.Count == 0 ? string.Empty : "      " + string.Join(" | ", parts);
    }

    private static string? DescribeLevels(CatalogItem item)
    {
        var levels = item.AvailableItemLevels;

        if (levels.Count == 0 || levels[^1] <= 0)
        {
            return null;
        }

        return levels.Count == 1
            ? $"item level {levels[0]}"
            : $"item levels {levels[0]}–{levels[^1]}";
    }
}
