using System.Text;

namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// Prices a move from the character's current stat spread to an imported build's target spread.
///
/// <para>This is deterministic arithmetic and it is done here rather than in the prompt on
/// purpose. The recorded failure this exists to prevent was not a wrong recommendation — moving
/// points out of Strength on a PvE healer is correct — it was a <em>correct recommendation
/// presented as a pure gain</em>, with the lost Strength 40 breakpoint never mentioned. A language
/// model asked to do this arithmetic in prose will sometimes get it right. Computing it makes the
/// cost impossible to omit, and leaves the model the job it is actually good at.</para>
///
/// <para>Three traps are handled explicitly, because each one silently produces a plausible wrong
/// answer:</para>
/// <list type="number">
/// <item>questlog stores <b>allocated</b> points, not totals. Base starts at 10, so
/// <c>base = 10 + allocated</c>.</item>
/// <item>A target spread assumes <b>the build author's equipment</b>. Copying their allocation onto
/// a character with different gear lands on a different total, so the target is re-projected
/// through <em>this</em> character's equipment contribution.</item>
/// <item>Point cost escalates on <b>base</b>, not on the displayed total. A stat showing 96 with
/// base 30 is in the expensive band; one showing 96 with base 12 is not.</item>
/// </list>
/// </summary>
public static class StatPlanner
{
    /// <summary>
    /// Stat points needed to raise a stat's <em>base</em> from one value to another.
    ///
    /// <para>Three bands: one point each below <see cref="TlStats.EscalationBase"/>, two at or above
    /// it, and <b>four</b> at or above <see cref="TlStats.SecondEscalationBase"/>. This is why
    /// distance-to-threshold is the wrong way to rank candidates — four points into a base-30 stat
    /// costs the same as eight into a base-10 one, and only two into a base-50 one.</para>
    ///
    /// <para>The 4x band used to be missing here, with a caveat saying costs for very high bases
    /// were probably understated. They were: a base-50 stat was being priced at half its real cost,
    /// so any recommendation touching a heavily-invested stat came out looking cheaper than it is.
    /// The band is now exact — see <see cref="TlStats.SecondEscalationBase"/> for the derivation.</para>
    /// </summary>
    public static int PointsToRaise(int fromBase, int toBase)
    {
        var cost = 0;

        for (var value = fromBase; value < toBase; value++)
        {
            cost += value >= TlStats.SecondEscalationBase ? 4
                : value >= TlStats.EscalationBase ? 2
                : 1;
        }

        return cost;
    }

    /// <summary>
    /// Points returned by lowering a base, assumed to be what those same points cost to buy.
    ///
    /// <para><b>Assumption, not an observation.</b> CLAUDE.md confirms the spend ladder and that
    /// redistribution exists; it does not state the refund rate, and it flags that the price of
    /// redistribution in 4.5.0 is unconfirmed. Symmetry is the reasonable default and it is
    /// surfaced as a caveat rather than presented as fact.</para>
    /// </summary>
    public static int PointsRefunded(int fromBase, int toBase) => PointsToRaise(toBase, fromBase);

    /// <summary>Thresholds at or below <paramref name="total"/>, i.e. the ones currently held.</summary>
    public static IReadOnlyList<int> ThresholdsHeld(int total) =>
        TlStats.Ladder.Where(t => total >= t).ToArray();

    /// <summary>
    /// The next rung above <paramref name="total"/>, or null when the ladder is exhausted.
    /// </summary>
    public static int? NextThreshold(int total) =>
        TlStats.Ladder.Cast<int?>().FirstOrDefault(t => total < t);

    /// <summary>
    /// Builds the full move, one entry per stat the target names.
    /// </summary>
    /// <param name="current">
    /// What the character sheet showed. <see cref="StatObservation.Base"/> may be null — the sheet
    /// shows only the total, and the Base/Equipment/Stellar Journey split lives in the stat's hover
    /// tooltip. Stats missing it are reported as unpriceable rather than guessed at.
    /// </param>
    /// <param name="targetAllocated">The build's <c>attributes</c>, already mapped to stats.</param>
    public static RedistributionPlan Plan(
        IReadOnlyList<StatObservation> current,
        IReadOnlyDictionary<TlStat, int> targetAllocated)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(targetAllocated);

        var byStat = current.ToDictionary(o => o.Stat);
        var moves = new List<StatMove>();
        var unpriceable = new List<string>();

        // Structured alongside the prose above, because the same fact has two audiences: the dev
        // console reads English, and the player reads their own language. Formatting it once as a
        // sentence forced the app to either ship English or re-derive the reason from nothing.
        var reasons = new List<UnpriceableStat>();

