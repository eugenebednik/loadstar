using Loadstar.Games.ThroneAndLiberty;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// The stat arithmetic, anchored on the worked examples recorded in CLAUDE.md.
///
/// <para>Those examples are observations from a live client, so pinning them here means a future
/// change to the cost model has to explain itself against real data rather than against someone's
/// recollection of how the escalation works.</para>
/// </summary>
public sealed class StatPlannerTests
{
    /// <summary>The reference character: Str 40, Dex 80, Wis 96, Per 80, For 71.</summary>
    private static readonly StatObservation[] ReferenceCharacter =
    [
        new() { Stat = TlStat.Strength, Total = 40, Base = 16 },
        new() { Stat = TlStat.Dexterity, Total = 80, Base = 24 },
        new() { Stat = TlStat.Wisdom, Total = 96, Base = 30 },
        new() { Stat = TlStat.Perception, Total = 80, Base = 29 },
        new() { Stat = TlStat.Fortitude, Total = 71, Base = 10 },
    ];

    [Fact]
    public void ReferenceCharacterBasesSumToFiftyNineAllocatedPoints()
    {
        // CLAUDE.md: bases 16/24/30/29/10 give 59 allocated, which matches five of the six
        // target loadouts' totals. That agreement is what confirms base starts at 10.
        var allocated = ReferenceCharacter.Sum(o => o.Base!.Value - TlStats.StartingBase);

        Assert.Equal(59, allocated);
    }

    [Fact]
    public void PointsBelowTheEscalationBaseCostOneEach()
    {
        // Fortitude 71 -> 80 is nine points at 1x, because its base is only 10.
        Assert.Equal(9, StatPlanner.PointsToRaise(10, 19));
    }

    [Fact]
    public void PointsAtOrAboveTheEscalationBaseCostTwoEach()
    {
        // Wisdom 96 -> 100 is four stat levels but eight points, because its base already sits at 30.
        Assert.Equal(8, StatPlanner.PointsToRaise(30, 34));
    }

    [Fact]
    public void CostSpanningTheEscalationBoundaryChargesBothRates()
    {
        // 28 -> 32: two points at 1x, then two at 2x.
        Assert.Equal(6, StatPlanner.PointsToRaise(28, 32));
    }

    [Fact]
    public void DistanceToThresholdIsNotTheSameAsCost()
    {
        // The correction CLAUDE.md records: Wisdom looks four points from its tier and Fortitude
        // nine, yet they cost about the same. Ranking by distance gets this backwards.
        var wisdom = StatPlanner.PointsToRaise(30, 34);
        var fortitude = StatPlanner.PointsToRaise(10, 19);

        Assert.True(wisdom < fortitude + 2 && wisdom > fortitude - 2);
    }

    [Fact]
    public void TargetIsProjectedThroughThisCharactersGearNotTheAuthors()
    {
        // Strength 40 = base 16 + 24 from gear and Stellar Journey. A build asking for str 0 is
        // not asking for Strength 10 — it lands at 34 on this character.
        var plan = StatPlanner.Plan(ReferenceCharacter, new Dictionary<TlStat, int> { [TlStat.Strength] = 0 });

        var move = Assert.Single(plan.Changes);
        Assert.Equal(24, move.ExternalContribution);
        Assert.Equal(10, move.ProjectedBase);
        Assert.Equal(34, move.ProjectedTotal);
    }

    [Fact]
    public void MoveOutOfStrengthReportsTheBreakpointItGivesUp()
    {
        // The recorded failure this whole class exists to prevent: the recommendation was right,
        // but it was presented as a pure gain with the lost Strength 40 tier never mentioned.
        var plan = StatPlanner.Plan(ReferenceCharacter, new Dictionary<TlStat, int> { [TlStat.Strength] = 0 });

        var move = Assert.Single(plan.Changes);

        Assert.Equal([40], move.ThresholdsLost);
        Assert.Empty(move.ThresholdsGained);
        Assert.Contains("Damage Reduction 30", move.Describe());
        Assert.Contains("COSTS the 40 tier", move.Describe());
    }

