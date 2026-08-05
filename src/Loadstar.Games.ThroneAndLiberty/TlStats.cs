namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// The five base stats, the shared breakpoint ladder, and what each threshold actually grants.
///
/// <para>Captured from live stat tooltips on 2026-08-03, patch 4.5.0. This is reference data
/// rather than logic: the arithmetic that uses it lives in <see cref="StatPlanner"/>.</para>
/// </summary>
public static class TlStats
{
    /// <summary>
    /// Every stat starts here before any allocation. It is the constant that converts questlog's
    /// <em>allocated</em> attribute numbers into base values: <c>base = 10 + allocated</c>.
    /// </summary>
    public const int StartingBase = 10;

    /// <summary>
    /// Base value at which each further point starts costing two.
    /// <para>Base only — equipment contributions do not count towards it, which is the entire
    /// reason a stat displaying 96 can be cheaper to raise than one displaying 71.</para>
    /// </summary>
    public const int EscalationBase = 30;

    /// <summary>
    /// Base value at which each further point starts costing <b>four</b>.
    ///
    /// <para>This band was long recorded as "reported but unverified". It is now exact, decompiled
    /// from questlog's own allocation transform, which converts allocated points into an attribute
    /// contribution with two diminishing-returns steps:</para>
    ///
    /// <code>
    /// allocated &lt;= 20            -> contributes allocated        (marginal 1.00 -> 1x cost)
    /// 20 &lt; allocated &lt;= 40   -> 20 + (a-20)*0.5           (marginal 0.50 -> 2x cost)
    /// allocated &gt; 40            -> 30 + (a-40)*0.25          (marginal 0.25 -> 4x cost)
    /// </code>
    ///
    /// <para>Since <c>base = 10 + allocated</c>, those bands land on base 30 and base <b>50</b>.
    /// The 30 threshold already recorded here is confirmed by the same function, which is a useful
    /// check that the two derivations agree.</para>
    /// </summary>
    public const int SecondEscalationBase = 50;

    /// <summary>
    /// Threshold positions are shared by all five stats; the rewards at them are not.
    /// Note the gaps: after 80 the ladder jumps by twenty, not ten.
    /// </summary>
    public static readonly IReadOnlyList<int> Ladder = [30, 40, 50, 60, 70, 80, 100, 120];

    public static readonly IReadOnlyList<TlStat> All =
    [
        TlStat.Strength,
        TlStat.Dexterity,
        TlStat.Wisdom,
        TlStat.Perception,
        TlStat.Fortitude,
    ];

    /// <summary>
    /// questlog's attribute keys. Two of them do not match the in-game stat name and quietly
    /// produce a wrong plan if mapped by eye: <c>int</c> is <b>Wisdom</b>, not Intelligence, and
    /// <c>con</c> is <b>Fortitude</b>, not Constitution.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, TlStat> QuestlogAttributeKeys =
        new Dictionary<string, TlStat>(StringComparer.OrdinalIgnoreCase)
        {
            ["str"] = TlStat.Strength,
            ["dex"] = TlStat.Dexterity,
            ["int"] = TlStat.Wisdom,
            ["per"] = TlStat.Perception,
            ["con"] = TlStat.Fortitude,
        };

    /// <summary>
    /// Translates a build's raw <c>attributes</c> into stats, dropping keys questlog may add that
    /// we do not recognise rather than failing the whole import over one of them.
    /// </summary>
    public static IReadOnlyDictionary<TlStat, int> MapAllocated(IReadOnlyDictionary<string, int> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        var mapped = new Dictionary<TlStat, int>();

        foreach (var (key, value) in attributes)
        {
            if (QuestlogAttributeKeys.TryGetValue(key, out var stat))
            {
                mapped[stat] = value;
            }
        }

        return mapped;
    }

    public static string QuestlogKeyFor(TlStat stat) => stat switch
    {
        TlStat.Strength => "str",
        TlStat.Dexterity => "dex",
        TlStat.Wisdom => "int",
        TlStat.Perception => "per",
        TlStat.Fortitude => "con",
        _ => throw new ArgumentOutOfRangeException(nameof(stat)),
    };