        foreach (var stat in TlStats.All)
        {
            if (!targetAllocated.TryGetValue(stat, out var target))
            {
                continue;
            }

            if (!byStat.TryGetValue(stat, out var observed))
            {
                unpriceable.Add($"{stat}: not read from the character sheet, so no comparison is possible.");
                reasons.Add(new UnpriceableStat { Stat = stat, Total = null, Reason = UnpriceableReason.NotRead });
                continue;
            }

            if (observed.Base is not { } currentBase)
            {
                unpriceable.Add(
                    $"{stat}: showing {observed.Total}, but the Base/Equipment split is unknown. " +
                    "Hover the stat and capture its tooltip — cost depends on base, not on the displayed total.");
                reasons.Add(new UnpriceableStat
                {
                    Stat = stat,
                    Total = observed.Total,
                    Reason = UnpriceableReason.SplitUnknown,
                });
                continue;
            }

            // Everything the character gets from gear and Stellar Journey. Held fixed across the
            // move: redistribution changes allocation, not equipment.
            var external = observed.Total - currentBase;
            var projectedBase = TlStats.StartingBase + target;
            var projectedTotal = projectedBase + external;

            var heldNow = ThresholdsHeld(observed.Total);
            var heldAfter = ThresholdsHeld(projectedTotal);

            moves.Add(new StatMove
            {
                Stat = stat,
                CurrentTotal = observed.Total,
                CurrentBase = currentBase,
                ExternalContribution = external,
                CurrentAllocated = currentBase - TlStats.StartingBase,
                TargetAllocated = target,
                ProjectedBase = projectedBase,
                ProjectedTotal = projectedTotal,
                PointCost = projectedBase >= currentBase
                    ? PointsToRaise(currentBase, projectedBase)
                    : -PointsRefunded(currentBase, projectedBase),
                ThresholdsGained = heldAfter.Except(heldNow).ToArray(),
                ThresholdsLost = heldNow.Except(heldAfter).ToArray(),
            });
        }

        return new RedistributionPlan
        {
            Moves = moves,
            Unpriceable = unpriceable,
            UnpriceableStats = reasons,
        };
    }
}

/// <summary>One stat as read from the screen.</summary>
public sealed record StatObservation
{
    public required TlStat Stat { get; init; }

    /// <summary>The number printed on the character sheet: base + equipment + Stellar Journey.</summary>
    public required int Total { get; init; }

    /// <summary>
    /// The base component, from the stat tooltip's source breakdown. Null when only the sheet was
    /// captured — which is the common case, since the breakdown needs a hover.
    /// </summary>
    public int? Base { get; init; }
}

public sealed record StatMove
{
    public required TlStat Stat { get; init; }
    public required int CurrentTotal { get; init; }
    public required int CurrentBase { get; init; }

    /// <summary>Equipment plus Stellar Journey. Unchanged by redistribution.</summary>
    public required int ExternalContribution { get; init; }

    public required int CurrentAllocated { get; init; }
    public required int TargetAllocated { get; init; }
    public required int ProjectedBase { get; init; }

    /// <summary>
    /// Where the stat actually lands — the target's allocation re-projected through this
    /// character's gear. Not the build author's total, which is the number it is tempting to quote.
    /// </summary>
    public required int ProjectedTotal { get; init; }

    /// <summary>Positive to spend, negative to refund.</summary>
    public required int PointCost { get; init; }

    public required IReadOnlyList<int> ThresholdsGained { get; init; }

    /// <summary>The part that must never be omitted from a recommendation.</summary>
    public required IReadOnlyList<int> ThresholdsLost { get; init; }

    public bool IsNoOp => CurrentAllocated == TargetAllocated;

    public string Describe()
    {
        if (IsNoOp)
        {
            return $"{Stat} {CurrentTotal} — already matches the build ({TargetAllocated} allocated). No change.";
        }

        var direction = PointCost >= 0 ? "costs" : "refunds";
        var builder = new StringBuilder()
            .Append(Stat)
            .Append(' ')
            .Append(CurrentTotal)
            .Append(" → ")
            .Append(ProjectedTotal)
            .Append(" (allocated ")
            .Append(CurrentAllocated)
            .Append(" → ")
            .Append(TargetAllocated)
            .Append(", base ")
            .Append(CurrentBase)
            .Append(" → ")
            .Append(ProjectedBase)
            .Append(" + ")
            .Append(ExternalContribution)
            .Append(" from gear); ")
            .Append(direction)
            .Append(' ')
            .Append(Math.Abs(PointCost))
            .Append(" stat points");

        foreach (var threshold in ThresholdsGained)
        {
            builder.Append("; GAINS ").Append(threshold).Append(" tier — ").Append(TlStats.EffectAt(Stat, threshold));
        }

        foreach (var threshold in ThresholdsLost)
        {
            builder.Append("; COSTS the ").Append(threshold).Append(" tier — ").Append(TlStats.EffectAt(Stat, threshold));
        }

        return builder.ToString();
    }
}

public sealed record RedistributionPlan
{
    public required IReadOnlyList<StatMove> Moves { get; init; }

    /// <summary>
    /// Stats that could not be priced, as English prose. Used by the dev console and kept so its output
    /// does not change; the app renders <see cref="UnpriceableStats"/> instead so the player sees their
    /// own language.
    /// </summary>
    public required IReadOnlyList<string> Unpriceable { get; init; }

