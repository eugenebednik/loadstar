namespace Loadstar.Core.Capture;

/// <summary>
/// A colour histogram of an icon's artwork.
///
/// <para><b>MEASURED AND REJECTED for matching a captured tile against questlog's art.</b> The reasoning
/// that produced it was sound and wrong, so it is recorded here rather than deleted: the perceptual hash was
/// failing across the two renderings, questlog serves art with padding inside a 200x200 frame while the game
/// fills a ~91px disc, a gradient hash keys on exactly that disagreement, and an item's palette survives it.
/// White trousers against navy trousers — the pair that started this — is trivially separable by colour and
/// marginal by gradient.</para>
///
/// <para><b>The measurement said otherwise, on the one tile whose identity was independently verified.</b>
/// Against the full 1,773-item catalogue the correct item ranked <b>1st under the perceptual hash</b> and
/// <b>154th under this signature</b>. The hash was never the problem — its ACCEPTANCE RULE was, an absolute
/// 20-bit tolerance calibrated on same-rendering comparisons. So the fix belonged in the threshold, not in a
/// new metric, and the intuition about palettes simply did not survive contact with the data.</para>
///
/// <para>Kept because <c>--icon-probe</c> still reports it, which is what makes that negative result
/// reproducible instead of a claim in a commit message. Do not wire it into identification without
/// re-measuring.</para>
///
/// <para>The construction is sound on its own terms if it is ever wanted for something else: background is
/// masked rather than cropped, weights are normalised so tiles of different pixel counts compare directly,
/// and binning is soft — see <see cref="Accumulate"/>, where hard 4-level boundaries would have been the
/// obvious mistake.</para>
/// </summary>
public sealed class ColourSignature
{
    /// <summary>Levels per channel. 4 gives 64 bins — coarse on purpose; see <see cref="Accumulate"/>.</summary>
    public const int LevelsPerChannel = 4;

    public const int Bins = LevelsPerChannel * LevelsPerChannel * LevelsPerChannel;

    /// <summary>Fixed-point scale. Bins sum to this, so two signatures are comparable without floats.</summary>
    public const int Total = 10_000;

    private readonly int[] _bins;

    private ColourSignature(int[] bins, int sampled)
    {
        _bins = bins;
        SampleCount = sampled;
    }

    /// <summary>Bin weights, summing to <see cref="Total"/> unless nothing was sampled.</summary>
    public IReadOnlyList<int> Weights => _bins;

    /// <summary>
    /// How many pixels went into it.
    ///
    /// <para>Worth exposing: a tile that is entirely background samples almost nothing, and a signature
    /// built from a handful of pixels will happily sit close to something by accident. Callers reject on
    /// this rather than trusting a confident-looking distance.</para>
    /// </summary>
    public int SampleCount { get; }

    public bool IsEmpty => SampleCount == 0;

    /// <summary>
    /// Builds a signature from pixels that are meaningfully opaque — the questlog side, which has real alpha.
    /// </summary>
    public static ColourSignature FromAlpha(Bgra32Image image, PixelRect region, byte minimumAlpha = 24)
    {
        ArgumentNullException.ThrowIfNull(image);

        return Build(image, region, (b, g, r, a) => a >= minimumAlpha);
    }

    /// <summary>
    /// Builds a signature from pixels that are not the tile background — the capture side, where the art is
    /// already composited over a rarity-coloured disc.
    /// </summary>
    /// <param name="tolerance">
    /// How far from the sampled backdrop still counts as background. Generous on purpose: letting a little
    /// disc through dilutes the histogram slightly, while masking too aggressively removes artwork, and
    /// dilution is much the cheaper error.
    /// </param>
    public static ColourSignature FromBackdrop(Bgra32Image image, PixelRect region, int tolerance = 52)
    {
        ArgumentNullException.ThrowIfNull(image);

        var clipped = region.Intersect(image.Bounds);

        if (clipped.IsEmpty)
        {
            return new ColourSignature(new int[Bins], 0);
        }

        var (backB, backG, backR) = ArtworkBounds.EstimateBackdrop(image, clipped);

        return Build(
            image,
            clipped,
            (b, g, r, _) =>
                Math.Abs(b - backB) > tolerance
                || Math.Abs(g - backG) > tolerance
                || Math.Abs(r - backR) > tolerance);
    }

    /// <summary>
    /// Total absolute difference between two signatures: 0 identical, <see cref="Total"/> * 2 disjoint.
    ///
    /// <para>L1 rather than chi-square or Bhattacharyya. All three rank the same way in practice here, and
    /// L1 is exact in integers — which keeps a stored signature comparing bit-identically to a recomputed
    /// one, so a cached index cannot drift from a fresh one.</para>
    /// </summary>
    public int DistanceTo(ColourSignature other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var total = 0;

        for (var i = 0; i < Bins; i++)
        {
            total += Math.Abs(_bins[i] - other._bins[i]);
        }

        return total;
    }

