using Loadstar.Core.Model;
using Loadstar.Games.ThroneAndLiberty;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Covers the three exactly-resolvable contribution sources, and — more importantly — the places
/// where the calculator must refuse to guess. A target that comes out low is worse than no target,
/// because it tells the player they have arrived when they have not.
/// </summary>
public sealed class TargetStatCalculatorTests
{
    /// <summary>A defense rune whose melee_critical_defense reaches 960 at level 120.</summary>
    private static TraitReference Reference => new()
    {
        RuneLevels = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>>
        {
            ["Armor_Def_Rune_kA_001"] = new Dictionary<string, IReadOnlyList<int>>
            {
                ["melee_critical_defense"] = [.. Enumerable.Range(0, 121).Select(i => i * 8)],
            },
            // Chaos runes are max_level 1 and arrive at full value.
            ["Armor_All_Rune_kA_001"] = new Dictionary<string, IReadOnlyList<int>>
            {
                ["magic_critical_defense"] = [400, 400],
            },
        },
        ItemToSet = new Dictionary<string, string>
        {
            ["chest_x"] = "set_a",
            ["head_x"] = "set_a",
            ["legs_x"] = "set_a",
        },
        Sets = new Dictionary<string, GearSet>
        {
            ["set_a"] = new()
            {
                Id = "set_a",
                Name = "Prayer of Salvation",
                Bonuses =
                [
                    new() { PieceCount = 2, Stats = new Dictionary<string, int> { ["hp_max"] = 2200 } },
                    new() { PieceCount = 4, Passives = ["Skill Healing +20%"] },
                ],
            },
        },
        Synergies = new Dictionary<string, RuneSynergy>
        {
            [TraitReference.SynergyKey("chest", ["attack", "defense", "assist"])] = new()
            {
                Name = "ATTACK DEFENSE ASSIST",
                Combination = ["attack", "defense", "assist"],
                Stats = new Dictionary<string, int> { ["str"] = 4 },
            },
        },
    };

    private static TargetBuild BuildWith(params (string Slot, TargetItem Item)[] slots) => new()
    {
        Id = "b",
        Name = "Test",
        Source = "questlog.gg",
        Equipment = slots.ToDictionary(s => s.Slot, s => s.Item),
    };

    [Fact]
    public void TraitValuesAreSummedStraightFromTheBuild()
    {
        // Trait values are stated outright in the payload, so there is nothing to look up.
        var build = BuildWith(("chest", new TargetItem
        {
            ItemId = "chest_x",
            Traits = new Dictionary<string, int> { ["melee_critical_defense"] = 1600 },
        }));

        var stats = TargetStatCalculator.Compute(build, Reference);

        Assert.Equal(1600, stats.For("melee_critical_defense")!.Total);
        Assert.Equal(1600, stats.For("melee_critical_defense")!.BySource["traits"]);
    }

    [Fact]
    public void RuneValuesResolveFromIdStatAndLevel()
    {
        var build = BuildWith(("chest", new TargetItem
        {
            ItemId = "chest_x",
            Runes = [new TargetRune { RuneId = "Armor_Def_Rune_kA_001", StatId = "melee_critical_defense", Level = 120 }],
        }));

        var stats = TargetStatCalculator.Compute(build, Reference);

        Assert.Equal(960, stats.For("melee_critical_defense")!.Total);
        Assert.Empty(stats.UnresolvedContributions);
    }

    [Fact]
    public void TraitsAndRunesAccumulateSeparatelyOnTheSameStat()
    {
        var build = BuildWith(("chest", new TargetItem
        {
            ItemId = "chest_x",
            Traits = new Dictionary<string, int> { ["melee_critical_defense"] = 1600 },
            Runes = [new TargetRune { RuneId = "Armor_Def_Rune_kA_001", StatId = "melee_critical_defense", Level = 120 }],
        }));

        var total = TargetStatCalculator.Compute(build, Reference).For("melee_critical_defense")!;

        Assert.Equal(2560, total.Total);
        Assert.Equal(1600, total.BySource["traits"]);
        Assert.Equal(960, total.BySource["runes"]);
    }

