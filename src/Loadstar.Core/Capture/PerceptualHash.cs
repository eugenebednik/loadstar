using System.Numerics;

namespace Loadstar.Core.Capture;

/// <summary>
/// A 256-bit difference hash (dHash) of an image region.
///
/// <para>This is the deterministic half of icon identification. CLAUDE.md is explicit that asking a
/// vision model to name a 40px icon produces plausible wrong answers, and that a local index is
/// "deterministic, free, and offline — strictly better". This is that index's primitive.</para>
///
/// <para>dHash rather than average-hash because it compares <em>adjacent</em> pixels, so it keys on
/// gradient structure instead of overall brightness. Game icons sit on rarity-coloured backgrounds
/// that shift the average considerably while leaving the shape intact — precisely the case
/// average-hash gets wrong.</para>
///
/// <para><b>256 bits rather than the conventional 64.</b> A test comparing two distinctly-shaped
/// icons found them landing within the 64-bit "same image" tolerance, which would be fatal here: the
/// roster is ~38 bosses and most exist in near-identical normal and "Ascended" forms, so the hash
/// has to separate sprites that genuinely look alike. Quadrupling the sample grid costs nothing at
/// these image sizes and buys the discrimination that actually decides whether this feature works.</para>
/// </summary>
public static class PerceptualHash
{
    /// <summary>Sample grid. 17 wide gives 16 horizontal comparisons per row, over 16 rows.</summary>
    private const int SampleWidth = 17;

    private const int SampleHeight = 16;

    /// <summary>
    /// Hashes a region of an image.
    ///
    /// <para>Downsamples by area-averaging rather than nearest-neighbour. This is load-bearing, not
    /// tidiness: the index is built from the Content Settings window and matched against the
    /// schedule, which draws the same sprites <em>smaller</em>. A nearest sample is dominated by
    /// whichever pixels happen to land on the grid, so the two captures would not agree.</para>
    /// </summary>
    public static IconHash Compute(Bgra32Image image, PixelRect region)
    {
        ArgumentNullException.ThrowIfNull(image);

        var clipped = region.Intersect(image.Bounds);

        if (clipped.IsEmpty)
        {
            throw new ArgumentException($"Region {region} lies outside the {image.Width}x{image.Height} image.", nameof(region));
        }

        var samples = new double[SampleHeight, SampleWidth];

        for (var y = 0; y < SampleHeight; y++)
        {
            for (var x = 0; x < SampleWidth; x++)
            {
                var left = clipped.X + (int)((long)x * clipped.Width / SampleWidth);
                var right = clipped.X + (int)((long)(x + 1) * clipped.Width / SampleWidth);
                var top = clipped.Y + (int)((long)y * clipped.Height / SampleHeight);
                var bottom = clipped.Y + (int)((long)(y + 1) * clipped.Height / SampleHeight);

                samples[y, x] = AverageLuminance(
                    image,
                    left,
                    top,
                    Math.Max(right, left + 1),
                    Math.Max(bottom, top + 1));
            }
        }

        var words = new ulong[4];
        var bit = 0;

        for (var y = 0; y < SampleHeight; y++)
        {
            for (var x = 0; x < SampleWidth - 1; x++)
            {
                if (samples[y, x] > samples[y, x + 1])
                {
                    words[bit / 64] |= 1UL << (bit % 64);
                }

                bit++;
            }
        }

        return new IconHash(words[0], words[1], words[2], words[3]);
    }

    public static IconHash Compute(Bgra32Image image) => Compute(image, image.Bounds);

    private static double AverageLuminance(Bgra32Image image, int left, int top, int right, int bottom)
    {
        right = Math.Min(right, image.Width);
        bottom = Math.Min(bottom, image.Height);

        double total = 0;
        var count = 0;

        for (var y = top; y < bottom; y++)
        {
            var offset = y * image.Stride + left * Bgra32Image.BytesPerPixel;

            for (var x = left; x < right; x++)
            {
                // Rec. 601 luma. The weights matter for game icons, which are frequently
                // near-isoluminant in RGB but clearly distinct to the eye.
                total += 0.114 * image.Pixels[offset]
                    + 0.587 * image.Pixels[offset + 1]
                    + 0.299 * image.Pixels[offset + 2];

                offset += Bgra32Image.BytesPerPixel;
                count++;
            }
        }

        return count == 0 ? 0 : total / count;
    }
}

/// <summary>A 256-bit perceptual hash, stored as four words so it round-trips through JSON plainly.</summary>
public readonly record struct IconHash(ulong W0, ulong W1, ulong W2, ulong W3)
{
    public const int Bits = 256;

    /// <summary>Differing bits between two hashes. 0 is identical, 256 maximally different.</summary>
    public int DistanceTo(IconHash other) =>
        BitOperations.PopCount(W0 ^ other.W0)
        + BitOperations.PopCount(W1 ^ other.W1)
        + BitOperations.PopCount(W2 ^ other.W2)
        + BitOperations.PopCount(W3 ^ other.W3);

    public override string ToString() => $"{W0:x16}{W1:x16}{W2:x16}{W3:x16}";
}
