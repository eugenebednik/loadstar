using Loadstar.Core.Model;
using Loadstar.Games.ThroneAndLiberty;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// The class profiles are 45 sections and a player is one of them. Trimming to the player's own class
/// is both a cost saving and an attention one — this pack's header warns that an unbounded prompt makes
/// the model worse at the rules that matter, and 44 irrelevant profiles beside the right one is exactly
/// that.
/// </summary>
public sealed class KnowledgeTrimTests
{
    [Fact]
    public void TrimmingKeepsTheRequestedClassAndDropsTheOthers()
    {
        var trimmed = TlKnowledgePack.ForClass("Seeker");

        Assert.Contains("### Seeker —", trimmed, StringComparison.Ordinal);
        Assert.DoesNotContain("### Oracle —", trimmed, StringComparison.Ordinal);
        Assert.DoesNotContain("### Gladiator —", trimmed, StringComparison.Ordinal);

        // Exactly one profile survives.
        Assert.Equal(1, trimmed.Split("### ").Count(part => part.Contains(" — ", StringComparison.Ordinal)
            && part.Contains("Stat priority", StringComparison.Ordinal)));
    }

    /// <summary>
    /// The baseline is what makes a profile readable — "Perception 51%" means nothing without knowing
    /// Perception is 39% everywhere. Trimming must never take it.
    /// </summary>
    [Fact]
    public void TrimmingKeepsTheInterpretationRulesAndTheBaseline()
    {
        var trimmed = TlKnowledgePack.ForClass("Mystic");

        Assert.Contains("Perception dominates everywhere", trimmed, StringComparison.Ordinal);
        Assert.Contains("A trait at 1.0x is the meta, not the class", trimmed, StringComparison.Ordinal);
        Assert.Contains("NOT as a target", trimmed, StringComparison.Ordinal);

        // And the rest of the pack is untouched — this trims one section, not the document.
        Assert.Contains("Only the ALLOCATED points are redistributable", trimmed, StringComparison.Ordinal);
        Assert.Contains("ever DROPPED", trimmed, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unknown class returns the pack WHOLE. With no class identified there is no basis for choosing
    /// which profile to keep, and dropping all of them would silently remove knowledge.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Necromancer")]
    public void AnUnknownClassFallsBackToTheWholePack(string? className)
    {
        Assert.Equal(TlKnowledgePack.Text, TlKnowledgePack.ForClass(className));
    }

    /// <summary>Every real class must be trimmable, or some players silently get the untrimmed pack.</summary>
    [Fact]
    public void EveryClassCanBeTrimmedTo()
    {
        foreach (var name in TlClasses.All)
        {
            var trimmed = TlKnowledgePack.ForClass(name);

            Assert.True(
                trimmed.Length < TlKnowledgePack.Text.Length,
                $"{name} did not trim — its profile section is probably named differently");
            Assert.Contains($"### {name} —", trimmed, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The prompt uses it when the build names the weapons, so a pinned build costs fewer tokens than
    /// none — which is the opposite of the obvious expectation and worth asserting.
    /// </summary>
    [Fact]
    public void APinnedBuildMakesThePromptSmallerNotLarger()
    {
        var seeker = new TargetBuild
        {
            Id = "x", Name = "Seeker", Source = "questlog", WeaponTypes = ["bow", "wand"],
        };

        var withBuild = TlSystemPrompt.Build(seeker, ["pve"]).Length;
        var without = TlSystemPrompt.Build(null, []).Length;

        Assert.True(withBuild < without,
            $"expected the trimmed pack to make a pinned-build prompt smaller; got {withBuild} vs {without}");
    }

    /// <summary>
    /// A tripwire on the whole pack. It is cached so it is cheap per turn, but attention is not cached —
    /// if this fires, the answer is to move reference DATA behind a per-turn lookup, not to raise the
    /// number.
    /// </summary>
    [Fact]
    public void ThePackStaysWithinItsAttentionBudget()
    {
        Assert.True(
            TlKnowledgePack.EstimatedTokens < 16_000,
            $"the knowledge pack is ~{TlKnowledgePack.EstimatedTokens} tokens; move reference data to a "
            + "per-turn lookup rather than raising this");
    }
}
