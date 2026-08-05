using Loadstar.Core.Capture;
using Loadstar.Core.Configuration;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Covers the capture path's one genuinely error-prone calculation.
///
/// <para>A wrong crop is obvious the first time anyone looks at the image. A wrong mask is not —
/// the picture looks fine, and the only symptom is that the party list and chat went to the AI
/// provider anyway. So the mask translation gets the attention here.</para>
/// </summary>
public sealed class CaptureGeometryTests
{
    private static CaptureRegion Region(double left, double top, double width, double height) =>
        new() { Left = left, Top = top, Width = width, Height = height };

    [Fact]
    public void FullRegionCoversTheWholeSurface()
    {
        var rect = CaptureGeometry.ToPixels(Region(0, 0, 1, 1), 2560, 1600);

        Assert.Equal(new PixelRect(0, 0, 2560, 1600), rect);
    }

    [Fact]
    public void CurrencyBarResolvesToTheTopStrip()
    {
        // The one crop the project treats as safe, because it is anchored to the screen edge.
        var rect = CaptureGeometry.ToPixels(Region(0, 0, 1.0, 0.035), 2560, 1600);

        Assert.Equal(0, rect.X);
        Assert.Equal(0, rect.Y);
        Assert.Equal(2560, rect.Width);
        Assert.Equal(56, rect.Height);
    }

    [Fact]
    public void RegionExtendingPastTheEdgeIsClampedRatherThanOverflowing()
    {
        var rect = CaptureGeometry.ToPixels(Region(0.8, 0.8, 0.5, 0.5), 1000, 1000);

        Assert.Equal(new PixelRect(800, 800, 200, 200), rect);
    }

    [Fact]
    public void AdjacentRegionsTileWithoutASeam()
    {
        // Edges are rounded independently and the extent derived, so thirds of an awkward width
        // still meet exactly instead of leaving a one-pixel gap.
        var left = CaptureGeometry.ToPixels(Region(0, 0, 1.0 / 3, 1), 1001, 10);
        var middle = CaptureGeometry.ToPixels(Region(1.0 / 3, 0, 1.0 / 3, 1), 1001, 10);
        var right = CaptureGeometry.ToPixels(Region(2.0 / 3, 0, 1.0 / 3, 1), 1001, 10);

        Assert.Equal(left.Right, middle.X);
        Assert.Equal(middle.Right, right.X);
        Assert.Equal(1001, right.Right);
    }

    [Fact]
    public void DegenerateRegionIsEmptyRatherThanNegative()
    {
        var rect = CaptureGeometry.ToPixels(Region(0.5, 0.5, 0, 0), 1000, 1000);

        Assert.True(rect.IsEmpty);
    }

    [Fact]
    public void MaskInsideAFullWindowCropKeepsItsPosition()
    {
        var crop = new PixelRect(0, 0, 1000, 1000);

        var masks = CaptureGeometry.MasksForCrop([Region(0, 0.72, 0.32, 0.28)], crop, 1000, 1000);

        Assert.Equal(new PixelRect(0, 720, 320, 280), Assert.Single(masks));
    }

    [Fact]
    public void MaskIsTranslatedIntoTheCropsCoordinateSpace()
    {
        // This is the bug worth having a test for. The mask is authored against the window, but the
        // buffer being painted starts at the crop's origin — so a mask at y=720 in the window has
        // to land at y=220 in a crop that begins at y=500. Skipping the translation paints a black
        // box over the wrong part of the image and leaves the real one untouched.
        var crop = new PixelRect(0, 500, 1000, 500);

        var masks = CaptureGeometry.MasksForCrop([Region(0, 0.72, 0.32, 0.28)], crop, 1000, 1000);

        Assert.Equal(new PixelRect(0, 220, 320, 280), Assert.Single(masks));
    }

    [Fact]
    public void MaskOutsideTheCropIsDropped()
    {
        var crop = new PixelRect(0, 0, 1000, 400);

        var masks = CaptureGeometry.MasksForCrop([Region(0, 0.72, 0.32, 0.28)], crop, 1000, 1000);

        Assert.Empty(masks);
    }

    [Fact]
    public void MaskStraddlingTheCropEdgeIsClippedNotDiscarded()
    {
        // Half in, half out. Discarding it would leave the visible half unmasked, which is the
        // failure that matters.
        var crop = new PixelRect(0, 600, 1000, 400);

        var masks = CaptureGeometry.MasksForCrop([Region(0, 0.5, 0.3, 0.3)], crop, 1000, 1000);

        var mask = Assert.Single(masks);
        Assert.Equal(new PixelRect(0, 0, 300, 200), mask);
    }

    [Fact]
    public void MaskIsAlsoClippedHorizontally()
    {
        var crop = new PixelRect(200, 0, 400, 1000);

        var masks = CaptureGeometry.MasksForCrop([Region(0, 0, 0.4, 0.1)], crop, 1000, 1000);

        var mask = Assert.Single(masks);
        Assert.Equal(new PixelRect(0, 0, 200, 100), mask);
    }

    [Fact]
    public void IntersectionOfDisjointRectanglesIsEmpty()
    {
        var a = new PixelRect(0, 0, 10, 10);
        var b = new PixelRect(20, 20, 10, 10);

        Assert.True(a.Intersect(b).IsEmpty);
    }
}
