using Loadstar.Core.Capture;

using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// The acceptance rule for matching questlog's published art against a screen capture.
///
/// <para>Separate from <see cref="IconIndex.Match"/> because it is a different problem. Match compares two
/// images the GAME drew, differing only in size, where 20 bits of drift is the whole story. Comparing a
/// published asset against a captured tile means comparing two renderings with different framing, so the
/// absolute distance is several times larger while the ranking stays right — measured on a verified tile,
/// the correct item ranked 1st of 1,773 at 71 bits, an answer the absolute rule discarded.</para>
/// </summary>
public class CrossRenderingMatchTests
{
    private static IconHash Hash(ulong seed) => new(seed, seed * 3, seed * 5, seed * 7);

    /// <summary>
    /// The case that was being thrown away: a clear winner far outside the same-rendering tolerance.
    /// Numbers are the measured ones — 71 bits for the winner, 100 for the runner-up.
    /// </summary>
    [Fact]
    public void AClearWinnerIsAcceptedWellBeyondTheSameRenderingTolerance()
    {
        var query = new IconHash(0, 0, 0, 0);
        var index = new IconIndex();

        index.Add("Sacred Tree Resurrection Ring", WithBitsSet(71));
        index.Add("Glade Stalker Trousers", WithBitsSet(100));

        var match = index.MatchAcrossRenderings(query);

        Assert.Equal("Sacred Tree Resurrection Ring", match?.Name);
        Assert.True(71 > IconIndex.DefaultTolerance, "the point is that this exceeds the strict tolerance");
    }

    /// <summary>
    /// The protection that replaces the tolerance. Every tile that held an unmatched item showed margins of
    /// 0-4 bits with tied nearest neighbours, so a near-tie has to resolve to nothing.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(IconIndex.CrossRenderingMargin - 1)]
    public void ANearTieResolvesToNothing(int gap)
    {
        var index = new IconIndex();

        index.Add("First", WithBitsSet(80));
        index.Add("Second", WithBitsSet(80 + gap));

        Assert.Null(index.MatchAcrossRenderings(new IconHash(0, 0, 0, 0)));
    }

    /// <summary>Chance-level answers are still rejected: a 256-bit hash averages 128 bits apart at random.</summary>
    [Fact]
    public void AChanceLevelBestIsRejectedHoweverBigTheMargin()
    {
        var index = new IconIndex();

        index.Add("Nothing Like It", WithBitsSet(200));

        Assert.Null(index.MatchAcrossRenderings(new IconHash(0, 0, 0, 0)));
    }

    /// <summary>
    /// The catalogue lists some items more than once and several share an icon, so an entry with the SAME
    /// name is not a rival — treating it as one would reject every duplicated item.
    /// </summary>
    [Fact]
    public void ADuplicateOfTheWinnerIsNotARival()
    {
        var index = new IconIndex();

        index.Add("Startree Stollen", WithBitsSet(60));
        index.Add("Startree Stollen", WithBitsSet(60));
        index.Add("Something Else", WithBitsSet(95));

        Assert.Equal("Startree Stollen", index.MatchAcrossRenderings(new IconHash(0, 0, 0, 0))?.Name);
    }

    /// <summary>But two DIFFERENT items that are equally close is genuine ambiguity, and stays null.</summary>
    [Fact]
    public void TwoDifferentItemsEquallyCloseStayAmbiguous()
    {
        var index = new IconIndex();

        index.Add("Sunshade Boots", WithBitsSet(70));
        index.Add("Bound Sunshade Boots", WithBitsSet(70));

        Assert.Null(index.MatchAcrossRenderings(new IconHash(0, 0, 0, 0)));
    }

    [Fact]
    public void AnEmptyIndexMatchesNothing() =>
        Assert.Null(new IconIndex().MatchAcrossRenderings(new IconHash(1, 2, 3, 4)));

    /// <summary>A hash whose distance from all-zero is exactly the requested number of bits.</summary>
    private static IconHash WithBitsSet(int bits)
    {
        var words = new ulong[4];

        for (var i = 0; i < bits; i++)
        {
            words[i / 64] |= 1UL << (i % 64);
        }

        return new IconHash(words[0], words[1], words[2], words[3]);
    }
}