    [Fact]
    public void DroppingAllocationRefundsRatherThanCosting()
    {
        var plan = StatPlanner.Plan(ReferenceCharacter, new Dictionary<TlStat, int> { [TlStat.Strength] = 0 });

        var move = Assert.Single(plan.Changes);

        Assert.Equal(-6, move.PointCost);
        Assert.Equal(6, plan.PointsRefunded);
        Assert.Equal(0, plan.PointsSpent);
        Assert.True(plan.IsSelfFunding);
    }

    [Fact]
    public void ThirtyTierIsRetainedWhenTheProjectedTotalStaysAboveIt()
    {
        // Strength lands at 34, so the 30 tier survives. Reporting it as lost would overstate
        // the price just as badly as omitting the 40 tier understated it.
        var plan = StatPlanner.Plan(ReferenceCharacter, new Dictionary<TlStat, int> { [TlStat.Strength] = 0 });

        Assert.DoesNotContain(30, Assert.Single(plan.Changes).ThresholdsLost);
    }

    [Fact]
    public void RedistributionThatPaysForItselfIsFlaggedAsSelfFunding()
    {
        // Six points out of Strength, six into Fortitude, whose base is far below the escalation.
        var plan = StatPlanner.Plan(ReferenceCharacter, new Dictionary<TlStat, int>
        {
            [TlStat.Strength] = 0,
            [TlStat.Fortitude] = 6,
        });

        Assert.Equal(6, plan.PointsSpent);
        Assert.Equal(6, plan.PointsRefunded);
        Assert.Equal(0, plan.NetPointCost);
        Assert.True(plan.IsSelfFunding);
    }

    [Fact]
    public void MovingIntoAnExpensiveStatDoesNotPayForItself()
    {
        // The same six points refunded from Strength buy only three levels of Wisdom, because
        // Wisdom's base is at the escalation threshold. This asymmetry is the reason cost has to
        // be computed from base rather than from the displayed value.
        var plan = StatPlanner.Plan(ReferenceCharacter, new Dictionary<TlStat, int>
        {
            [TlStat.Strength] = 0,
            [TlStat.Wisdom] = 26,
        });

        Assert.Equal(12, plan.PointsSpent);
        Assert.Equal(6, plan.PointsRefunded);
        Assert.Equal(6, plan.NetPointCost);
        Assert.False(plan.IsSelfFunding);
    }

    [Fact]
    public void GainingATierIsReportedWithItsEffect()
    {
        var plan = StatPlanner.Plan(ReferenceCharacter, new Dictionary<TlStat, int> { [TlStat.Fortitude] = 9 });

        var move = Assert.Single(plan.Changes);

        Assert.Equal(80, move.ProjectedTotal);
        Assert.Equal([80], move.ThresholdsGained);
        Assert.Contains("Endurance 60 · Heavy Attack Evasion 60", move.Describe());
    }

    [Theory]
    // 1x band: base 10 -> 30 is twenty points at one each.
    [InlineData(10, 30, 20)]
    // 2x band: base 30 -> 50 is twenty points at two each.
    [InlineData(30, 50, 40)]
    // 4x band: base 50 -> 60 is ten points at four each.
    [InlineData(50, 60, 40)]
    // Spanning all three.
    [InlineData(10, 60, 20 + 40 + 40)]
    public void PointCostsUseAllThreeBands(int fromBase, int toBase, int expected)
    {
        // The 4x band was long recorded as "reported but unverified" and was missing here, so a
        // heavily-invested stat was priced at half its real cost — making any recommendation that
        // touched one look cheaper than it is. The bands come from questlog's own allocation
        // transform: marginal value 1.00 / 0.50 / 0.25 at allocated 20 and 40, which is base 30
        // and base 50.
        Assert.Equal(expected, StatPlanner.PointsToRaise(fromBase, toBase));
    }

    [Fact]
    public void RaisingAcrossABandCostsMoreThanTheSameDistanceLower()
    {
        // The whole reason distance-to-threshold is the wrong ranking metric.
        var cheap = StatPlanner.PointsToRaise(10, 20);
        var dear = StatPlanner.PointsToRaise(50, 60);

        Assert.Equal(10, cheap);
        Assert.Equal(40, dear);
    }

