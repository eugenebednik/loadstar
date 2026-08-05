using Loadstar.Core.Capture;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Icon identification. The property that matters most is the <b>refusal</b>: with ~38 bosses that
/// mostly exist in near-identical normal and "Ascended" forms, guessing between two close matches
/// would produce exactly the confident-wrong naming this approach exists to avoid.
/// </summary>
public sealed class IconIndexTests
{
    /// <summary>
    /// Draws a deterministic test icon: an off-centre blob on a tinted background.
    ///
    /// <para>Deliberately <b>low-frequency</b>, because that is what a game icon is — a creature
    /// silhouette, not fine texture. An earlier version of this fixture drew high-frequency diagonal
    /// stripes and failed the rescale test, which was the fixture's fault rather than the hash's:
    /// stripes alias under downsampling, so the 64px and 32px renders genuinely differ. Any
    /// perceptual hash fails that, and no real icon looks like it.</para>
    /// </summary>
    private static Bgra32Image Icon(int seed, int size = 40, int padBytes = 4)
    {
        var stride = size * Bgra32Image.BytesPerPixel + padBytes;
        var pixels = new byte[stride * size];

        // All geometry in fractions of the image, so the shape itself is scale-independent and any
        // drift across sizes comes from resampling rather than from drawing something different.
        var centreX = 0.34 + 0.32 * ((seed * 7) % 10) / 10.0;
        var centreY = 0.30 + 0.36 * ((seed * 3) % 10) / 10.0;
        var radius = 0.24 + 0.14 * ((seed * 5) % 7) / 7.0;
        var notchX = 0.30 + 0.40 * ((seed * 11) % 9) / 9.0;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var offset = y * stride + x * Bgra32Image.BytesPerPixel;

                var fx = (x + 0.5) / size;
                var fy = (y + 0.5) / size;
                var dx = fx - centreX;
                var dy = fy - centreY;
                var distance = Math.Sqrt(dx * dx + dy * dy);

                // Body, a lighter rim, and an off-centre bar. Three tones at mid frequency is a
                // fair stand-in for a creature sprite: enough structure for a gradient hash to key
                // on, without the fine texture that aliases when downsampled.
                double luma;

                if (fy > 0.62 && Math.Abs(fx - notchX) < 0.10)
                {
                    luma = 120;
                }
                else if (distance < radius * 0.72)
                {
                    luma = 35;
                }
                else if (distance < radius)
                {
                    luma = 165;
                }
                else
                {
                    luma = 205 - 40 * fy;
                }

                pixels[offset] = (byte)luma;
                pixels[offset + 1] = (byte)luma;
                pixels[offset + 2] = (byte)luma;
                pixels[offset + 3] = 255;
            }
        }

        return new Bgra32Image(pixels, size, size, stride);
    }

    [Fact]
    public void IdenticalImagesHashIdentically()
    {
        Assert.Equal(PerceptualHash.Compute(Icon(1)), PerceptualHash.Compute(Icon(1)));
    }

    [Fact]
    public void DifferentIconsHashApart()
    {
        var distance = PerceptualHash.Compute(Icon(1)).DistanceTo(PerceptualHash.Compute(Icon(5)));

        Assert.True(distance > IconIndex.DefaultTolerance, $"expected a clear difference, got {distance} bits");
    }

    [Fact]
    public void SameIconAtADifferentScaleStillMatches()
    {
        // The schedule renders icons smaller than Content Settings does, so the index is useless
        // unless a rescaled capture of the same sprite still resolves.
        var large = PerceptualHash.Compute(Icon(3, size: 64));
        var small = PerceptualHash.Compute(Icon(3, size: 32));

        Assert.True(
            large.DistanceTo(small) <= IconIndex.DefaultTolerance,
            $"rescaled icon drifted {large.DistanceTo(small)} bits");
    }

    [Fact]
    public void StrideIsRespectedWhenHashing()
    {
        // Padded GPU rows would otherwise shear the sample grid and change the hash.
        Assert.Equal(
            PerceptualHash.Compute(Icon(2, padBytes: 0)),
            PerceptualHash.Compute(Icon(2, padBytes: 28)));
    }

    [Fact]
    public void KnownIconResolvesToItsName()
    {
        var index = new IconIndex();
        index.Add("Ascended Morokai", PerceptualHash.Compute(Icon(1)), "Boss");
        index.Add("Ramux", PerceptualHash.Compute(Icon(9)), "Archboss");

        var match = index.Match(PerceptualHash.Compute(Icon(1)));

        Assert.NotNull(match);
        Assert.Equal("Ascended Morokai", match.Name);
        Assert.Equal("Boss", match.Category);
        Assert.True(match.IsStrong);
    }

    [Fact]
    public void UnknownIconReturnsNullRatherThanTheNearestGuess()
    {
        var index = new IconIndex();
        index.Add("Adentus", PerceptualHash.Compute(Icon(1)));

        Assert.Null(index.Match(PerceptualHash.Compute(Icon(7))));
    }

    [Fact]
    public void AmbiguousMatchIsRefused()
    {
        // Two entries sharing a hash is the Ascended-variant scenario. Returning either name would
        // be a coin flip presented as fact.
        var hash = PerceptualHash.Compute(Icon(4));

        var index = new IconIndex();
        index.Add("Morokai", hash);
        index.Add("Ascended Morokai", hash);

        Assert.Null(index.Match(hash));
    }

    [Fact]
    public void CollisionsAreDiscoverableAtBuildTime()
    {
        var hash = PerceptualHash.Compute(Icon(4));

        var index = new IconIndex();
        index.Add("Talus", hash);
        index.Add("Ascended Talus", hash);
        index.Add("Chernobog", PerceptualHash.Compute(Icon(8)));

        var collisions = index.FindCollisions();

        var collision = Assert.Single(collisions);
        Assert.Equal(0, collision.Distance);
        Assert.Contains("Talus", collision.First);
    }

    [Fact]
    public void EmptyIndexMatchesNothingInsteadOfThrowing()
    {
        Assert.Null(new IconIndex().Match(PerceptualHash.Compute(Icon(1))));
    }

    [Fact]
    public void IndexRoundTripsThroughJson()
    {
        var original = new IconIndex { GameVersion = "1.443.22.7936", BuiltAt = DateTimeOffset.UnixEpoch };
        original.Add("Ramux", PerceptualHash.Compute(Icon(2)), "Archboss");
        original.Add("Pakilo Naru", PerceptualHash.Compute(Icon(6)), "Boss");

        var restored = IconIndex.FromJson(original.ToJson());

        Assert.Equal(2, restored.Count);
        Assert.Equal("1.443.22.7936", restored.GameVersion);

        var match = restored.Match(PerceptualHash.Compute(Icon(2)));
        Assert.Equal("Ramux", match?.Name);
        Assert.Equal("Archboss", match?.Category);
    }

    [Fact]
    public void RescaleDriftStaysBelowTheGapToDifferentIcons()
    {
        // The two bands this whole scheme depends on, pinned so a change to the hash cannot
        // silently close the gap between them. Measured across seeds 1-9: rescaling one sprite
        // 64px -> 32px moves it at most ~14 bits, while different sprites sit at least ~23 apart.
        var worstDrift = 0;
        var closestPair = int.MaxValue;

        for (var seed = 1; seed <= 9; seed++)
        {
            worstDrift = Math.Max(
                worstDrift,
                PerceptualHash.Compute(Icon(seed, 64)).DistanceTo(PerceptualHash.Compute(Icon(seed, 32))));

            for (var other = seed + 1; other <= 9; other++)
            {
                closestPair = Math.Min(
                    closestPair,
                    PerceptualHash.Compute(Icon(seed)).DistanceTo(PerceptualHash.Compute(Icon(other))));
            }
        }

        Assert.True(worstDrift <= IconIndex.DefaultTolerance,
            $"same icon rescaled drifted {worstDrift} bits, past the {IconIndex.DefaultTolerance} tolerance");

        Assert.True(closestPair > IconIndex.DefaultTolerance,
            $"two different icons sat {closestPair} bits apart, inside the {IconIndex.DefaultTolerance} tolerance");
    }

    [Fact]
    public void RegionOutsideTheImageIsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => PerceptualHash.Compute(Icon(1), new PixelRect(500, 500, 10, 10)));
    }
}
