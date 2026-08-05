using Loadstar.Core.Model;

namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// Works out whether a build is aimed at PvP or PvE, because almost every gear and trait
/// recommendation is only correct relative to one of them.
///
/// <para>The character sheet showed a character invested defensively in PvP (−10% damage taken)
/// with no offensive PvP damage at all. That is a coherent choice, and a tool that assumed PvE
/// would have tried to "fix" it. So the axis is established first and never silently defaulted.</para>
///
/// <para>Tags are the authority. The trait fingerprint is a fallback, measured from five pages each
/// of pvp- and pve-tagged questlog builds (180 weapon slots per side, 2026-08-04): PvP builds buy
/// <c>all_accuracy</c> at roughly 1.7x the PvE rate, PvE builds buy <c>all_critical_attack</c> at
/// roughly 1.4x the PvP rate. Player targets stack Evasion and mobs do not, so hit chance is
/// contested in PvP and largely solved in PvE.</para>
/// </summary>
public static class BuildAxis
{
    /// <summary>Traits that lean PvP, with the weight of the observed signal.</summary>
    private static readonly IReadOnlyDictionary<string, int> PvpMarkers =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["all_accuracy"] = 2,
            ["melee_accuracy"] = 1,
            ["range_accuracy"] = 1,
            ["magic_accuracy"] = 1,
            ["con"] = 2,
        };

    private static readonly IReadOnlyDictionary<string, int> PveMarkers =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["all_critical_attack"] = 2,
            ["melee_critical_attack"] = 1,
            ["range_critical_attack"] = 1,
            ["magic_critical_attack"] = 1,
            ["dex"] = 2,
        };

    /// <summary>
    /// Determines the axis, preferring explicit tags and falling back to the trait fingerprint.
    /// Returns <see cref="CombatAxis.Unknown"/> rather than guessing when neither is conclusive —
    /// the caller is expected to ask.
    /// </summary>
    public static AxisVerdict Determine(TargetBuild build, IReadOnlyList<string> characterTags)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(characterTags);

        var tags = characterTags.Concat(build.Tags).ToArray();

        var hasPvp = tags.Any(t => t.Contains("pvp", StringComparison.OrdinalIgnoreCase));
        var hasPve = tags.Any(t => t.Contains("pve", StringComparison.OrdinalIgnoreCase));

        // Tagged both ways is not a contradiction to resolve by coin flip — hybrid builds exist,
        // and the honest answer is that the player has to say which they are playing right now.
        if (hasPvp && hasPve)
        {
            return new AxisVerdict(CombatAxis.Unknown, AxisEvidence.Tags,
                "Tagged both pvp and pve. Ask which the player is gearing for before advising.");
        }

        if (hasPvp)
        {
            return new AxisVerdict(CombatAxis.Pvp, AxisEvidence.Tags, "Build is tagged pvp.");
        }

        if (hasPve)
        {
            return new AxisVerdict(CombatAxis.Pve, AxisEvidence.Tags, "Build is tagged pve.");
        }

        return FromTraits(build);
    }

    /// <summary>
    /// Scores the build's traits against the measured markers. Deliberately weak evidence: it is
    /// a tiebreak when tags are absent, not a substitute for them.
    /// </summary>
    public static AxisVerdict FromTraits(TargetBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);

        var pvp = 0;
        var pve = 0;

        foreach (var item in build.Equipment.Values)
        {
            foreach (var trait in item.Traits.Keys.Concat(item.Heroic))
            {
                if (PvpMarkers.TryGetValue(trait, out var pvpWeight))
                {
                    pvp += pvpWeight;
                }

                if (PveMarkers.TryGetValue(trait, out var pveWeight))
                {
                    pve += pveWeight;
                }
            }
        }

        if (pvp == 0 && pve == 0)
        {
            return new AxisVerdict(CombatAxis.Unknown, AxisEvidence.None,
                "No tags and no marker traits. Ask the player which axis they are gearing for.");
        }

        // A near-tie is not a verdict. Demanding a clear margin keeps a one-trait accident from
        // flipping every recommendation that follows.
        var margin = Math.Abs(pvp - pve);
        var total = pvp + pve;

        if (margin * 3 < total)
        {
            return new AxisVerdict(CombatAxis.Unknown, AxisEvidence.TraitFingerprint,
                $"Trait mix is ambiguous (PvP {pvp} vs PvE {pve}). Ask rather than assume.");
        }

        return pvp > pve
            ? new AxisVerdict(CombatAxis.Pvp, AxisEvidence.TraitFingerprint,
                $"Untagged, but accuracy-weighted traits read PvP (PvP {pvp} vs PvE {pve}).")
            : new AxisVerdict(CombatAxis.Pve, AxisEvidence.TraitFingerprint,
                $"Untagged, but critical-weighted traits read PvE (PvE {pve} vs PvP {pvp}).");
    }
}

public enum CombatAxis
{
    /// <summary>Not established. Ask — never quietly default to PvE.</summary>
    Unknown = 0,
    Pve,
    Pvp,
}

public enum AxisEvidence
{
    None = 0,

    /// <summary>Author-supplied build tags. Authoritative.</summary>
    Tags,

    /// <summary>Inferred from trait composition. Weak — a tiebreak, not a conclusion.</summary>
    TraitFingerprint,
}

public sealed record AxisVerdict(CombatAxis Axis, AxisEvidence Evidence, string Reason)
{
    /// <summary>True when the axis is settled well enough to shape advice without asking first.</summary>
    public bool IsConfident => Axis != CombatAxis.Unknown && Evidence == AxisEvidence.Tags;
}
