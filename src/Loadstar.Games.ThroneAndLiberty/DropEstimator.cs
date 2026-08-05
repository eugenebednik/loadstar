namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// Turns a drop probability into kill counts a player can act on.
///
/// <para>Computed here rather than asked of the model, for the same reason stat costs are: it is
/// arithmetic with one correct answer, and getting it wrong is highly visible. A player told
/// "about 130 kills" who is still empty at 400 will not trust anything else the tool says.</para>
///
/// <para><b>There is no number of kills that guarantees a random drop.</b> Each kill is an
/// independent trial, so the chance of never seeing it approaches zero without reaching it. Asking
/// for "how many until guaranteed" is reasonable and the honest answer is a confidence level, so
/// this reports the expected count alongside how many kills reach 50%, 90% and 99% — and
/// <see cref="DropEstimate.Describe"/> says plainly that none of them is a guarantee. The one
/// exception is a genuinely deterministic drop (probability 1), which is reported as such.</para>
/// </summary>
public static class DropEstimator
{
    /// <summary>
    /// Kills needed to reach <paramref name="confidence"/> chance of at least one drop.
    ///
    /// <para><c>n = ln(1 - confidence) / ln(1 - p)</c>, rounded up — the standard inversion of
    /// "probability of at least one success in n independent trials".</para>
    /// </summary>
    public static int KillsForConfidence(double probability, double confidence)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(confidence, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(confidence, 1);

        if (probability >= 1)
        {
            return 1;
        }

        if (probability <= 0)
        {
            return 0;
        }

        return (int)Math.Ceiling(Math.Log(1 - confidence) / Math.Log(1 - probability));
    }

    public static DropEstimate? Estimate(double probability, string? source = null, string? condition = null)
    {
        if (probability is <= 0 or > 1 || double.IsNaN(probability))
        {
            return null;
        }

        return new DropEstimate
        {
            Probability = probability,
            Source = source,
            Condition = condition,
            ExpectedKills = probability >= 1 ? 1 : (int)Math.Ceiling(1 / probability),
            KillsFor50Percent = KillsForConfidence(probability, 0.50),
            KillsFor90Percent = KillsForConfidence(probability, 0.90),
            KillsFor99Percent = KillsForConfidence(probability, 0.99),
        };
    }
}

public sealed record DropEstimate
{
    /// <summary>Drop chance as a fraction, e.g. 0.00751705.</summary>
    public required double Probability { get; init; }

    /// <summary>The NPC or container this drops from.</summary>
    public string? Source { get; init; }

    /// <summary>Any gating condition, e.g. <c>dungeonPointDrop</c>.</summary>
    public string? Condition { get; init; }

    /// <summary>Mean kills to one drop — <c>1/p</c>. Not the same as a 50% chance, which is lower.</summary>
    public required int ExpectedKills { get; init; }

    public required int KillsFor50Percent { get; init; }
    public required int KillsFor90Percent { get; init; }
    public required int KillsFor99Percent { get; init; }

    public double Percentage => Probability * 100;

    /// <summary>True for a deterministic drop, where a kill count really is a guarantee.</summary>
    public bool IsGuaranteed => Probability >= 1;

    /// <summary>
    /// Phrasing that gives the player the numbers they asked for without claiming a certainty the
    /// mathematics does not support.
    /// </summary>
    public string Describe()
    {
        if (IsGuaranteed)
        {
            return $"{FormatPercentage()} — guaranteed drop, one kill.";
        }

        var where = Source is null ? string.Empty : $" from {Source}";

        return
            $"{FormatPercentage()} drop rate{where}. Expect ~{ExpectedKills:N0} kills on average; " +
            $"{KillsFor50Percent:N0} gives you a 50% chance, {KillsFor90Percent:N0} gives 90%, " +
            $"{KillsFor99Percent:N0} gives 99%. No number guarantees it — each kill is an " +
            "independent roll.";
    }

    /// <summary>
    /// Formats the rate without rounding a rare drop to a misleading "0.0%". A sub-0.1% chance
    /// displayed as zero would make a multi-hundred-kill grind look free.
    /// </summary>
    public string FormatPercentage() => Percentage switch
    {
        >= 10 => $"{Percentage:0.#}%",
        >= 1 => $"{Percentage:0.##}%",
        _ => $"{Percentage:0.###}%",
    };
}