    private static ColourSignature Build(
        Bgra32Image image,
        PixelRect region,
        Func<byte, byte, byte, byte, bool> include)
    {
        var clipped = region.Intersect(image.Bounds);
        var bins = new double[Bins];
        var sampled = 0;

        for (var y = clipped.Y; y < clipped.Bottom; y++)
        {
            var offset = (y * image.Stride) + (clipped.X * Bgra32Image.BytesPerPixel);

            for (var x = clipped.X; x < clipped.Right; x++)
            {
                var b = image.Pixels[offset];
                var g = image.Pixels[offset + 1];
                var r = image.Pixels[offset + 2];
                var a = image.Pixels[offset + 3];

                offset += Bgra32Image.BytesPerPixel;

                if (!include(b, g, r, a))
                {
                    continue;
                }

                Accumulate(bins, b, g, r);
                sampled++;
            }
        }

        if (sampled == 0)
        {
            return new ColourSignature(new int[Bins], 0);
        }

        // Normalised so tiles of different pixel counts compare directly — which is the entire reason this
        // survives the scale difference between a 200px asset and a 91px disc.
        var scaled = new int[Bins];
        var sum = 0.0;

        foreach (var weight in bins)
        {
            sum += weight;
        }

        var running = 0;

        for (var i = 0; i < Bins; i++)
        {
            scaled[i] = (int)Math.Round(bins[i] / sum * Total);
            running += scaled[i];
        }

        // Rounding leaves a few units unaccounted for; parking them in the heaviest bin keeps the invariant
        // that weights sum to Total, so a distance is always on the same scale.
        if (running != Total)
        {
            var heaviest = 0;

            for (var i = 1; i < Bins; i++)
            {
                if (scaled[i] > scaled[heaviest])
                {
                    heaviest = i;
                }
            }

            scaled[heaviest] += Total - running;
        }

        return new ColourSignature(scaled, sampled);
    }

    /// <summary>
    /// Adds one pixel, spread across neighbouring bins by trilinear weight.
    ///
    /// <para><b>Soft binning, not nearest bin, and this is the difference between working and not.</b> At
    /// four levels per channel the hard boundaries sit at 64, 128 and 192, so a channel that reads 126 in
    /// one rendering and 130 in the other lands in a different bin and contributes nothing in common. WebP
    /// compression and the game's own tinting move values by exactly that much. Splitting each pixel between
    /// the two bins it sits between makes the histogram continuous, so a small shift moves a little weight
    /// instead of all of it.</para>
    /// </summary>
    private static void Accumulate(double[] bins, byte b, byte g, byte r)
    {
        var (b0, b1, bw) = Split(b);
        var (g0, g1, gw) = Split(g);
        var (r0, r1, rw) = Split(r);

        Add(bins, r0, g0, b0, (1 - rw) * (1 - gw) * (1 - bw));
        Add(bins, r1, g0, b0, rw * (1 - gw) * (1 - bw));
        Add(bins, r0, g1, b0, (1 - rw) * gw * (1 - bw));
        Add(bins, r1, g1, b0, rw * gw * (1 - bw));
        Add(bins, r0, g0, b1, (1 - rw) * (1 - gw) * bw);
        Add(bins, r1, g0, b1, rw * (1 - gw) * bw);
        Add(bins, r0, g1, b1, (1 - rw) * gw * bw);
        Add(bins, r1, g1, b1, rw * gw * bw);
    }

    /// <summary>
    /// The two bin indexes a channel value falls between, and how far towards the upper one it sits.
    /// </summary>
    private static (int Low, int High, double Weight) Split(byte value)
    {
        // Bin centres at 32, 96, 160, 224 for four levels, so the position is measured between centres
        // rather than between edges. Values outside the outermost centres clamp, which is correct: pure
        // black and pure white belong wholly to their end bin.
        var position = ((value / 255.0) * LevelsPerChannel) - 0.5;
        var low = (int)Math.Floor(position);
        var weight = position - low;

        if (low < 0)
        {
            return (0, 0, 0);
        }

        if (low >= LevelsPerChannel - 1)
        {
            return (LevelsPerChannel - 1, LevelsPerChannel - 1, 0);
        }

        return (low, low + 1, weight);
    }

    private static void Add(double[] bins, int r, int g, int b, double weight)
    {
        if (weight > 0)
        {
            bins[(r * LevelsPerChannel * LevelsPerChannel) + (g * LevelsPerChannel) + b] += weight;
        }
    }
}
