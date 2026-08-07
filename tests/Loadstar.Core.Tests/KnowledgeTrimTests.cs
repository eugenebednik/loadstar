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
    /// Tripwires on what ACTUALLY reaches the model, which is not the same as the whole pack.
    ///
    /// <para>The earlier version of this capped <c>TlKnowledgePack.EstimatedTokens</c> — the untrimmed
    /// pack — which is never sent at all when a build is pinned, because the class profiles are trimmed
    /// to one. So it was measuring a number no request pays for, and it was about to fail on knowledge
    /// that costs the real path nothing.</para>
    ///
    /// <para>These are cost AND attention limits. The pack is cached so it is cheap per turn after the
    /// first, but attention is not cached: past some size the model gets worse at the specific rules
    /// that matter. If one of these fires, the answer is to move reference DATA behind a per-turn lookup
    /// — the way item and drop data already work — rather than to raise the number.</para>
    /// </summary>
    [Fact]
    public void WhatActuallyShipsStaysWithinBudget()
    {
        // The normal path: a pinned build, so exactly one class profile travels.
        var shipped = TlKnowledgePack.ForClass("Seeker").Length / 4;

        Assert.True(shipped < 14_000, $"the trimmed pack is ~{shipped} tokens; move reference data to a lookup");

        // The fallback path, when no class is known and all 45 profiles travel. Looser on purpose, but
        // still bounded — it is the case that grows fastest as classes are added.
        var whole = TlKnowledgePack.EstimatedTokens;

        Assert.True(whole < 17_000, $"the untrimmed pack is ~{whole} tokens");
        Assert.True(whole > shipped, "trimming saved nothing, so the class filter has stopped working");
    }

    /// <summary>
    /// The assembled prompt, in the three shapes that actually ship.
    ///
    /// <para><b>The equipped case is the one that counts, and it used to be missing.</b> This test measured
    /// a <see cref="TargetBuild"/> with an empty <c>Equipment</c> dictionary, which no real pinned build
    /// has — so it was asserting on a prompt ~1,700 tokens smaller than the one that gets sent. Measured
    /// against the live reference build (questlog 8344612, 30 filled slots) the real figure is ~23,400,
    /// because each slot carries three runes and three traits. The old 23,000 bound was never tested
    /// against that and a real build would have sailed past it unnoticed.</para>
    ///
    /// <para>The no-build case stays the other tight one: it carries every class profile AND the candidate
    /// builds, so it is dense even though it has the least information in it.</para>
    /// </summary>
    [Fact]
    public void TheAssembledPromptStaysWithinBudget()
    {
        var bare = new TargetBuild
        {
            Id = "x", Name = "Seeker", Source = "questlog", WeaponTypes = ["bow", "wand"],
        };

        var equipped = bare with { Equipment = ReferenceEquipment() };

        var withBuild = TlSystemPrompt.Build(bare, ["pve"]).Length / 4;
        var withGear = TlSystemPrompt.Build(equipped, ["pve"]).Length / 4;
        var without = TlSystemPrompt.Build(null, []).Length / 4;

        Assert.True(withBuild < 23_000, $"prompt with a bare build is ~{withBuild} tokens");
        Assert.True(without < 25_000, $"prompt with no build is ~{without} tokens");
        Assert.True(
            withGear < 25_000,
            $"prompt with a fully equipped build is ~{withGear} tokens — the real shipping case");
        Assert.True(withGear > withBuild, "equipment added nothing, so the equipment section has broken");
    }

    /// <summary>
    /// Thirty slots at the density a real questlog build has: three runes and three traits each. The names
    /// are shaped like the real ones rather than copied, because this measures SIZE — what matters is the
    /// slot count and the per-slot payload, not which item is in it.
    /// </summary>
    private static Dictionary<string, TargetItem> ReferenceEquipment()
    {
        string[] slots =
        [
            "attack", "belt", "boonstone", "bracelet", "brooch", "castle", "chest", "cloak", "defense",
            "earring", "feet", "gemstone1", "gemstone2", "hands", "head", "hp-recovery", "legs",
            "main_hand", "mana-recovery", "necklace", "off_hand", "riftstone", "ring_1", "ring_2",
            "stellarite", "talistone1", "talistone2", "talistone3", "talistone4", "utility",
        ];

        return slots.ToDictionary(
            slot => slot,
            slot => new TargetItem
            {
                ItemId = $"{slot}_aa_t3_normal_004",
                Runes =
                [
                    new TargetRune { RuneId = "r1", StatId = "skill_power_resistance", Level = 120 },
                    new TargetRune { RuneId = "r2", StatId = "magic_double_defense", Level = 1 },
                    new TargetRune { RuneId = "r3", StatId = "magic_double_defense", Level = 120 },
                ],
                Traits = new Dictionary<string, int>
                {
                    ["hp_max"] = 600,
                    ["skill_power_resistance"] = 800,
                    ["debuff_taken_duration_modifier"] = -600,
                },
            });
    }
}