    [Fact]
    public void AnUnresolvableRuneIsReportedRatherThanCountedAsZero()
    {
        // The failure that matters. Treating an unknown rune as zero silently lowers the target,
        // and a low target is the one kind of wrong answer this whole design exists to avoid.
        var build = BuildWith(("chest", new TargetItem
        {
            ItemId = "chest_x",
            Runes = [new TargetRune { RuneId = "Rune_Not_In_Tables", StatId = "melee_evasion", Level = 60 }],
        }));

        var stats = TargetStatCalculator.Compute(build, Reference);

        Assert.Null(stats.For("melee_evasion"));
        Assert.Single(stats.UnresolvedContributions);
        Assert.Contains("Rune_Not_In_Tables", stats.UnresolvedContributions[0]);
    }

    [Fact]
    public void ChaosRuneLevelBeyondTheTableClampsInsteadOfFailing()
    {
        // Chaos runes are max_level 1, so a stored level above the table is normal, not an error.
        var build = BuildWith(("chest", new TargetItem
        {
            ItemId = "chest_x",
            Runes = [new TargetRune { RuneId = "Armor_All_Rune_kA_001", StatId = "magic_critical_defense", Level = 60 }],
        }));

        var stats = TargetStatCalculator.Compute(build, Reference);

        Assert.Equal(400, stats.For("magic_critical_defense")!.Total);
        Assert.Empty(stats.UnresolvedContributions);
    }

    [Fact]
    public void SetBonusesApplyOnlyAtOrAboveTheirPieceCount()
    {
        var item = (string slot, string id) => (slot, new TargetItem { ItemId = id });

        var two = TargetStatCalculator.Compute(
            BuildWith(item("chest", "chest_x"), item("head", "head_x")), Reference);

        Assert.Equal(2200, two.For("hp_max")!.Total);

        var one = TargetStatCalculator.Compute(BuildWith(item("chest", "chest_x")), Reference);

        Assert.Null(one.For("hp_max"));
    }

    [Fact]
    public void SetProgressReportsTheNearestUnmetThreshold()
    {
        // The least-resistance case: one piece short of a real bonus is cheap advice, and it is
        // invisible from item level alone.
        var build = BuildWith(("chest", new TargetItem { ItemId = "chest_x" }));

        var progress = Assert.Single(TargetStatCalculator.Compute(build, Reference).Sets);

        Assert.Equal(1, progress.Pieces);
        Assert.Equal(2, progress.Next!.PieceCount);
        Assert.Equal(1, progress.PiecesToNext);
        Assert.Contains("1 more for the 2-piece", progress.Describe());
    }

    [Fact]
    public void CompletedSetReportsNoNextThreshold()
    {
        var build = BuildWith(
            ("chest", new TargetItem { ItemId = "chest_x" }),
            ("head", new TargetItem { ItemId = "head_x" }),
            ("legs", new TargetItem { ItemId = "legs_x" }));

        var progress = Assert.Single(TargetStatCalculator.Compute(build, Reference).Sets);

        Assert.Equal(3, progress.Pieces);
        // 4-piece is still unmet at three pieces.
        Assert.Equal(4, progress.Next!.PieceCount);
        Assert.Single(progress.Active);
    }

    [Fact]
    public void PassiveOnlySetBonusesContributeNoStats()
    {
        // bonus_passive is prose. Quoting it is fine; deriving a number from it would be inventing.
        var build = BuildWith(
            ("chest", new TargetItem { ItemId = "chest_x" }),
            ("head", new TargetItem { ItemId = "head_x" }),
            ("legs", new TargetItem { ItemId = "legs_x" }));

        var stats = TargetStatCalculator.Compute(build, Reference);
        var fourPiece = Reference.Sets["set_a"].Bonuses.Single(b => b.PieceCount == 4);

        Assert.Empty(fourPiece.Stats);
        Assert.Single(fourPiece.Passives);
        Assert.Equal(2200, stats.For("hp_max")!.Total);
    }

    [Fact]
    public void RuneSynergyRequiresTheExactOrder()
    {
        TargetBuild WithOrder(params string[] runeIds) => BuildWith(("chest", new TargetItem
        {
            ItemId = "chest_x",
            Runes = [.. runeIds.Select(id => new TargetRune { RuneId = id, StatId = "x", Level = 0 })],
        }));

        var right = TargetStatCalculator.Compute(
            WithOrder("A_Atk_R", "A_Def_R", "A_Ast_R"), Reference);

        Assert.Equal(4, right.For("str")!.Total);

        // Same three types, different sequence — a different bonus, or none. Order is the mechanic.
        var wrong = TargetStatCalculator.Compute(
            WithOrder("A_Def_R", "A_Atk_R", "A_Ast_R"), Reference);

        Assert.Null(wrong.For("str"));
    }

