using Loadstar.Games.ThroneAndLiberty;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// The Redfrost-style purify chain: drop frozen, purify for either the item or cinders, craft from
/// cinders.
///
/// <para>The property worth protecting is that the cinder path <b>bounds</b> the purify stage.
/// Modelling this as an ordinary random drop would tell a player their bad luck could continue
/// indefinitely, when in fact it cannot.</para>
/// </summary>
public sealed class PurifyChainEstimatorTests
{
    [Fact]
    public void CinderThresholdSetsAHardCeilingOnPurifies()
    {
        // 20 cinders needed, 2 per failed purify: ten purifies fund the craft no matter what.
        var estimate = PurifyChainEstimator.Estimate(0.05, purifyItemChance: 0.10, cindersPerPurify: 2, cindersToCraft: 20)!;

        Assert.Equal(10, estimate.MaxPurifies);
    }

    [Fact]
    public void ExpectedPurifiesNeverExceedsTheCeiling()
    {
        var estimate = PurifyChainEstimator.Estimate(0.05, 0.10, 2, 20)!;

        Assert.True(estimate.ExpectedPurifies <= estimate.MaxPurifies);
        Assert.True(estimate.ExpectedPurifies > 0);
    }

    [Fact]
    public void AHopelessPurifyRollStillTerminatesViaCrafting()
    {
        // Zero chance of the item from purifying: the craft path is the only route, and it is
        // reached in exactly the ceiling number of purifies. An ordinary drop model would say
        // "never" here, which is the failure this class exists to avoid.
        var estimate = PurifyChainEstimator.Estimate(0.05, purifyItemChance: 0.0, cindersPerPurify: 1, cindersToCraft: 8)!;

        Assert.Equal(8, estimate.MaxPurifies);
        Assert.Equal(8, estimate.ExpectedPurifies);
        Assert.Equal(0, estimate.ChanceOfItemBeforeCrafting);
        Assert.Equal(160, estimate.ExpectedKills); // 8 purifies / 0.05 drop chance
    }

    [Fact]
    public void GuaranteedPurifyMakesTheChainCollapseToOneFrozenDrop()
    {
        var estimate = PurifyChainEstimator.Estimate(0.25, purifyItemChance: 1.0, cindersPerPurify: 1, cindersToCraft: 5)!;

        Assert.Equal(1, estimate.ExpectedPurifies);
        Assert.Equal(4, estimate.ExpectedKills); // 1 / 0.25
    }

    [Fact]
    public void BothStagesAreCountedInTheKillEstimate()
    {
        // The trap: quoting only the frozen drop rate. The second roll means more kills than the
        // drop rate alone implies.
        var estimate = PurifyChainEstimator.Estimate(0.10, purifyItemChance: 0.25, cindersPerPurify: 1, cindersToCraft: 10)!;

        var killsIfFrozenWereTheItem = 10; // 1 / 0.10

        Assert.True(estimate.ExpectedKills > killsIfFrozenWereTheItem);
    }

    [Fact]
    public void DescriptionCreditsTheCraftPathWithoutOverclaimingTheDropStage()
    {
        var description = PurifyChainEstimator.Estimate(0.05, 0.10, 2, 20)!.Describe();

        Assert.Contains("guarantee it even with no lucky roll", description);
        Assert.Contains("drop stage stays random", description);
    }

    [Theory]
    [InlineData(0, 0.1, 1, 5)]
    [InlineData(0.1, 1.5, 1, 5)]
    [InlineData(0.1, 0.1, 0, 5)]
    [InlineData(0.1, 0.1, 1, 0)]
    public void InvalidInputsYieldNoEstimate(double drop, double purify, int perPurify, int toCraft)
    {
        Assert.Null(PurifyChainEstimator.Estimate(drop, purify, perPurify, toCraft));
    }
}
