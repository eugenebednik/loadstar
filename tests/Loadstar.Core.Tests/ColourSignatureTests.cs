using Loadstar.Core.Capture;

using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// The colour signature, which was measured and rejected for icon matching — see its own remarks. Tested
/// because the probe still reports it, and a measurement tool that is itself wrong proves nothing.
/// </summary>
public class ColourSignatureTests
{
    private static Bgra32Image Image(int size, params (PixelRect Rect, byte B, byte G, byte R, byte A)[] parts)
    {
        var stride = size * Bgra32Image.BytesPerPixel;
        var image = new Bgra32Image(new byte[stride * size], size, size, stride);

        foreach (var (rect, b, g, r, a) in parts)
        {
            image.Fill(rect, b, g, r, a);
        }

        return image;
    }

    [Fact]
    public void WeightsAlwaysSumToTheFixedTotal()
    {
        var image = Image(32, (new PixelRect(0, 0, 32, 32), 10, 200, 90, 255));
        var signature = ColourSignature.FromAlpha(image, image.Bounds);

        Assert.Equal(ColourSignature.Total, signature.Weights.Sum());
    }

    /// <summary>
    /// The property the whole approach rested on: the same palette at a different SIZE is the same
    /// signature. It holds — which is why the failure had to be diagnosed by ranking rather than assumed.
    /// </summary>
    [Fact]
    public void ScaleDoesNotChangeTheSignature()
    {
        var small = Image(16, (new PixelRect(0, 0, 16, 16), 30, 60, 200, 255));
        var large = Image(64, (new PixelRect(0, 0, 64, 64), 30, 60, 200, 255));

        Assert.Equal(0, ColourSignature.FromAlpha(small, small.Bounds)
            .DistanceTo(ColourSignature.FromAlpha(large, large.Bounds)));
    }

    /// <summary>Different palettes must be far apart, or it discriminates nothing.</summary>
    [Fact]
    public void DifferentPalettesAreFarApart()
    {
        var white = Image(32, (new PixelRect(0, 0, 32, 32), 245, 245, 245, 255));
        var navy = Image(32, (new PixelRect(0, 0, 32, 32), 90, 40, 25, 255));

        Assert.True(
            ColourSignature.FromAlpha(white, white.Bounds)
                .DistanceTo(ColourSignature.FromAlpha(navy, navy.Bounds)) > ColourSignature.Total,
            "white and navy should be nearly disjoint");
    }

    /// <summary>
    /// Soft binning, stated as the relative claim it actually supports.
    ///
    /// <para>With hard 4-level boundaries at 64/128/192, greys of 126 and 130 land in different bins and
    /// share NOTHING — a maximal distance from a 4-unit difference. Soft binning makes the cost
    /// proportional instead. It does not make it negligible: at four levels a 4-unit shift near a boundary
    /// still moves about a quarter of the weight (measured ~2,500 of 20,000), which is a fair part of why
    /// this signature underperformed. The meaningful assertion is therefore comparative — a boundary
    /// straddle must cost far less than a genuinely different tone — and an earlier absolute threshold of
    /// Total/10 was simply wrong about the arithmetic.</para>
    /// </summary>
    [Fact]
    public void ValuesEitherSideOfABoundaryCostFarLessThanADifferentTone()
    {
        var below = Image(32, (new PixelRect(0, 0, 32, 32), 126, 126, 126, 255));
        var above = Image(32, (new PixelRect(0, 0, 32, 32), 130, 130, 130, 255));
        var lighter = Image(32, (new PixelRect(0, 0, 32, 32), 210, 210, 210, 255));

        var straddle = ColourSignature.FromAlpha(below, below.Bounds)
            .DistanceTo(ColourSignature.FromAlpha(above, above.Bounds));

        var different = ColourSignature.FromAlpha(below, below.Bounds)
            .DistanceTo(ColourSignature.FromAlpha(lighter, lighter.Bounds));

        Assert.True(
            straddle < different / 3,
            $"a 4-unit straddle cost {straddle} against {different} for a real difference");
    }

    /// <summary>Transparent pixels are not artwork, so an empty tile samples nothing and says so.</summary>
    [Fact]
    public void AFullyTransparentTileIsEmpty()
    {
        var image = Image(32, (new PixelRect(0, 0, 32, 32), 0, 0, 0, 0));
        var signature = ColourSignature.FromAlpha(image, image.Bounds);

        Assert.True(signature.IsEmpty);
        Assert.Equal(0, signature.SampleCount);
    }

    /// <summary>
    /// The background is masked out, so the rarity disc behind an item does not enter its palette. This is
    /// the part that made the approach worth testing at all.
    /// </summary>
    [Fact]
    public void TheTileBackgroundIsMaskedOut()
    {
        var onPurple = Image(
            40,
            (new PixelRect(0, 0, 40, 40), 0x8E, 0x3F, 0x86, 255),
            (new PixelRect(14, 14, 12, 12), 245, 245, 245, 255));

        var onOrange = Image(
            40,
            (new PixelRect(0, 0, 40, 40), 0x20, 0x80, 0xE0, 255),
            (new PixelRect(14, 14, 12, 12), 245, 245, 245, 255));

        var distance = ColourSignature.FromBackdrop(onPurple, onPurple.Bounds)
            .DistanceTo(ColourSignature.FromBackdrop(onOrange, onOrange.Bounds));

        Assert.True(distance < ColourSignature.Total / 5, $"backdrop leaked: {distance}");
    }
}
