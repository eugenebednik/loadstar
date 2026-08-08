using Loadstar.Core.Capture;

using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Loadstar.Capture.Windows;

/// <summary>
/// Decodes encoded image bytes into the project's <see cref="Bgra32Image"/>.
///
/// <para>The counterpart to <see cref="FrameEncoder"/>, and the primitive that makes local icon
/// identification possible: the icon index is built from questlog's own item art, which arrives as
/// <b>WebP</b> over HTTP, while the thing it is matched against is a screen capture. Both have to become
/// the same pixel format before a hash means anything.</para>
///
/// <para><b>WinRT's decoder rather than System.Drawing</b>, for one disqualifying reason: System.Drawing
/// cannot read WebP at all, and every icon questlog serves is WebP. WIC — which this wraps — has shipped
/// a WebP codec since Windows 10 1809, comfortably below the 19041 floor this app already requires for
/// Windows Graphics Capture. So this adds no dependency and no new platform requirement.</para>
///
/// <para><b>Alpha is straightened out here, and it is load-bearing.</b> Item icons are transparent
/// outside the artwork, and in a premultiplied buffer those pixels arrive as zeroed BGRA — which a
/// luminance hash reads as black. The in-game copy of the same icon is composited over a purple rarity
/// plate, so left alone the two would differ everywhere the artwork is absent, which is most of the
/// tile. Flattening onto a known background makes both sides comparable; see
/// <see cref="DecodeAsync"/>.</para>
/// </summary>
public static class ImageDecoder
{
    /// <summary>
    /// Decodes <paramref name="encoded"/> (WebP, PNG, JPEG — anything WIC handles) to BGRA32.
    /// </summary>
    /// <param name="flattenOnto">
    /// Background to composite transparent pixels onto, as (B, G, R). Null keeps the raw alpha.
    ///
    /// <para>Pass the colour the game draws its icons over. A transparent icon hashed against an
    /// opaque capture of the same icon compares artwork with artwork in the middle and nothing with
    /// plate everywhere else, and the everywhere-else is the majority of the pixels.</para>
    /// </param>
    public static async Task<Bgra32Image> DecodeAsync(
        byte[] encoded,
        (byte B, byte G, byte R)? flattenOnto = null)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        if (encoded.Length == 0)
        {
            throw new ArgumentException("Nothing to decode.", nameof(encoded));
        }

        using var stream = new InMemoryRandomAccessStream();
        using var writer = new DataWriter(stream);

        writer.WriteBytes(encoded);
        await writer.StoreAsync().AsTask().ConfigureAwait(false);
        await writer.FlushAsync().AsTask().ConfigureAwait(false);
        writer.DetachStream();

        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask().ConfigureAwait(false);

        // Straight alpha, not premultiplied: this code has to look at the alpha byte to decide what to
        // do with a pixel, and premultiplied colour is already destroyed where alpha is low.
        var bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Straight)
            .AsTask()
            .ConfigureAwait(false);

        using (bitmap)
        {
            var width = bitmap.PixelWidth;
            var height = bitmap.PixelHeight;
            var stride = width * Bgra32Image.BytesPerPixel;
            var pixels = new byte[stride * height];

            // Through a WinRT buffer rather than byte[].AsBuffer(): that extension lives in the
            // WindowsRuntime interop shim, which CsWinRT does not carry, and DataReader is already here.
            var buffer = new global::Windows.Storage.Streams.Buffer((uint)pixels.Length);

            bitmap.CopyToBuffer(buffer);

            using var reader = DataReader.FromBuffer(buffer);

            reader.ReadBytes(pixels);

            if (flattenOnto is { } background)
            {
                Flatten(pixels, background);
            }

            return new Bgra32Image(pixels, width, height, stride);
        }
    }

    /// <summary>
    /// Composites every pixel onto an opaque background, in place.
    ///
    /// <para>Standard source-over, done by hand because the whole buffer is one contiguous array and
    /// this avoids a second allocation the size of the image. Partial alpha is blended rather than
    /// thresholded — icon art is antialiased at its edges, and a hard cutoff would put a jagged
    /// high-contrast border into the hash, which is exactly the kind of detail dHash keys on.</para>
    /// </summary>
    private static void Flatten(byte[] pixels, (byte B, byte G, byte R) background)
    {
        for (var i = 0; i < pixels.Length; i += Bgra32Image.BytesPerPixel)
        {
            var alpha = pixels[i + 3];

            if (alpha == 255)
            {
                continue;
            }

            if (alpha == 0)
            {
                pixels[i] = background.B;
                pixels[i + 1] = background.G;
                pixels[i + 2] = background.R;
                pixels[i + 3] = 255;

                continue;
            }

            var inverse = 255 - alpha;

            pixels[i] = (byte)(((pixels[i] * alpha) + (background.B * inverse)) / 255);
            pixels[i + 1] = (byte)(((pixels[i + 1] * alpha) + (background.G * inverse)) / 255);
            pixels[i + 2] = (byte)(((pixels[i + 2] * alpha) + (background.R * inverse)) / 255);
            pixels[i + 3] = 255;
        }
    }
}
