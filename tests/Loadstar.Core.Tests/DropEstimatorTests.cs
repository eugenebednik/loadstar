using Loadstar.Games.ThroneAndLiberty;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Drop-rate arithmetic, anchored on the live sample from <c>database.getItem</c>.
///
/// <para>The distinction these tests protect is between the <em>average</em> number of kills and
/// the number that reaches a given confidence. They are not the same number, they differ by a lot
/// at low probabilities, and quoting one as the other is how a player ends up 400 kills deep
/// wondering why the tool lied to them.</para>
/// </summary>
public sealed class DropEstimatorTests
{
    /// <summary>Observed on `bracelet_aa_t3_normal_001` from the Wraith Controller — about 0.75%.</summary>
    private const double ObservedRate = 0.00751705;

    [Fact]
    public void ExpectedKillsIsTheReciprocalOfTheRate()
    {
        var estimate = DropEstimator.Estimate(ObservedRate)!;

        // 1 / 0.00751705 = 133.03..., so 134 kills on average.
        Assert.Equal(134, estimate.ExpectedKills);
    }

    [Fact]
    public void MedianIsLowerThanTheMeanForARareDrop()
    {
        // The distribution is skewed: half of players see it well before the average. Reporting
        // only the mean makes the grind look longer than it usually is; reporting only the median
        // makes it look shorter. Both get shown.
        var estimate = DropEstimator.Estimate(ObservedRate)!;

        Assert.True(estimate.KillsFor50Percent < estimate.ExpectedKills);
        Assert.Equal(92, estimate.KillsFor50Percent);
    }

    [Fact]
    public void HigherConfidenceCostsDisproportionatelyMoreKills()
    {
        var estimate = DropEstimator.Estimate(ObservedRate)!;

        Assert.Equal(306, estimate.KillsFor90Percent);
        Assert.Equal(611, estimate.KillsFor99Percent);

        // Going from 90% to 99% costs roughly as many kills again as reaching 90% did. That
        // non-linearity is the useful part of the advice.
        Assert.True(estimate.KillsFor99Percent > estimate.KillsFor90Percent * 1.5);
    }

    [Fact]
    public void DescriptionRefusesToPromiseAGuarantee()
    {
        // The player asked for "how many until guaranteed". The honest answer names confidence
        // levels and says outright that none of them is certainty.
        var description = DropEstimator.Estimate(ObservedRate, "Wraith Controller")!.Describe();

        Assert.Contains("No number guarantees it", description);
        Assert.Contains("independent roll", description);
        Assert.Contains("Wraith Controller", description);
    }

    [Fact]
    public void DeterministicDropIsReportedAsActuallyGuaranteed()
    {
        var estimate = DropEstimator.Estimate(1.0)!;

        Assert.True(estimate.IsGuaranteed);
        Assert.Equal(1, estimate.ExpectedKills);
        Assert.Contains("guaranteed", estimate.Describe());
    }

    [Fact]
    public void RareRateIsNotRoundedAwayToZero()
    {
        // 0.75% shown as "0.0%" would make a 300-kill grind look free.
        var estimate = DropEstimator.Estimate(ObservedRate)!;

        Assert.Equal("0.752%", estimate.FormatPercentage());
        Assert.DoesNotContain("0.0%", estimate.FormatPercentage());
    }

    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(0.25, 3)]
    [InlineData(0.1, 7)]
    public void FiftyPercentConfidenceMatchesTheClosedForm(double probability, int expected)
    {
        Assert.Equal(expected, DropEstimator.KillsForConfidence(probability, 0.50));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void ImpossibleRatesYieldNoEstimateRatherThanNonsense(double probability)
    {
        Assert.Null(DropEstimator.Estimate(probability));
    }
}
