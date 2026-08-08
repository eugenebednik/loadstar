namespace Loadstar.Core.Capture;

/// <summary>
/// Finds the artwork inside an icon, so two copies of the same item can be compared.
///
/// <para><b>Why this is necessary, measured rather than assumed.</b> Hashing a questlog icon against the
/// same item cropped from a character sheet produced distances of 71–108 bits out of 256 — barely better
/// than the ~128 two unrelated hashes would give. The cause is framing, not coordinates: questlog's art
/// fills its 200x200 frame edge to edge on a transparent background, while the game insets the same art
/// inside a coloured rarity disc ringed in bronze. So most of each tile is background, the two backgrounds
/// are nothing alike, and a gradient hash keys on exactly that.</para>
///
/// <para>Normalising both sides to the artwork's own bounding box removes the difference at its source.
/// After it, a hash compares art with art.</para>
/// </summary>
public static class ArtworkBounds
{
    /// <summary>
    /// The bounding box of pixels that are meaningfully opaque — for icons that arrive with real alpha,
    /// which is how questlog serves them.
    /// </summary>
    /// <param name="minimumAlpha">
    /// Above this counts as artwork. Not 1: icon art is antialiased and drop-shadowed, and a threshold of
    /// one would grow the box out to include a halo of nearly-invisible pixels, which defeats the point.
    /// </param>
    public static PixelRect FromAlpha(Bgra32Image image, byte minimumAlpha = 24)
    {
        ArgumentNullException.ThrowIfNull(image);

        return Scan(image, image.Bounds, (b, g, r, a) => a >= minimumAlpha);
    }

    /// <summary>
    /// The bounding box of pixels that differ from the tile's background, for an icon already composited
    /// over one — which is how it arrives from a screen capture.
    ///
    /// <para><b>The background colour is sampled, not hardcoded.</b> The disc behind an equipment icon is
    /// the item's RARITY colour: purple for epic, orange for heroic, and a fixed constant would silently
    /// stop working on exactly the best items a player owns. Sampling adapts for free.</para>
    /// </summary>
    /// <param name="tolerance">
    /// How far a channel may stray from the sampled background and still count as background. The disc is
    /// a gradient rather than a flat fill, so this has to absorb real variation across the tile.
    /// </param>
    public static PixelRect FromBackdrop(Bgra32Image image, PixelRect region, int tolerance = 46)
    {
        ArgumentNullException.ThrowIfNull(image);

        var clipped = region.Intersect(image.Bounds);

        if (clipped.IsEmpty)
        {
            return clipped;
        }

        var (backB, backG, backR) = SampleBackdrop(image, clipped);

        return Scan(
            image,
            clipped,
            (b, g, r, _) =>
                Math.Abs(b - backB) > tolerance
                || Math.Abs(g - backG) > tolerance
                || Math.Abs(r - backR) > tolerance);
    }

    /// <summary>
    /// Estimates the tile background from the pixels nearest its corners.
    ///
    /// <para>The corners are chosen because the artwork is centred: a disc inscribed in a square leaves the
    /// corners as background under every rarity, and the artwork never reaches them. Median rather than
    /// mean, so the bronze ring clipping one corner cannot drag the estimate.</para>
    /// </summary>
    private static (int B, int G, int R) SampleBackdrop(Bgra32Image image, PixelRect region)
    {
        var inset = Math.Max(1, Math.Min(region.Width, region.Height) / 10);
        var bs = new List<byte>();
        var gs = new List<byte>();
        var rs = new List<byte>();

        (int X, int Y)[] corners =
        [
            (region.X + inset, region.Y + inset),
            (region.Right - inset - 1, region.Y + inset),
            (region.X + inset, region.Bottom - inset - 1),
            (region.Right - inset - 1, region.Bottom - inset - 1),
        ];

        foreach (var (cx, cy) in corners)
        {
            // A small patch per corner rather than a single pixel, so noise and gradient banding average
            // out before the median sees them.
            for (var y = cy - 1; y <= cy + 1; y++)
            {
                for (var x = cx - 1; x <= cx + 1; x++)
                {
                    if (x < 0 || y < 0 || x >= image.Width || y >= image.Height)
                    {
                        continue;
                    }

                    var offset = (y * image.Stride) + (x * Bgra32Image.BytesPerPixel);

                    bs.Add(image.Pixels[offset]);
                    gs.Add(image.Pixels[offset + 1]);
                    rs.Add(image.Pixels[offset + 2]);
                }
            }
        }

        return (Median(bs), Median(gs), Median(rs));
    }

    private static byte Median(List<byte> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        values.Sort();

        return values[values.Count / 2];
    }

    /// <summary>
    /// The bounding box of every pixel the predicate accepts, or the whole region when none do.
    ///
    /// <para>Falling back to the region rather than to an empty rectangle matters: a tile that is entirely
    /// background is an EMPTY equipment slot, and returning nothing to hash would make the caller decide
    /// what that means. Returning the region lets it hash a uniform patch, which cannot match any item and
    /// so reports unidentified — the correct outcome, reached without a special case.</para>
    /// </summary>
    private static PixelRect Scan(
        Bgra32Image image,
        PixelRect region,
        Func<byte, byte, byte, byte, bool> isArtwork)
    {
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;

        for (var y = region.Y; y < region.Bottom; y++)
        {
            var offset = (y * image.Stride) + (region.X * Bgra32Image.BytesPerPixel);

            for (var x = region.X; x < region.Right; x++)
            {
                if (isArtwork(
                        image.Pixels[offset],
                        image.Pixels[offset + 1],
                        image.Pixels[offset + 2],
                        image.Pixels[offset + 3]))
                {
                    if (x < minX) { minX = x; }
                    if (x > maxX) { maxX = x; }
                    if (y < minY) { minY = y; }
                    if (y > maxY) { maxY = y; }
                }

                offset += Bgra32Image.BytesPerPixel;
            }
        }

        return maxX < minX
            ? region
            : new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}
