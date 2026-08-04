namespace Loadstar.Core.Capture;

/// <summary>
/// A raw BGRA32 pixel buffer, with the two operations the capture path needs: crop to the
/// region of interest, and blank the privacy masks.
///
/// <para>Deliberately plain and platform-neutral. The Windows layer's job shrinks to "get bytes
/// out of a GPU surface" and "encode bytes as PNG"; everything that decides <em>which</em> pixels
/// leave the machine happens here, where it is testable without a GPU. Masking is the whole
/// reason: if it silently no-ops, other players' names go to the AI provider, and that is a
/// failure no one would see in a screenshot of the overlay.</para>
///
/// <para>BGRA is not an arbitrary choice — it is what Windows Graphics Capture hands back
/// (<c>B8G8R8A8UIntNormalized</c>), so treating it as the native format avoids a conversion pass.</para>
/// </summary>
public sealed class Bgra32Image
{
    public const int BytesPerPixel = 4;

    public int Width { get; }

    public int Height { get; }

    /// <summary>Bytes per row. Frequently wider than <see cref="Width"/> × 4 — GPU rows are padded.</summary>
    public int Stride { get; }

    public byte[] Pixels { get; }

    public Bgra32Image(byte[] pixels, int width, int height, int stride)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, width * BytesPerPixel);

        var required = (long)stride * (height - 1) + (long)width * BytesPerPixel;

        if (pixels.Length < required)
        {
            throw new ArgumentException(
                $"Buffer holds {pixels.Length} bytes; a {width}x{height} image at stride {stride} needs {required}.",
                nameof(pixels));
        }

        Pixels = pixels;
        Width = width;
        Height = height;
        Stride = stride;
    }

    public PixelRect Bounds => new(0, 0, Width, Height);

    /// <summary>
    /// Returns the sub-image described by <paramref name="rect"/>, tightly packed.
    ///
    /// <para>The rectangle is intersected with the image first, so an over-large crop yields
    /// whatever overlaps rather than throwing — a capture that arrives a few pixels smaller than
    /// the window reported is normal, and losing the whole frame over it would be worse.</para>
    /// </summary>
    public Bgra32Image Crop(PixelRect rect)
    {
        var clipped = rect.Intersect(Bounds);

        if (clipped.IsEmpty)
        {
            throw new ArgumentException($"Crop {rect} lies entirely outside the {Width}x{Height} image.", nameof(rect));
        }

        if (clipped == Bounds)
        {
            return this;
        }

        var destStride = clipped.Width * BytesPerPixel;
        var dest = new byte[destStride * clipped.Height];

        for (var row = 0; row < clipped.Height; row++)
        {
            var source = (clipped.Y + row) * Stride + clipped.X * BytesPerPixel;
            Array.Copy(Pixels, source, dest, row * destStride, destStride);
        }

        return new Bgra32Image(dest, clipped.Width, clipped.Height, destStride);
    }

    /// <summary>
    /// Blanks every rectangle in <paramref name="masks"/> to opaque black, in place.
    ///
    /// <para>Opaque rather than transparent: a transparent hole composites back to whatever the
    /// encoder puts underneath, which on some paths is the original pixels. Solid black cannot be
    /// undone.</para>
    /// </summary>
    public void ApplyMasks(IEnumerable<PixelRect> masks)
    {
        ArgumentNullException.ThrowIfNull(masks);

        foreach (var mask in masks)
        {
            Fill(mask, b: 0, g: 0, r: 0, a: 255);
        }
    }

    public void Fill(PixelRect rect, byte b, byte g, byte r, byte a)
    {
        var clipped = rect.Intersect(Bounds);

        if (clipped.IsEmpty)
        {
            return;
        }

        for (var y = clipped.Y; y < clipped.Bottom; y++)
        {
            var offset = y * Stride + clipped.X * BytesPerPixel;

            for (var x = 0; x < clipped.Width; x++)
            {
                Pixels[offset] = b;
                Pixels[offset + 1] = g;
                Pixels[offset + 2] = r;
                Pixels[offset + 3] = a;
                offset += BytesPerPixel;
            }
        }
    }

    /// <summary>Repacks to a gap-free buffer, which is what image encoders expect.</summary>
    public byte[] ToTightlyPacked()
    {
        var destStride = Width * BytesPerPixel;

        if (Stride == destStride && Pixels.Length == destStride * Height)
        {
            return Pixels;
        }

        var dest = new byte[destStride * Height];

        for (var row = 0; row < Height; row++)
        {
            Array.Copy(Pixels, row * Stride, dest, row * destStride, destStride);
        }

        return dest;
    }
}