    /// <summary>The same facts, structured, so any caller can phrase them however it needs to.</summary>
    public IReadOnlyList<UnpriceableStat> UnpriceableStats { get; init; } = [];

    public IEnumerable<StatMove> Changes => Moves.Where(m => !m.IsNoOp);

    public int PointsSpent => Moves.Where(m => m.PointCost > 0).Sum(m => m.PointCost);

    public int PointsRefunded => Moves.Where(m => m.PointCost < 0).Sum(m => -m.PointCost);

    /// <summary>Positive means the move needs more points than it frees up.</summary>
    public int NetPointCost => PointsSpent - PointsRefunded;

    public bool HasChanges => Changes.Any();

    /// <summary>
    /// True when the move pays for itself out of points already allocated — the case worth leading
    /// with, because it costs no Sollant, no tokens, and no time.
    /// </summary>
    public bool IsSelfFunding => NetPointCost <= 0;

    public IReadOnlyList<StatMove> LosingThresholds =>
        Changes.Where(m => m.ThresholdsLost.Count > 0).ToArray();

    /// <summary>Renders the plan for both the console and the model prompt, so they cannot disagree.</summary>
    public string Describe()
    {
        var builder = new StringBuilder();

        if (Moves.Count == 0)
        {
            // NOT "already matches". Nothing was priced, so nothing was compared — and claiming a
            // match here asserts the spread is correct on the strength of no evidence at all. It
            // printed that for two mutually incompatible target builds in a row before this was
            // separated out, which is exactly how a confident wrong answer looks from the outside.
            builder.AppendLine(
                "No stat could be compared: the Base/Equipment split was not available for any of them, "
                + "and cost depends on base rather than the displayed total.");
            builder.AppendLine(
                "This says nothing about whether the spread is right — only that it could not be checked.");
        }
        else if (!HasChanges)
        {
            builder.Append("Stat spread already matches the target build for the ")
                .Append(Moves.Count)
                .AppendLine(Moves.Count == 1 ? " stat that could be priced." : " stats that could be priced.");

            if (Unpriceable.Count > 0)
            {
                builder.AppendLine("The rest were not checked — see below.");
            }
        }
        else
        {
            foreach (var move in Changes)
            {
                builder.Append("- ").AppendLine(move.Describe());
            }

            builder.AppendLine();
            builder.Append("Net: ")
                .Append(PointsSpent)
                .Append(" stat points spent, ")
                .Append(PointsRefunded)
                .Append(" refunded, net ")
                .Append(NetPointCost >= 0 ? "+" : string.Empty)
                .Append(NetPointCost)
                .AppendLine(IsSelfFunding
                    ? ". Funded entirely by reallocating points already spent."
                    : ". Needs that many more points than the move frees up.");

            if (LosingThresholds.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Breakpoints given up by this move (state these alongside the benefit):");

                foreach (var move in LosingThresholds)
                {
                    foreach (var threshold in move.ThresholdsLost)
                    {
                        builder.Append("- ")
                            .Append(move.Stat)
                            .Append(' ')
                            .Append(threshold)
                            .Append(": ")
                            .AppendLine(TlStats.EffectAt(move.Stat, threshold));
                    }
                }
            }
        }

        if (Unpriceable.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Could not price:");

            foreach (var reason in Unpriceable)
            {
                builder.Append("- ").AppendLine(reason);
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Things the arithmetic above assumes rather than knows. Carried into the prompt so the
    /// assistant hedges in the same places the data does, instead of inventing its own confidence.
    /// </summary>
    public static IReadOnlyList<string> Caveats =>
    [
        "Point costs model all three bands: 1x below base 30, 2x from 30, and 4x from base 50.",

        "Refunds are assumed to return what the same points cost to buy. The refund rate is not " +
        "confirmed, and whether redistribution itself carries a fee or cooldown in 4.5.0 is not " +
        "confirmed either.",

        "Projected totals hold equipment and Stellar Journey contributions constant. New gear changes " +
        "them, so a re-check is due after any upgrade.",
    ];
}

/// <summary>Why a stat could not be priced. Two cases, and they need different advice.</summary>
public enum UnpriceableReason
{
    /// <summary>The stat was not on the captured screen at all.</summary>
    NotRead,

    /// <summary>
    /// The total was read but the Base/Equipment split was not, and cost depends on base. Fixed by one
    /// hover — which is why it is worth telling the player apart from the case above.
    /// </summary>
    SplitUnknown,
}

/// <summary>One stat that could not be priced, with enough detail to say so in any language.</summary>
public sealed record UnpriceableStat
{
    public required TlStat Stat { get; init; }

    /// <summary>The displayed total, when it was read. Null for <see cref="UnpriceableReason.NotRead"/>.</summary>
    public required int? Total { get; init; }

    public required UnpriceableReason Reason { get; init; }
}
