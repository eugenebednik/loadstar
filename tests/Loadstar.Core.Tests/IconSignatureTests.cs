using Loadstar.Core.Capture;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// The colour signature that identifies equipment across renderings.
///
/// <para>These tests pin the three properties the descriptor was chosen for, each of which was established
/// by measurement against a live character sheet rather than by preference: it survives rescaling, it
/// survives the lighting shift the game applies, and it separates items whose SHAPE is identical but whose
/// colour is not — which is the case a luminance hash cannot see at all, and the reason this replaced one.</para>
/// </summary>
public sealed class IconSignatureTests
{
    /// <summary>
    /// A deterministic test icon: a coloured blob on a tinted ground, with the hue driven by
    /// <paramref name="hueSeed"/> and the shape by <paramref name="shapeSeed"/>, so the two can be varied
    /// independently. Low-frequency on purpose — see IconIndexTests for why a striped fixture measures the
    /// fixture rather than the descriptor.
    /// </summary>
    private static Bgra32Image Icon(
        int shapeSeed,
        int hueSeed,
        int size = 64,
        double brightness = 1.0,
        double offset = 0.0,
        int padBytes = 4)
    {
        var stride = (size * Bgra32Image.BytesPerPixel) + padBytes;
        var pixels = new byte[stride * size];

        var centreX = 0.34 + (0.32 * ((shapeSeed * 7) % 10) / 10.0);
        var centreY = 0.30 + (0.36 * ((shapeSeed * 3) % 10) / 10.0);
        var radius = 0.24 + (0.14 * ((shapeSeed * 5) % 7) / 7.0);

        // Three well-separated hues, so "same shape, different colour" is a real difference rather than a
        // subtle one.
        (double R, double G, double B)[] palette =
        [
            (0.85, 0.25, 0.20),
            (0.20, 0.80, 0.35),
            (0.25, 0.35, 0.90),
            (0.85, 0.80, 0.20),
        ];

        // TWO hues per icon, one for the body and one for the ground, so the channels carry DIFFERENT
        // spatial patterns. A single tint over one luminance pattern is the degenerate case: per-channel
        // normalisation divides a uniform colour cast out entirely, so such a fixture measures nothing.
        // Real item art is never that — gold trim sits in different places from a green gem.
        var body = palette[hueSeed % palette.Length];
        var ground = palette[(hueSeed + 2) % palette.Length];

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var index = (y * stride) + (x * Bgra32Image.BytesPerPixel);

                var fx = (x + 0.5) / size;
                var fy = (y + 0.5) / size;
                var dx = fx - centreX;
                var dy = fy - centreY;
                var distance = Math.Sqrt((dx * dx) + (dy * dy));

                var inside = distance < radius;

                var level = distance < radius * 0.72 ? 0.35
                    : inside ? 0.95
                    : 0.55 - (0.2 * fy);

                var tint = inside ? body : ground;

                var r = 255 * level * tint.R;
                var g = 255 * level * tint.G;
                var b = 255 * level * tint.B;

                pixels[index] = Clamp((b * brightness) + offset);
                pixels[index + 1] = Clamp((g * brightness) + offset);
                pixels[index + 2] = Clamp((r * brightness) + offset);
                pixels[index + 3] = 255;
            }
        }

        return new Bgra32Image(pixels, size, size, stride);

        static byte Clamp(double v) => (byte)Math.Clamp(v, 0, 255);
    }

    [Fact]
    public void IdenticalImagesAreMaximallySimilar()
    {
        var signature = IconSignature.Compute(Icon(1, 0));

        // Three unit-length channels, so a perfect match is 3.0 rather than 1.0.
        Assert.Equal(3.0, signature.SimilarityTo(IconSignature.Compute(Icon(1, 0))), 5);
    }

    /// <summary>
    /// The index is built from published art a few hundred pixels wide and matched against a tile of about a
    /// hundred, so surviving a rescale is the whole job rather than a nice property.
    /// </summary>
    [Fact]
    public void RescalingTheSameIconBarelyMovesIt()
    {
        var large = IconSignature.Compute(Icon(3, 2, size: 200));
        var small = IconSignature.Compute(Icon(3, 2, size: 56));

        var same = large.SimilarityTo(small);
        var different = large.SimilarityTo(IconSignature.Compute(Icon(4, 1, size: 56)));

        Assert.True(same > 2.7, $"same icon at two sizes scored only {same:0.000}");
        Assert.True(same > different + 0.5, $"rescale drift {same:0.000} did not clear a different icon at {different:0.000}");
    }

    /// <summary>
    /// The game relights its tiles, so a brightness and contrast shift must not change the answer. This is
    /// what the per-channel centring and normalisation buy, and it is worth pinning because removing either
    /// step would still pass the tests above.
    /// </summary>
    [Fact]
    public void BrightnessAndContrastShiftsAreDividedOut()
    {
        var plain = IconSignature.Compute(Icon(5, 3));
        var relit = IconSignature.Compute(Icon(5, 3, brightness: 0.75, offset: 30));

        Assert.True(plain.SimilarityTo(relit) > 2.9,
            $"a relit copy scored {plain.SimilarityTo(relit):0.000}");
    }

    /// <summary>
    /// THE POINT OF THE WHOLE CLASS. A luminance hash cannot distinguish these at all: identical geometry,
    /// different colour. Equipment sets are frequently exactly this — the same silhouette in a different
    /// palette — and it is why a colour descriptor beat a difference hash on real tiles.
    /// </summary>
    [Fact]
    public void SameShapeInADifferentColourIsSeparated()
    {
        var red = IconSignature.Compute(Icon(6, 0));
        var green = IconSignature.Compute(Icon(6, 1));
        var blue = IconSignature.Compute(Icon(6, 2));

        Assert.True(red.SimilarityTo(green) < 2.5, $"red and green scored {red.SimilarityTo(green):0.000}");
        Assert.True(red.SimilarityTo(blue) < 2.5, $"red and blue scored {red.SimilarityTo(blue):0.000}");

        // And a luminance-only view is far less able to tell them apart, which is the comparison that
        // justifies the change. Not zero distance, because two hues at the same level do differ slightly in
        // Rec.601 luma — but far closer than the colour signature puts them.
        var lumaRed = PerceptualHash.Compute(Icon(6, 0));
        var lumaGreen = PerceptualHash.Compute(Icon(6, 1));

        Assert.True(lumaRed.DistanceTo(lumaGreen) < 40,
            $"the fixture no longer isolates colour: luma distance was {lumaRed.DistanceTo(lumaGreen)}");
    }

    /// <summary>
    /// THE PRICE OF THE LIGHTING INVARIANCE, pinned deliberately rather than discovered later.
    ///
    /// <para>Per-channel centring and normalisation divide out any purely MULTIPLICATIVE colour cast, so the
    /// same shape under a uniform tint is indistinguishable. That is not a defect to fix — it is the same
    /// arithmetic that makes the descriptor survive the game relighting its tiles, and the two cannot be
    /// separated. It is recorded here so nobody reads the test above and concludes colour is separated in
    /// every sense.</para>
    ///
    /// <para>It does not bite in practice because real item art varies colour SPATIALLY — gold trim in one
    /// place, a gem in another — rather than as one hue over one luminance pattern.</para>
    /// </summary>
    [Fact]
    public void AUniformColourCastIsInvisible()
    {
        var plain = IconSignature.Compute(UniformTint(1.0, 1.0, 1.0));
        var tinted = IconSignature.Compute(UniformTint(1.0, 0.55, 0.40));

        Assert.Equal(3.0, plain.SimilarityTo(tinted), 3);

        static Bgra32Image UniformTint(double r, double g, double b)
        {
            const int size = 48;
            var stride = size * Bgra32Image.BytesPerPixel;
            var pixels = new byte[stride * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var i = (y * stride) + (x * Bgra32Image.BytesPerPixel);
                    var level = x < size / 2 ? 90.0 : 200.0;

                    pixels[i] = (byte)(level * b);
                    pixels[i + 1] = (byte)(level * g);
                    pixels[i + 2] = (byte)(level * r);
                    pixels[i + 3] = 255;
                }
            }

            return new Bgra32Image(pixels, size, size, stride);
        }
    }

    [Fact]
    public void SignaturesRoundTripThroughTheirValues()
    {
        var original = IconSignature.Compute(Icon(7, 1));
        var restored = IconSignature.FromValues(original.Values);

        Assert.Equal(3.0, original.SimilarityTo(restored), 5);
        Assert.Equal(IconSignature.Grid * IconSignature.Grid * 3, original.Length);
    }

    [Fact]
    public void AnEmptyRegionIsRejectedRatherThanScoredAgainstNothing()
    {
        var image = Icon(1, 0);

        Assert.Throws<ArgumentException>(() =>
            IconSignature.Compute(image, new PixelRect(image.Width + 10, image.Height + 10, 4, 4)));
    }
}