    /// <summary>
    /// What each stat grants at each rung, verbatim from its tooltip. Used to price a breakpoint
    /// that a redistribution would gain or give up — "costs Damage Reduction 30" is the part that
    /// makes a recommendation honest, and it is only available from a table like this one.
    /// </summary>
    public static readonly IReadOnlyDictionary<TlStat, IReadOnlyDictionary<int, string>> Effects =
        new Dictionary<TlStat, IReadOnlyDictionary<int, string>>
        {
            [TlStat.Strength] = new Dictionary<int, string>
            {
                [30] = "Max Health 750",
                [40] = "Damage Reduction 30",
                [50] = "Heavy Attack Chance 100",
                [60] = "Max Health 900",
                [70] = "Max Health 450 · Melee Defense 200 · Ranged Defense 200",
                [80] = "Max Health 450 · Heavy Attack Chance 60",
                [100] = "Max Health 600 · Damage Reduction 18",
                [120] = "Max Health 600 · Heavy Attack Damage 5%",
            },
            [TlStat.Dexterity] = new Dictionary<int, string>
            {
                [30] = "Critical Hit Chance 100",
                [40] = "Bonus Damage 30",
                [50] = "Movement Speed 5%",
                [60] = "Critical Hit Chance 120",
                [70] = "Critical Hit Chance 60 · Evasion 120",
                [80] = "Critical Hit Chance 60 · Bonus Damage 18",
                [100] = "Critical Hit Chance 60 · Attack Speed 4%",
                [120] = "Critical Hit Chance 60 · Critical Damage 4%",
            },
            [TlStat.Wisdom] = new Dictionary<int, string>
            {
                [30] = "Max Mana 750",
                [40] = "Debuff Duration −5%",
                [50] = "Cooldown Speed 5%",
                [60] = "Max Mana 900",
                [70] = "Max Mana 450 · Mana Regen 120",
                [80] = "Max Mana 450 · Cooldown Speed 3%",
                [100] = "Max Mana 600 · Mana Cost Efficiency 3%",
                [120] = "Max Mana 600 · Max Damage 10",
            },
            [TlStat.Perception] = new Dictionary<int, string>
            {
                [30] = "Hit Chance 100",
                [40] = "Buff Duration 5%",
                [50] = "Range 7.5%",
                [60] = "Hit Chance 120",
                [70] = "Hit Chance 60 · CC Chance 100",
                [80] = "Hit Chance 60 · Buff Duration 3%",
                [100] = "Hit Chance 60 · Range 5%",
                [120] = "Hit Chance 60 · CC Chance 100",
            },
            [TlStat.Fortitude] = new Dictionary<int, string>
            {
                [30] = "Endurance 100",
                [40] = "Magic Defense 200",
                [50] = "Heavy Attack Evasion 100",
                [60] = "Endurance 120",
                [70] = "Endurance 60 · CC Resistances 100",
                [80] = "Endurance 60 · Heavy Attack Evasion 60",
                [100] = "Endurance 60 · Critical Damage Resistance 4%",
                [120] = "Endurance 60 · Heavy Attack Damage Resistance 5%",
            },
        };

    public static string EffectAt(TlStat stat, int threshold) =>
        Effects[stat].TryGetValue(threshold, out var effect) ? effect : $"(unknown effect at {threshold})";

    /// <summary>
    /// Which damage components a stat feeds. Stated outright in each stat's own descriptor, so
    /// unlike the per-point multipliers this split is observed rather than inferred.
    /// </summary>
    public static bool FeedsMinDamage(TlStat stat) =>
        stat is TlStat.Strength or TlStat.Perception or TlStat.Fortitude;
}

public enum TlStat
{
    Strength,
    Dexterity,

    /// <summary>questlog calls this <c>int</c>. It is Wisdom in game.</summary>
    Wisdom,

    Perception,

    /// <summary>questlog calls this <c>con</c>. It is Fortitude in game.</summary>
    Fortitude,
}
