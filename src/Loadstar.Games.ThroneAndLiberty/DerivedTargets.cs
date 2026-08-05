using System.Text;

namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// A computed target, paired with the tables needed to state it in the units a player reads, and
/// able to render itself for the prompt.
///
/// <para>Rendering lives here rather than in the prompt builder because the framing is the hard
/// part, not the formatting. These numbers are <b>partial by construction</b> — traits, runes and
/// set bonuses only — while the number on a character sheet is a full total including base
/// attributes and item stats. Subtracting one from the other is meaningless, and it is the obvious
/// mistake to make, so <see cref="Describe"/> spends most of its words preventing it.</para>
/// </summary>
public sealed record DerivedTargets
{
    public required TargetStats Stats { get; init; }
    public required TraitReference Reference { get; init; }

    /// <summary>
    /// Stats worth putting in front of the model. Capped because the prompt is a cached prefix, not
    /// a data dump — thirty-odd rows of small contributions dilute attention on the ones that matter.
    /// </summary>
    private const int TopStats = 20;

    public string Describe()
    {
        var builder = new StringBuilder();

        builder.AppendLine("## What this build's GEAR contributes, computed exactly");
        builder.AppendLine();
        builder.AppendLine(
            "Below is what the target build's **traits, runes, rune synergies and set bonuses** add "
            + "up to, resolved from the build data rather than estimated. Values are in the units the "
            + "character sheet shows.");
        builder.AppendLine();

        builder.AppendLine("**READ THIS BEFORE USING THE NUMBERS.** They are a partial sum, and they");
        builder.AppendLine("are NOT comparable to a total on the character sheet:");
        builder.AppendLine();

        foreach (var excluded in TargetStats.ExcludedSources)
        {
            builder.Append("- excludes ").AppendLine(excluded);
        }

        builder.AppendLine();
        builder.AppendLine(
            "So DO NOT subtract these from what you read on screen, and do not say the player is "
            + "\"short by\" the difference. A sheet showing Melee Endurance 1,800 against a gear "
            + "contribution of 990 is not a 810 deficit — the rest comes from Fortitude and item "
            + "stats, which are not counted here.");
        builder.AppendLine();
        builder.AppendLine("What they ARE good for, and this is genuinely useful:");
        builder.AppendLine();
        builder.AppendLine(
            "1. **The build's intent and priorities.** Where the numbers are lopsided, that is a "
            + "deliberate choice by the build's author. If magic heavy attack evasion is five times "
            + "the melee figure, the build is magic-skewed BY DESIGN — do not report that same "
            + "asymmetry on the player's sheet as a weakness to fix. It is the target.");
        builder.AppendLine(
            "2. **Which stats the build cares about at all.** A stat absent from this list is one the "
            + "build's gear does not invest in, so it is not a priority however low it reads.");
        builder.AppendLine(
            "3. **Set progress**, which unlike the stat sums IS complete and directly actionable.");
        builder.AppendLine();

        AppendStats(builder);
        AppendSets(builder);
        AppendUnresolved(builder);

        return builder.ToString().TrimEnd();
    }

    private void AppendStats(StringBuilder builder)
    {
        var rows = Stats.ByStat.Values
            .OrderByDescending(stat => stat.Total)
            .Take(TopStats)
            .ToArray();

        if (rows.Length == 0)
        {
            builder.AppendLine("The build's gear carries no resolvable trait or rune contributions.");
            builder.AppendLine();
            return;
        }

        builder.Append("### Gear contribution by stat (top ").Append(rows.Length)
            .Append(" of ").Append(Stats.ByStat.Count).AppendLine(")");
        builder.AppendLine();
        builder.AppendLine("| Stat | From gear | Sources |");
        builder.AppendLine("| --- | ---: | --- |");

        foreach (var stat in rows)
        {
            var sources = string.Join(", ", stat.BySource
                .OrderByDescending(source => source.Value)
                .Select(source => $"{source.Key} {Reference.Format(stat.StatId, source.Value)}"));

            builder.Append("| ").Append(Reference.NameOf(stat.StatId))
                .Append(" | ").Append(Reference.Format(stat.StatId, stat.Total))
                .Append(" | ").Append(sources)
                .AppendLine(" |");
        }

        builder.AppendLine();
    }

    private void AppendSets(StringBuilder builder)
    {
        if (Stats.Sets.Count == 0)
        {
            return;
        }

        builder.AppendLine("### Gear sets in this build");
        builder.AppendLine();

        foreach (var set in Stats.Sets)
        {
            builder.Append("- **").Append(set.Set.Name).Append("** — ")
                .Append(set.Pieces).Append(" piece(s) across ")
                .Append(string.Join(", ", set.Slots)).Append(". ");

            if (set.PiecesToNext is { } needed)
            {
                builder.Append("**").Append(needed).Append(" more for the ")
                    .Append(set.Next!.PieceCount).Append("-piece bonus**");

                var reward = DescribeBonus(set.Next);

                builder.AppendLine(string.IsNullOrEmpty(reward) ? "." : $" ({reward}).");
            }
            else
            {
                builder.AppendLine("Complete.");
            }

            foreach (var active in set.Active)
            {
                var reward = DescribeBonus(active);

                if (!string.IsNullOrEmpty(reward))
                {
                    builder.Append("    - ").Append(active.PieceCount)
                        .Append("pc active: ").AppendLine(reward);
                }
            }
        }

        builder.AppendLine();
        builder.AppendLine(
            "A set sitting ONE piece short of a threshold is usually the best gear advice available, "
            + "and it is invisible from item level — a slot can look fine on level and still leave a "
            + "whole set bonus unclaimed. Prefer it over a bigger upgrade that costs more.");
        builder.AppendLine();
    }

    private string DescribeBonus(GearSetBonus bonus)
    {
        var parts = bonus.Stats
            .Select(stat => $"{Reference.NameOf(stat.Key)} {Reference.Format(stat.Key, stat.Value)}")
            .Concat(bonus.Passives.Select(Flatten))
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        return string.Join("; ", parts);
    }

    /// <summary>
    /// Collapses a passive's embedded newlines. Several set bonuses ship as multi-line prose, and
    /// dropping that into a markdown bullet breaks the list — the second line renders as body text
    /// outside the bullet it belongs to.
    /// </summary>
    private static string Flatten(string passive) =>
        string.Join("; ", passive
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private void AppendUnresolved(StringBuilder builder)
    {
        if (Stats.UnresolvedContributions.Count == 0)
        {
            return;
        }

        builder.Append("### Could not be resolved (")
            .Append(Stats.UnresolvedContributions.Count).AppendLine(")");
        builder.AppendLine();
        builder.AppendLine(
            "These contribute to the real target but are missing from the sums above, so the figures "
            + "understate it by however much these are worth:");
        builder.AppendLine();

        foreach (var item in Stats.UnresolvedContributions.Take(10))
        {
            builder.Append("- ").AppendLine(item);
        }

        builder.AppendLine();
    }
}
