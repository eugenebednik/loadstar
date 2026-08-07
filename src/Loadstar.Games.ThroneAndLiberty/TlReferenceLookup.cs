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