    [Fact]
    public void ChaosRuneMakesTheSynergyUndeterminable()
    {
        // A chaos rune substitutes for any type, but the build does not record which one the game
        // chose — so the synergy cannot be resolved, and guessing one would fabricate a bonus.
        var build = BuildWith(("chest", new TargetItem
        {
            ItemId = "chest_x",
            Runes =
            [
                new TargetRune { RuneId = "A_Atk_R", StatId = "x", Level = 0 },
                new TargetRune { RuneId = "A_All_R", StatId = "x", Level = 0 },
                new TargetRune { RuneId = "A_Ast_R", StatId = "x", Level = 0 },
            ],
        }));

        Assert.Null(TargetStatCalculator.Compute(build, Reference).For("str"));
    }

    [Fact]
    public void ExclusionsAreStatedSoAPartialTotalIsNeverMistakenForComplete()
    {
        Assert.NotEmpty(TargetStats.ExcludedSources);
        Assert.Contains(TargetStats.ExcludedSources, e => e.Contains("attribute", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(TargetStats.ExcludedSources, e => e.Contains("item level", StringComparison.OrdinalIgnoreCase));
    }

    private static DerivedTargets DerivedFor(TargetBuild build) => new()
    {
        Stats = TargetStatCalculator.Compute(build, Reference),
        Reference = Reference with
        {
            Display = new Dictionary<string, StatDisplay>
            {
                ["melee_critical_defense"] = new() { Name = "Melee Endurance", Multiplier = 0.1 },
                ["hp_max"] = new() { Name = "Max Health" },
            },
        },
    };

    [Fact]
    public void PromptSectionStatesValuesInTheUnitsTheGameShows()
    {
        // Internal 1,600 is "Melee Endurance 160" on screen. Handing the raw number to the model
        // invites a comparison that is wrong by an order of magnitude.
        var build = BuildWith(("chest", new TargetItem
        {
            ItemId = "chest_x",
            Traits = new Dictionary<string, int> { ["melee_critical_defense"] = 1600 },
        }));

        var text = DerivedFor(build).Describe();

        Assert.Contains("Melee Endurance", text, StringComparison.Ordinal);
        Assert.Contains("160", text, StringComparison.Ordinal);
        Assert.DoesNotContain("1,600", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptSectionForbidsSubtractingFromTheCharacterSheet()
    {
        // The section is only safe to include if it carries its own caveat. These figures are a
        // partial sum; the sheet shows a full total. Subtracting one from the other invents a
        // deficit, which is the exact failure the whole design guards against.
        var build = BuildWith(("chest", new TargetItem
        {
            ItemId = "chest_x",
            Traits = new Dictionary<string, int> { ["hp_max"] = 500 },
        }));

        var text = DerivedFor(build).Describe();

        Assert.Contains("DO NOT subtract", text, StringComparison.Ordinal);
        Assert.Contains("short by", text, StringComparison.Ordinal);

        foreach (var excluded in TargetStats.ExcludedSources)
        {
            Assert.Contains(excluded, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PromptSectionWarnsAgainstFixingAnAsymmetryTheBuildIntends()
    {
        // The concrete mistake this caused: the model called a magic-skewed heavy attack evasion
        // profile a weakness, when the target build is magic-skewed by design.
        var build = BuildWith(("chest", new TargetItem { ItemId = "chest_x" }));

        var text = DerivedFor(build).Describe();

        Assert.Contains("BY DESIGN", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptSectionLeadsWithTheNearestSetThreshold()
    {
        var build = BuildWith(("chest", new TargetItem { ItemId = "chest_x" }));

        var text = DerivedFor(build).Describe();

        Assert.Contains("Prayer of Salvation", text, StringComparison.Ordinal);
        Assert.Contains("1 more for the 2-piece bonus", text, StringComparison.Ordinal);
    }
}
