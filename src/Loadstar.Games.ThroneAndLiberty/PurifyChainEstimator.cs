namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// Two-stage acquisition: a monster drops a <em>frozen</em> item, purifying it yields either the
/// item or cinders, and enough cinders craft the item outright.
///
/// <para><b>Why this needs its own model.</b> Treating it as an ordinary drop gets the answer wrong
/// in both directions. It understates the grind, because a frozen drop is not the item — there is a
/// second roll after it. And it overstates the risk far more seriously, because cinders accumulate:
/// a player who never wins the purify roll still reaches the item by crafting. That makes the
/// purify stage <b>bounded</b>, which an ordinary drop never is. Telling a player "no number
/// guarantees it" here would be false and would push them away from a path that actually has a
/// worst case.</para>
///
/// <para><b>The item outcome is not a fixed prize.</b> Purifying can also manifest a Potential
/// ability — an enhancement to a weapon mastery, +1 level to a skill, or a random stat. This model
/// deliberately does not price that: it answers "how many kills", and folding a low-probability
/// bonus into an expected value would quietly inflate every estimate. Present Potentials as upside
/// on top of the result, never as part of it.</para>
///
/// <para><b>Provenance.</b> The mechanic is as described by a player of the game (Redfrost items,
/// 2026-08-04). It has <b>not</b> been confirmed against questlog's API — the equipment catalogue is
/// equipment-only and carries no purify data, so the frozen item, the cinder currency and the craft
/// threshold would come from `database.getItem` on the relevant material ids, which has not yet been
/// checked. Treat the numbers this produces as conditional on inputs the caller supplies, and label
/// them as such until the data source is wired up.</para>
/// </summary>
public static class PurifyChainEstimator
{
    /// <summary>
    /// Estimates the cost of a purify chain.
    /// </summary>
    /// <param name="dropChance">Chance a kill yields the frozen item, 0..1.</param>
    /// <param name="purifyItemChance">Chance a purify yields the item itself, 0..1.</param>
    /// <param name="cindersPerPurify">Cinders granted when a purify does not yield the item.</param>
    /// <param name="cindersToCraft">Cinders needed to craft the item outright.</param>
    public static PurifyChainEstimate? Estimate(
        double dropChance,
        double purifyItemChance,
        int cindersPerPurify,
        int cindersToCraft)
    {
        if (dropChance is <= 0 or > 1 || double.IsNaN(dropChance))
        {
            return null;
        }

        if (purifyItemChance is < 0 or > 1 || double.IsNaN(purifyItemChance))
        {
            return null;
        }

        if (cindersPerPurify <= 0 || cindersToCraft <= 0)
        {
            return null;
        }

        // The pity ceiling: even winning no purify roll, this many purifies funds the craft.
        var maxPurifies = (int)Math.Ceiling((double)cindersToCraft / cindersPerPurify);

        // Expected purifies is a geometric truncated at that ceiling — you stop either when a
        // purify yields the item or when cinders reach the threshold, whichever comes first.
        var expectedPurifies = 0.0;

        for (var n = 1; n <= maxPurifies; n++)
        {
            expectedPurifies += n * purifyItemChance * Math.Pow(1 - purifyItemChance, n - 1);
        }

        expectedPurifies += maxPurifies * Math.Pow(1 - purifyItemChance, maxPurifies);

        return new PurifyChainEstimate
        {
            DropChance = dropChance,
            PurifyItemChance = purifyItemChance,
            CindersPerPurify = cindersPerPurify,
            CindersToCraft = cindersToCraft,
            MaxPurifies = maxPurifies,
            ExpectedPurifies = expectedPurifies,
            ExpectedKills = (int)Math.Ceiling(expectedPurifies / dropChance),
            WorstCaseKills = (int)Math.Ceiling(maxPurifies / dropChance),
            ChanceOfItemBeforeCrafting = 1 - Math.Pow(1 - purifyItemChance, maxPurifies),
        };
    }
}

public sealed record PurifyChainEstimate
{
    public required double DropChance { get; init; }
    public required double PurifyItemChance { get; init; }
    public required int CindersPerPurify { get; init; }
    public required int CindersToCraft { get; init; }

    /// <summary>
    /// Purifies after which the item is craftable regardless of luck. This is a real ceiling — the
    /// one genuine guarantee anywhere in this acquisition model.
    /// </summary>
    public required int MaxPurifies { get; init; }

    public required double ExpectedPurifies { get; init; }

    /// <summary>Average kills to finish the whole chain, both stages combined.</summary>
    public required int ExpectedKills { get; init; }

    /// <summary>
    /// Kills to reach the cinder ceiling on average. Note this is <em>not</em> a guaranteed kill
    /// count: the purify stage becomes certain, the drop stage never does.
    /// </summary>
    public required int WorstCaseKills { get; init; }

    /// <summary>Odds of winning a purify roll before cinders would have paid for it anyway.</summary>
    public required double ChanceOfItemBeforeCrafting { get; init; }

    public string Describe() =>
        $"Frozen drop {DropChance * 100:0.###}% per kill, then purify: {PurifyItemChance * 100:0.#}% " +
        $"yields the item, otherwise {CindersPerPurify} cinder(s). " +
        $"{CindersToCraft} cinders craft it outright, so {MaxPurifies} purifies guarantee it even " +
        $"with no lucky roll. Expect ~{ExpectedKills:N0} kills; " +
        $"~{WorstCaseKills:N0} if the purify roll never lands. " +
        $"{ChanceOfItemBeforeCrafting * 100:0.#}% chance you win a purify before crafting becomes " +
        "necessary. The craft path bounds the purify stage — the drop stage stays random.";
}
