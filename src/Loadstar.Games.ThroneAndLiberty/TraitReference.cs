namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// The static per-patch tables the target calculator needs, separated from the calculator so the
/// arithmetic stays pure and testable without a 10 MB catalogue in the loop.
///
/// <para>Everything here is static for a given patch, so it is fetched once and cached. The
/// calculator treats a missing entry as "unknown" rather than zero, and reports it — a rune whose
/// value could not be resolved must not quietly contribute nothing, because that understates the
/// target and understating a target tells the player they are closer than they are.</para>
/// </summary>
public sealed record TraitReference
{
    /// <summary>
    /// Rune value tables: rune id → stat id → value at each level, indexed by level.
    ///
    /// <para>From <c>characterBuilder.getEquipmentRunes</c>, where each rune carries a
    /// <c>random_stat_group_1</c> of possible stats, each with an explicit <c>levels</c> array. A
    /// rune's stat is a weighted roll, so the pool is large and only the rolled stat matters.</para>
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>> RuneLevels { get; init; }
        = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>>();

    /// <summary>Item id → set id. Built by inverting each set's <c>itemSetMadeOfItems</c>.</summary>
    public IReadOnlyDictionary<string, string> ItemToSet { get; init; }
        = new Dictionary<string, string>();

    /// <summary>Set id → its name and piece-count bonus tiers.</summary>
    public IReadOnlyDictionary<string, GearSet> Sets { get; init; }
        = new Dictionary<string, GearSet>();

    /// <summary>
    /// Rune synergies keyed by <c>"{category}:{type1}|{type2}|{type3}"</c> — an <b>ordered</b>
    /// arrangement, because the same three rune types in a different sequence give a different
    /// bonus or none at all.
    /// </summary>
    public IReadOnlyDictionary<string, RuneSynergy> Synergies { get; init; }
        = new Dictionary<string, RuneSynergy>();

    /// <summary>
    /// Stat id → how the game displays it.
    ///
    /// <para><b>Not cosmetic.</b> Internal values are on a different scale from the on-screen number,
    /// so a computed total is not comparable to what the player reads until it is converted. A
    /// <c>melee_critical_defense</c> of 9,920 is "Melee Endurance 992"; a
    /// <c>critical_damage_taken_modifier</c> of 700 is "+7%". Comparing the raw figure to a
    /// screenshot would be wrong by an order of magnitude, which is exactly the sort of confidently
    /// wrong number this project keeps guarding against.</para>
    /// </summary>
    public IReadOnlyDictionary<string, StatDisplay> Display { get; init; }
        = new Dictionary<string, StatDisplay>();

    public static string SynergyKey(string category, IReadOnlyList<string> order) =>
        $"{category}:{string.Join("|", order)}";

    /// <summary>
    /// Converts an internal value to what the character sheet shows, formatted. Falls back to the
    /// raw value when the stat is unknown, rather than guessing a scale.
    /// </summary>
    public string Format(string statId, int value)
    {
        if (!Display.TryGetValue(statId, out var display))
        {
            return value.ToString("N0");
        }

        var scaled = value * display.Multiplier;
        var number = scaled == Math.Floor(scaled) ? scaled.ToString("N0") : scaled.ToString("N1");

        return display.IsPercent ? $"{number}%" : number;
    }

    public string NameOf(string statId) =>
        Display.TryGetValue(statId, out var display) ? display.Name : statId;
}

public sealed record StatDisplay
{
    public required string Name { get; init; }

    /// <summary>Internal value × this = the displayed number. Commonly 0.1, or 0.01 for percentages.</summary>
    public double Multiplier { get; init; } = 1;

    public bool IsPercent { get; init; }
}

public sealed record GearSet
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Ascending by piece count.</summary>
    public IReadOnlyList<GearSetBonus> Bonuses { get; init; } = [];
}

public sealed record GearSetBonus
{
    public required int PieceCount { get; init; }

    /// <summary>Machine-readable bonuses, summable into a stat total.</summary>
    public IReadOnlyDictionary<string, int> Stats { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// Free-text bonuses. Quoted, never parsed — the game states several set bonuses only as prose
    /// ("Shield Health +20%"), and turning that into a number would be inventing data.
    /// </summary>
    public IReadOnlyList<string> Passives { get; init; } = [];
}

public sealed record RuneSynergy
{
    public required string Name { get; init; }
    public IReadOnlyList<string> Combination { get; init; } = [];
    public IReadOnlyDictionary<string, int> Stats { get; init; } = new Dictionary<string, int>();
}
