using Loadstar.Core.Capture;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Pixel handling, with the emphasis on stride.
///
/// <para>GPU rows are padded to alignment boundaries, so a captured frame routinely arrives with
/// dead bytes at the end of every row. Treating those as pixels is the classic diagonally-sheared
/// screenshot, and it is easy to write and easy to miss on a window whose width happens to be a
/// convenient multiple. Every test here therefore uses a padded buffer.</para>
/// </summary>
public sealed class Bgra32ImageTests
{
    /// <summary>Builds an image whose stride exceeds width*4, with each pixel encoding its own position.</summary>
    private static Bgra32Image PaddedImage(int width, int height, int padBytes)
    {
        var stride = width * Bgra32Image.BytesPerPixel + padBytes;
        var pixels = new byte[stride * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = y * stride + x * Bgra32Image.BytesPerPixel;
                pixels[offset] = (byte)x;       // B carries the column
                pixels[offset + 1] = (byte)y;   // G carries the row
                pixels[offset + 2] = 0x7F;
                pixels[offset + 3] = 0xFF;
            }

            // Poison the padding, so any code that mistakes it for pixel data is caught.
            for (var p = width * Bgra32Image.BytesPerPixel; p < stride; p++)
            {
                pixels[y * stride + p] = 0xEE;
            }
        }

        return new Bgra32Image(pixels, width, height, stride);
    }

    [Fact]
    public void CropReadsThroughPaddingRatherThanAcrossIt()
    {
        var image = PaddedImage(16, 8, padBytes: 12);

        var cropped = image.Crop(new PixelRect(4, 2, 8, 4));

        Assert.Equal(8, cropped.Width);
        Assert.Equal(4, cropped.Height);
        Assert.Equal(8 * Bgra32Image.BytesPerPixel, cropped.Stride);

        // Every pixel must still report the source coordinate it came from. If the padding had
        // been read as pixels these would drift by a few columns more on each successive row.
        for (var y = 0; y < cropped.Height; y++)
        {
            for (var x = 0; x < cropped.Width; x++)
            {
                var offset = y * cropped.Stride + x * Bgra32Image.BytesPerPixel;
                Assert.Equal((byte)(x + 4), cropped.Pixels[offset]);
                Assert.Equal((byte)(y + 2), cropped.Pixels[offset + 1]);
            }
        }
    }

    [Fact]
    public void TightlyPackedOutputDropsThePadding()
    {
        var image = PaddedImage(5, 3, padBytes: 8);

        var packed = image.ToTightlyPacked();

        Assert.Equal(5 * 3 * Bgra32Image.BytesPerPixel, packed.Length);
        Assert.DoesNotContain((byte)0xEE, packed);
    }

    [Fact]
    public void MaskBlanksExactlyTheRequestedRectangle()
    {
        var image = PaddedImage(10, 10, padBytes: 4);

        image.ApplyMasks([new PixelRect(2, 3, 4, 5)]);

        for (var y = 0; y < 10; y++)
        {
            for (var x = 0; x < 10; x++)
            {
                var offset = y * image.Stride + x * Bgra32Image.BytesPerPixel;
                var inside = x >= 2 && x < 6 && y >= 3 && y < 8;

                if (inside)
                {
                    Assert.Equal(0, image.Pixels[offset]);
                    Assert.Equal(0, image.Pixels[offset + 1]);
                    Assert.Equal(0, image.Pixels[offset + 2]);

                    // Opaque, not transparent — a transparent hole can composite back to the
                    // original pixels on some encoder paths, which would undo the masking.
                    Assert.Equal(255, image.Pixels[offset + 3]);
                }
                else
                {
                    Assert.Equal((byte)x, image.Pixels[offset]);
                    Assert.Equal((byte)y, image.Pixels[offset + 1]);
                }
            }
        }
    }

    [Fact]
    public void MaskExtendingPastTheEdgeIsClippedInsteadOfThrowing()
    {
        var image = PaddedImage(8, 8, padBytes: 4);

        image.ApplyMasks([new PixelRect(6, 6, 100, 100)]);

        var corner = 7 * image.Stride + 7 * Bgra32Image.BytesPerPixel;
        Assert.Equal(0, image.Pixels[corner]);
    }

    [Fact]
    public void CropCoveringTheWholeImageAvoidsACopy()
    {
        var image = PaddedImage(4, 4, padBytes: 0);

        Assert.Same(image, image.Crop(image.Bounds));
    }

    [Fact]
    public void CropLargerThanTheImageYieldsTheOverlapRatherThanFailing()
    {
        // A frame arriving a few pixels smaller than the window reported is normal; losing the
        // whole capture over it would be worse than trimming.
        var image = PaddedImage(10, 10, padBytes: 4);

        var cropped = image.Crop(new PixelRect(5, 5, 50, 50));

        Assert.Equal(5, cropped.Width);
        Assert.Equal(5, cropped.Height);
    }

    [Fact]
    public void CropEntirelyOutsideTheImageThrows()
    {
        var image = PaddedImage(10, 10, padBytes: 4);

        Assert.Throws<ArgumentException>(() => image.Crop(new PixelRect(50, 50, 10, 10)));
    }

    [Fact]
    public void UndersizedBufferIsRejectedAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => new Bgra32Image(new byte[10], 16, 16, 64));
    }
}
