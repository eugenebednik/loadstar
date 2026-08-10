namespace Loadstar.Core.Capture;

/// <summary>
/// A small colour thumbnail of an icon, compared by cosine similarity. This is what identifies equipment
/// across renderings; <see cref="PerceptualHash"/> is kept for matching images the game drew both times.
///
/// <para><b>Measured against the alternative, on real tiles with real ground truth.</b> Ranking the correct
/// item within its category pool on a live character sheet, where the player independently confirmed the
/// equipped set:</para>
///
/// <list type="table">
/// <item><description>slot ...... dHash (was) ... this</description></item>
/// <item><description>head ...... #2 of 109 ..... <b>#1</b></description></item>
/// <item><description>hands ..... #47 of 110 .... <b>#2</b></description></item>
/// <item><description>legs ...... #20 of 108 .... <b>#5</b></description></item>
/// <item><description>chest ..... #36 of 110 .... <b>#25</b></description></item>
/// <item><description>feet ...... #60 of 106 .... <b>#26</b></description></item>
/// </list>
///
/// <para><b>Why colour, when a colour histogram was already tried and rejected.</b> The earlier attempt was
/// a global histogram, which discards where the colours are — and it ranked the one verified item 154th. The
/// difference here is that the colour is SPATIAL: a 12x12 grid per channel keeps the layout, so a green gem
/// in the middle of a dark band is a different signature from a dark band with a green edge. The old
/// conclusion "colour does not work" was really "colour histograms do not work", and generalising it cost
/// this feature a long time.</para>
///
/// <para><b>Why real values rather than a bit per comparison.</b> A difference hash keeps only the SIGN of
/// each adjacent-pixel comparison, so a strong edge and a faint one are indistinguishable. Across two
/// renderings of the same item the faint comparisons are exactly the ones that flip, which is why the hash
/// degrades into noise while the magnitudes stay informative.</para>
///
/// <para><b>Each channel is centred and normalised separately.</b> That is what makes this survive the
/// game's lighting: a brightness or contrast shift moves the mean and the scale, and both are divided out.
/// What survives is the relative pattern, which is the part that identifies the item.</para>
/// </summary>
public sealed class IconSignature
{
    /// <summary>
    /// Grid resolution per channel. 12 was not tuned to a single sample — it is small enough that a few
    /// pixels of registration error move each cell only slightly, and large enough to keep the layout that
    /// distinguishes items whose palettes match.
    /// </summary>
    public const int Grid = 12;

    private const int Channels = 3;

    private readonly float[] _values;

    private IconSignature(float[] values) => _values = values;

    /// <summary>Length of the underlying vector, for callers that persist it.</summary>
    public int Length => _values.Length;

    public IReadOnlyList<float> Values => _values;

    /// <summary>
    /// Builds the signature for a region of an image.
    ///
    /// <para>Cells are AREA-AVERAGED rather than point-sampled, for the same reason the hash is: the index
    /// is built from published art at a few hundred pixels and matched against a tile of a hundred or so, and
    /// a point sample lands on whichever pixels happen to fall on the grid.</para>
    /// </summary>
    public static IconSignature Compute(Bgra32Image image, PixelRect region)
    {
        ArgumentNullException.ThrowIfNull(image);

        var clipped = region.Intersect(image.Bounds);

        if (clipped.IsEmpty)
        {
            throw new ArgumentException(
                $"Region {region} lies outside the {image.Width}x{image.Height} image.", nameof(region));
        }

        var sums = new double[Channels, Grid, Grid];
        var counts = new int[Grid, Grid];

        for (var cy = 0; cy < Grid; cy++)
        {
            var top = clipped.Y + (int)((long)cy * clipped.Height / Grid);
            var bottom = clipped.Y + (int)((long)(cy + 1) * clipped.Height / Grid);

            bottom = Math.Max(bottom, top + 1);

            for (var cx = 0; cx < Grid; cx++)
            {
                var left = clipped.X + (int)((long)cx * clipped.Width / Grid);
                var right = clipped.X + (int)((long)(cx + 1) * clipped.Width / Grid);

                right = Math.Max(right, left + 1);

                for (var y = top; y < Math.Min(bottom, image.Height); y++)
                {
                    var offset = (y * image.Stride) + (left * Bgra32Image.BytesPerPixel);

                    for (var x = left; x < Math.Min(right, image.Width); x++)
                    {
                        sums[0, cy, cx] += image.Pixels[offset + 2];   // R
                        sums[1, cy, cx] += image.Pixels[offset + 1];   // G
                        sums[2, cy, cx] += image.Pixels[offset];       // B

                        offset += Bgra32Image.BytesPerPixel;
                        counts[cy, cx]++;
                    }
                }
            }
        }

        var values = new float[Channels * Grid * Grid];

        for (var c = 0; c < Channels; c++)
        {
            // Centre and normalise WITHIN the channel. Doing it across all three at once would let a
            // colour cast dominate, which is the thing the game's lighting varies most.
            double mean = 0;

            for (var cy = 0; cy < Grid; cy++)
            {
                for (var cx = 0; cx < Grid; cx++)
                {
                    mean += counts[cy, cx] == 0 ? 0 : sums[c, cy, cx] / counts[cy, cx];
                }
            }

            mean /= Grid * Grid;

            double energy = 0;
            var plane = new double[Grid * Grid];

            for (var i = 0; i < plane.Length; i++)
            {
                var cy = i / Grid;
                var cx = i % Grid;
                var value = counts[cy, cx] == 0 ? 0 : sums[c, cy, cx] / counts[cy, cx];

                plane[i] = value - mean;
                energy += plane[i] * plane[i];
            }

            var scale = energy > 1e-9 ? 1.0 / Math.Sqrt(energy) : 0.0;

            for (var i = 0; i < plane.Length; i++)
            {
                values[(c * Grid * Grid) + i] = (float)(plane[i] * scale);
            }
        }

        return new IconSignature(values);
    }

    public static IconSignature Compute(Bgra32Image image) => Compute(image, image.Bounds);

    /// <summary>
    /// Cosine similarity: 3.0 when every channel matches perfectly, 0 for unrelated images, negative when
    /// anti-correlated. Three rather than one because each channel is separately unit-length.
    /// </summary>
    public double SimilarityTo(IconSignature other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (other._values.Length != _values.Length)
        {
            throw new ArgumentException("Signatures were built with different grid sizes.", nameof(other));
        }

        double total = 0;

        for (var i = 0; i < _values.Length; i++)
        {
            total += (double)_values[i] * other._values[i];
        }

        return total;
    }

    /// <summary>Round-trips through JSON as a plain float array, so an index file stays inspectable.</summary>
    public static IconSignature FromValues(IReadOnlyList<float> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count != Channels * Grid * Grid)
        {
            throw new ArgumentException(
                $"Expected {Channels * Grid * Grid} values, got {values.Count}.", nameof(values));
        }

        return new IconSignature([.. values]);
    }
}
