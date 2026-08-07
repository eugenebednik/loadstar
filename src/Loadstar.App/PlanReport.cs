using System.Text;

using Loadstar.Games.ThroneAndLiberty;

namespace Loadstar.App;

/// <summary>
/// Renders a <see cref="RedistributionPlan"/> for the player, in their own language.
///
/// <para><b>Why not just translate RedistributionPlan.Describe().</b> That method lives in the
/// game-knowledge assembly, which cannot see <see cref="Strings"/>, and it has a second consumer: the
/// developer console in Loadstar.Poc, which wants English. The same facts have two audiences, so the
/// plan carries STRUCTURE and each caller phrases it for its own reader.</para>
///
/// <para>This is the third time that split has been the answer — <see cref="BossLabels"/> for the boss
/// widget and the class table before it. The rule it follows: the game layer holds facts, the app layer
/// holds sentences.</para>
///
/// <para><b>Stat names stay in English.</b> Strength, Dexterity, Wisdom, Perception and Fortitude are
/// labels the player has to find on their own character sheet, so translating them would make them
/// unfindable — the same reason boss names pass through untouched.</para>
/// </summary>
internal static class PlanReport
{
    /// <summary>The whole redistribution block, or an empty string when there is nothing to say.</summary>
    public static string Render(RedistributionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.HasChanges && plan.UnpriceableStats.Count == 0 && plan.Moves.Count == 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        var rule = new string('-', 70);

        text.AppendLine(rule);
        text.AppendLine(Strings.Get("plan.heading"));
        text.AppendLine(rule);

        if (plan.Moves.Count == 0)
        {
            // NOT "already matches". Nothing was priced, so nothing was compared, and claiming a match
            // would assert the spread is correct on the strength of no evidence.
            text.AppendLine(Strings.Get("plan.nothingCompared"));
            text.AppendLine(Strings.Get("plan.nothingCompared.caveat"));
        }
        else if (!plan.HasChanges)
        {
            text.AppendLine(string.Format(Strings.Get("plan.alreadyMatches"), plan.Moves.Count));

            if (plan.UnpriceableStats.Count > 0)
            {
                text.AppendLine(Strings.Get("plan.restNotChecked"));
            }
        }
        else
        {
            foreach (var move in plan.Changes)
            {
                text.Append("- ").AppendLine(Describe(move));
            }

            text.AppendLine();
            text.AppendLine(string.Format(
                Strings.Get(plan.IsSelfFunding ? "plan.netSelfFunded" : "plan.netShortfall"),
                plan.PointsSpent,
                plan.PointsRefunded,
                (plan.NetPointCost >= 0 ? "+" : string.Empty) + plan.NetPointCost));

            if (plan.LosingThresholds.Count > 0)
            {
                text.AppendLine();
                text.AppendLine(Strings.Get("plan.breakpointsLost"));

                foreach (var move in plan.LosingThresholds)
                {
                    foreach (var threshold in move.ThresholdsLost)
                    {
                        text.Append("- ")
                            .Append(move.Stat)
                            .Append(' ')
                            .Append(threshold)
                            .Append(": ")
                            .AppendLine(TlStats.EffectAt(move.Stat, threshold));
                    }
                }
            }
        }

        if (plan.UnpriceableStats.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(Strings.Get("plan.couldNotPrice"));

            foreach (var entry in plan.UnpriceableStats)
            {
                text.Append("- ").AppendLine(entry.Reason switch
                {
                    UnpriceableReason.NotRead =>
                        string.Format(Strings.Get("plan.notRead"), entry.Stat),
                    _ => string.Format(Strings.Get("plan.splitUnknown"), entry.Stat, entry.Total),
                });
            }
        }

        text.AppendLine();
        text.AppendLine(Strings.Get("plan.assumptions"));

        // Three fixed caveats. Keyed rather than taken from RedistributionPlan.Caveats, whose strings
        // are English prose for the console — but they must stay in step, so a test compares the counts.
        foreach (var key in CaveatKeys)
        {
            text.Append("  - ").AppendLine(Strings.Get(key));
        }

        return text.ToString();
    }

    /// <summary>The caveat keys, in the order RedistributionPlan states them.</summary>
    internal static readonly string[] CaveatKeys =
    [
        "plan.caveat.bands",
        "plan.caveat.refunds",
        "plan.caveat.constant",
    ];

    private static string Describe(StatMove move)
    {
        if (move.IsNoOp)
        {
            return string.Format(
                Strings.Get("plan.move.noChange"), move.Stat, move.CurrentTotal, move.TargetAllocated);
        }

        var text = new StringBuilder(string.Format(
            Strings.Get("plan.move"),
            move.Stat,
            move.CurrentTotal,
            move.ProjectedTotal,
            move.CurrentAllocated,
            move.TargetAllocated,
            move.CurrentBase,
            move.ProjectedBase,
            move.ExternalContribution));

        text.Append("; ").Append(string.Format(
            Strings.Get(move.PointCost >= 0 ? "plan.move.costs" : "plan.move.refunds"),
            Math.Abs(move.PointCost)));

        foreach (var threshold in move.ThresholdsGained)
        {
            text.Append("; ").Append(string.Format(
                Strings.Get("plan.move.gains"), threshold, TlStats.EffectAt(move.Stat, threshold)));
        }

        foreach (var threshold in move.ThresholdsLost)
        {
            text.Append("; ").Append(string.Format(
                Strings.Get("plan.move.loses"), threshold, TlStats.EffectAt(move.Stat, threshold)));
        }

        return text.ToString();
    }
}
