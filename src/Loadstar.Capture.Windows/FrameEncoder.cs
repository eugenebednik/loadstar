using System.Runtime.InteropServices;
using Loadstar.Core.Capture;
using Windows.Graphics.Capture;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT;

namespace Loadstar.Capture.Windows;

/// <summary>
/// Turns a captured GPU frame into the PNG that gets sent to the AI provider.
///
/// <para>The order of operations is the point: pixels come down from the GPU, get cropped, get
/// their privacy masks painted, and only then get encoded. Masking after encoding would be
/// decorative — the bytes would already exist unmasked — and masking before cropping would put
/// the black boxes in the wrong place once the origin moves. The geometry itself lives in
/// <see cref="CaptureGeometry"/> in Core so it can be tested without a GPU.</para>
/// </summary>
internal static class FrameEncoder
{
    public static async Task<CaptureResult> EncodeAsync(
        Direct3D11CaptureFrame frame,
        GameWindow window,
        CaptureRequest request,
        CancellationToken cancellationToken)
    {
        var image = await ReadPixelsAsync(frame).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        var crop = request.Region is null
            ? image.Bounds
            : CaptureGeometry.ToPixels(request.Region, image.Width, image.Height);

        if (crop.IsEmpty)
        {
            return CaptureResult.Fail(
                CaptureStatus.Failed,
                $"The configured capture region resolves to nothing against a {image.Width}x{image.Height} window.");
        }

        // Masks are authored against the window, so they are resolved against the full frame and
        // then moved into the crop's coordinate space — not resolved against the crop.
        var masks = CaptureGeometry.MasksForCrop(request.PrivacyMasks, crop, image.Width, image.Height);

        var cropped = image.Crop(crop);
        cropped.ApplyMasks(masks);

        var png = await EncodePngAsync(cropped).ConfigureAwait(false);

        return CaptureResult.Ok(new CapturedFrame
        {
            Png = png,
            Width = cropped.Width,
            Height = cropped.Height,
            CapturedAt = DateTimeOffset.Now,
            WindowTitle = window.Title,
            Label = request.Label,
            PrivacyMasksApplied = masks.Count,
        });
    }

    /// <summary>
    /// Copies the frame off the GPU into a managed BGRA buffer.
    ///
    /// <para>The stride from the plane description is carried through rather than assumed. GPU rows
    /// are padded to alignment boundaries, so a capture whose width is not a convenient multiple
    /// arrives with gaps at the end of every row; treating those as pixels produces the classic
    /// diagonally-sheared screenshot.</para>
    /// </summary>
    private static async Task<Bgra32Image> ReadPixelsAsync(Direct3D11CaptureFrame frame)
    {
        using var bitmap = await SoftwareBitmap
            .CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Premultiplied)
            .AsTask()
            .ConfigureAwait(false);

        using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
        using var reference = buffer.CreateReference();

        var access = reference.As<NativeMethods.IMemoryBufferByteAccess>();
        access.GetBuffer(out var pointer, out var capacity);

        if (pointer == IntPtr.Zero || capacity == 0)
        {
            throw new CaptureException("Locked frame buffer was empty.");
        }

        var plane = buffer.GetPlaneDescription(0);
        var managed = new byte[capacity];
        Marshal.Copy(pointer, managed, 0, (int)capacity);

        return new Bgra32Image(managed, plane.Width, plane.Height, plane.Stride);
    }

    private static async Task<byte[]> EncodePngAsync(Bgra32Image image)
    {
        using var stream = new InMemoryRandomAccessStream();

        var encoder = await BitmapEncoder
            .CreateAsync(BitmapEncoder.PngEncoderId, stream)
            .AsTask()
            .ConfigureAwait(false);

        // BitmapAlphaMode.Ignore, not Premultiplied. Window captures routinely come back with a
        // zeroed alpha channel, and honouring it yields a PNG that is technically correct and
        // entirely transparent — which the model then reads as a blank screen.
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            (uint)image.Width,
            (uint)image.Height,
            dpiX: 96,
            dpiY: 96,
            pixels: image.ToTightlyPacked());

        await encoder.FlushAsync().AsTask().ConfigureAwait(false);

        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size).AsTask().ConfigureAwait(false);
        reader.ReadBytes(bytes);

        return bytes;
    }
}
