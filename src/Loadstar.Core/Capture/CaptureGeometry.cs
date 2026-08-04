using Loadstar.Core.Configuration;

namespace Loadstar.Core.Capture;

/// <summary>
/// Turns the fractional <see cref="CaptureRegion"/> values users configure into concrete pixel
/// rectangles against a captured surface.
///
/// <para>This is pure arithmetic and it lives in Core on purpose: the one genuinely error-prone
/// step in the capture path is expressing the privacy masks — which are authored in
/// <em>window</em> coordinates — relative to a crop that has already moved the origin. Getting
/// that wrong does not crash, it silently sends the chat panel to the model anyway. So the
/// translation is done here, where it can be tested without a GPU.</para>
/// </summary>
public static class CaptureGeometry
{
    /// <summary>
    /// Converts a fractional region to pixels against a surface.
    ///
    /// <para>Edges are rounded independently and the extent derived from them, rather than
    /// rounding the width. That way regions that abut in fractional space still abut in pixel
    /// space instead of leaving a one-pixel seam.</para>
    /// </summary>
    public static PixelRect ToPixels(CaptureRegion region, int surfaceWidth, int surfaceHeight)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentOutOfRangeException.ThrowIfNegative(surfaceWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(surfaceHeight);

        var left = RoundClamp(region.Left, surfaceWidth);
        var top = RoundClamp(region.Top, surfaceHeight);
        var right = RoundClamp(region.Left + region.Width, surfaceWidth);
        var bottom = RoundClamp(region.Top + region.Height, surfaceHeight);

        return new PixelRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>
    /// Expresses <paramref name="masks"/> — authored as fractions of the whole window — in the
    /// coordinate space of <paramref name="crop"/>, dropping any that fall outside it.
    ///
    /// <para>The two steps that are easy to omit: intersecting with the crop (a mask half outside
    /// it must be clipped, not skipped) and translating the origin (a crop starting at y=800 makes
    /// a mask at y=810 land at y=10). Omitting either leaves player names in the image.</para>
    /// </summary>
    public static IReadOnlyList<PixelRect> MasksForCrop(
        IEnumerable<CaptureRegion> masks,
        PixelRect crop,
        int surfaceWidth,
        int surfaceHeight)
    {
        ArgumentNullException.ThrowIfNull(masks);

        var result = new List<PixelRect>();

        foreach (var mask in masks)
        {
            var inSurface = ToPixels(mask, surfaceWidth, surfaceHeight);
            var clipped = inSurface.Intersect(crop);

            if (clipped.IsEmpty)
            {
                continue;
            }

            result.Add(clipped.Translate(-crop.X, -crop.Y));
        }

        return result;
    }

    private static int RoundClamp(double fraction, int extent)
    {
        if (double.IsNaN(fraction))
        {
            return 0;
        }

        var scaled = (int)Math.Round(fraction * extent, MidpointRounding.AwayFromZero);
        return Math.Clamp(scaled, 0, extent);
    }
}

/// <summary>An integer rectangle in pixels. Origin is top-left, as every surface here is.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    /// <summary>True when the rectangle covers no pixels, so callers can skip it entirely.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public PixelRect Intersect(PixelRect other)
    {
        var x = Math.Max(X, other.X);
        var y = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);

        return right <= x || bottom <= y
            ? default
            : new PixelRect(x, y, right - x, bottom - y);
    }

    public PixelRect Translate(int dx, int dy) => new(X + dx, Y + dy, Width, Height);

    public override string ToString() => $"{Width}x{Height}+{X}+{Y}";
}
