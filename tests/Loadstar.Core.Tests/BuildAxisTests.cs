using Loadstar.Core.Model;
using Loadstar.Games.ThroneAndLiberty;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Axis detection. The property under protection is that <b>PvE is never the silent default</b> —
/// the reference character is defensively invested in PvP, and a tool that assumed PvE would try
/// to "correct" a deliberate build.
/// </summary>
public sealed class BuildAxisTests
{
    private static TargetBuild BuildWith(IReadOnlyList<string> tags, params string[] traits) => new()
    {
        Id = "b",
        Name = "test",
        Source = "questlog.gg",
        Tags = tags,
        Equipment = new Dictionary<string, TargetItem>
        {
            ["mainhand"] = new()
            {
                ItemId = "x",
                Traits = traits.ToDictionary(t => t, _ => 800),
            },
        },
    };

    [Fact]
    public void TagsAreAuthoritative()
    {
        var verdict = BuildAxis.Determine(BuildWith(["pvp"]), []);

        Assert.Equal(CombatAxis.Pvp, verdict.Axis);
        Assert.Equal(AxisEvidence.Tags, verdict.Evidence);
        Assert.True(verdict.IsConfident);
    }

    [Fact]
    public void CharacterLevelTagsCountAsWellAsBuildTags()
    {
        var verdict = BuildAxis.Determine(BuildWith([]), ["pve", "healer"]);

        Assert.Equal(CombatAxis.Pve, verdict.Axis);
    }

    [Fact]
    public void BuildTaggedBothWaysIsNotResolvedByGuessing()
    {
        // Hybrid builds exist. The honest answer is to ask which one they are playing now.
        var verdict = BuildAxis.Determine(BuildWith(["pvp", "pve"]), []);

        Assert.Equal(CombatAxis.Unknown, verdict.Axis);
        Assert.Contains("Ask", verdict.Reason);
    }

    [Fact]
    public void AccuracyWeightedTraitsReadAsPvpWhenUntagged()
    {
        var verdict = BuildAxis.Determine(BuildWith([], "all_accuracy", "melee_accuracy"), []);

        Assert.Equal(CombatAxis.Pvp, verdict.Axis);
        Assert.Equal(AxisEvidence.TraitFingerprint, verdict.Evidence);
    }

    [Fact]
    public void CriticalWeightedTraitsReadAsPveWhenUntagged()
    {
        var verdict = BuildAxis.Determine(BuildWith([], "all_critical_attack", "magic_critical_attack"), []);

        Assert.Equal(CombatAxis.Pve, verdict.Axis);
    }

    [Fact]
    public void FingerprintEvidenceIsNeverTreatedAsConfident()
    {
        // Trait inference is a tiebreak, not a conclusion — the caller should still confirm.
        var verdict = BuildAxis.Determine(BuildWith([], "all_accuracy"), []);

        Assert.False(verdict.IsConfident);
    }

    [Fact]
    public void BalancedTraitMixRefusesToPickASide()
    {
        var verdict = BuildAxis.Determine(BuildWith([], "all_accuracy", "all_critical_attack"), []);

        Assert.Equal(CombatAxis.Unknown, verdict.Axis);
        Assert.Contains("ambiguous", verdict.Reason);
    }

    [Fact]
    public void UniversalTraitsCarryNoSignal()
    {
        // all_double_attack appeared 131 times in PvP and 162 in PvE — it distinguishes nothing.
        var verdict = BuildAxis.Determine(BuildWith([], "all_double_attack", "attack_speed_modifier"), []);

        Assert.Equal(CombatAxis.Unknown, verdict.Axis);
        Assert.Equal(AxisEvidence.None, verdict.Evidence);
    }

    [Fact]
    public void HeroicTraitsContributeToTheFingerprint()
    {
        var build = new TargetBuild
        {
            Id = "b",
            Name = "t",
            Source = "questlog.gg",
            Equipment = new Dictionary<string, TargetItem>
            {
                ["mainhand"] = new() { ItemId = "x", Heroic = ["con"] },
            },
        };

        Assert.Equal(CombatAxis.Pvp, BuildAxis.Determine(build, []).Axis);
    }
}