    [Fact]
    public void MatchingSpreadProducesNoChanges()
    {
        var plan = StatPlanner.Plan(ReferenceCharacter, new Dictionary<TlStat, int>
        {
            [TlStat.Strength] = 6,
            [TlStat.Dexterity] = 14,
        });

        Assert.False(plan.HasChanges);
        Assert.Contains("already matches", plan.Describe());
    }

    [Fact]
    public void NothingPriceableIsNotReportedAsAMatch()
    {
        // Regression. When no stat carried a base, Moves came back empty, HasChanges was false, and
        // the summary read "already matches the target build" — asserting the spread was correct on
        // the strength of no evidence. It printed that for two incompatible target builds in a row
        // against the same character, which is what exposed it.
        var totalsOnly = TlStats.All
            .Select(stat => new StatObservation { Stat = stat, Total = 80 })
            .ToArray();

        var pve = StatPlanner.Plan(totalsOnly, new Dictionary<TlStat, int>
        {
            [TlStat.Fortitude] = 0,
            [TlStat.Dexterity] = 20,
            [TlStat.Wisdom] = 19,
        });

        var pvp = StatPlanner.Plan(totalsOnly, new Dictionary<TlStat, int>
        {
            [TlStat.Fortitude] = 24,
            [TlStat.Dexterity] = 0,
            [TlStat.Wisdom] = 16,
        });

        foreach (var plan in new[] { pve, pvp })
        {
            Assert.Empty(plan.Moves);
            Assert.DoesNotContain("already matches", plan.Describe());
            Assert.Contains("could not be", plan.Describe(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void StatWithoutAKnownBaseIsReportedUnpriceableRatherThanGuessed()
    {
        // The character sheet shows only the total; the Base/Equipment split needs a hover
        // tooltip. Inventing the split would produce a confident, wrong cost.
        var partial = new StatObservation[] { new() { Stat = TlStat.Wisdom, Total = 96 } };

        var plan = StatPlanner.Plan(partial, new Dictionary<TlStat, int> { [TlStat.Wisdom] = 26 });

        Assert.Empty(plan.Moves);
        var reason = Assert.Single(plan.Unpriceable);
        Assert.Contains("Base/Equipment split is unknown", reason);
        Assert.Contains("capture its tooltip", reason);
    }

    [Fact]
    public void QuestlogAttributeKeysMapIntToWisdomAndConToFortitude()
    {
        // The trap: read by eye, `int` looks like Intelligence and `con` like Constitution.
        // Getting these wrong silently swaps two stats in every recommendation.
        var mapped = TlStats.MapAllocated(new Dictionary<string, int>
        {
            ["str"] = 1,
            ["dex"] = 2,
            ["int"] = 3,
            ["per"] = 4,
            ["con"] = 5,
        });

        Assert.Equal(3, mapped[TlStat.Wisdom]);
        Assert.Equal(5, mapped[TlStat.Fortitude]);
        Assert.Equal(1, mapped[TlStat.Strength]);
        Assert.Equal(2, mapped[TlStat.Dexterity]);
        Assert.Equal(4, mapped[TlStat.Perception]);
    }

    [Fact]
    public void UnknownAttributeKeysAreIgnoredRatherThanFailingTheImport()
    {
        var mapped = TlStats.MapAllocated(new Dictionary<string, int> { ["str"] = 1, ["luck"] = 9 });

        Assert.Equal(1, Assert.Single(mapped).Value);
    }

    [Theory]
    [InlineData(29, 30)]
    [InlineData(80, 100)]
    [InlineData(96, 100)]
    public void NextThresholdFollowsTheSharedLadder(int total, int expected)
    {
        Assert.Equal(expected, StatPlanner.NextThreshold(total));
    }

    [Fact]
    public void NextThresholdIsNullPastTheTopOfTheLadder()
    {
        Assert.Null(StatPlanner.NextThreshold(120));
    }

    [Fact]
    public void PlanCarriesItsAssumptionsRatherThanPresentingThemAsFacts()
    {
        // The 4x escalation trigger and the refund rate are both unverified in CLAUDE.md, so the
        // plan has to say so wherever it is shown.
        var caveats = string.Join(" ", RedistributionPlan.Caveats);

        Assert.Contains("4x", caveats);
        Assert.Contains("Refunds are assumed", caveats);
    }
}
