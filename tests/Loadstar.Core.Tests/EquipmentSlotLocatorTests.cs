using Loadstar.Core.Capture;

using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Equipment-slot detection: finding the slots from their own appearance rather than from coordinates.
///
/// <para>Colours here are the ones measured off a real character sheet, because the detector's whole
/// premise is a colour relationship: the bronze rim is warm (R &gt; B) while both the rarity disc and the
/// panel behind it are cool (B &gt; R). Substituting invented colours would test the flood fill and prove
/// nothing about the premise.</para>
/// </summary>
public class EquipmentSlotLocatorTests
{
    private const int Ring = 0;
    private const int Disc = 1;
    private const int Panel = 2;

    /// <summary>Measured: rim (113,87,71), epic disc (34,18,46), panel (26,37,66) as R,G,B.</summary>
    private static readonly (byte B, byte G, byte R)[] Palette =
    [
        (71, 87, 113),
        (46, 18, 34),
        (66, 37, 26),
    ];

    private static Bgra32Image Sheet(int width, int height)
    {
        var stride = width * Bgra32Image.BytesPerPixel;
        var image = new Bgra32Image(new byte[stride * height], width, height, stride);
        var (b, g, r) = Palette[Panel];

        image.Fill(image.Bounds, b, g, r, 255);

        return image;
    }

    /// <summary>Draws one slot: a rim annulus around a disc, the way the game does.</summary>
    private static void DrawSlot(Bgra32Image image, int centreX, int centreY, int radius, int rim = 4)
    {
        for (var y = centreY - radius; y <= centreY + radius; y++)
        {
            for (var x = centreX - radius; x <= centreX + radius; x++)
            {
                if (x < 0 || y < 0 || x >= image.Width || y >= image.Height)
                {
                    continue;
                }

                var dx = x - centreX;
                var dy = y - centreY;
                var distance = Math.Sqrt((dx * dx) + (dy * dy));

                var part = distance > radius ? -1 : distance >= radius - rim ? Ring : Disc;

                if (part < 0)
                {
                    continue;
                }

                var (b, g, r) = Palette[part];

                image.Fill(new PixelRect(x, y, 1, 1), b, g, r, 255);
            }
        }
    }

    /// <summary>Two columns of the given length, at the pitch a real sheet uses.</summary>
    private static Bgra32Image SheetWithSlots(int width, int height, int radius, int rows)
    {
        var image = Sheet(width, height);
        var pitch = (int)(radius * 2.4);

        for (var row = 0; row < rows; row++)
        {
            DrawSlot(image, width / 2 - pitch / 2, (radius * 2) + (row * pitch), radius);
            DrawSlot(image, width / 2 + pitch / 2, (radius * 2) + (row * pitch), radius);
        }

        return image;
    }

    [Fact]
    public void ItFindsAGridOfSlots()
    {
        var found = EquipmentSlotLocator.Locate(SheetWithSlots(600, 900, radius: 30, rows: 6));

        Assert.Equal(12, found.Count);
    }

    /// <summary>
    /// The point of detecting rather than measuring. Verified against the real capture downscaled across a
    /// 6x range — 1280x800 through 7680x4320 all returned 14 slots — because every threshold derives from
    /// the image's own dimensions rather than from a pixel count.
    /// </summary>
    [Theory]
    [InlineData(1280, 800, 14)]
    [InlineData(1920, 1080, 20)]
    [InlineData(2560, 1600, 27)]
    [InlineData(3840, 2160, 41)]
    [InlineData(7680, 4320, 82)]
    public void ItScalesWithTheResolution(int width, int height, int radius)
    {
        var found = EquipmentSlotLocator.Locate(SheetWithSlots(width, height, radius, rows: 6));

        Assert.Equal(12, found.Count);
    }

    /// <summary>
    /// The behaviour that keeps a wrong answer off the screen. A capture of the open world or the inventory
    /// has no slot grid, and returning a dozen plausible rectangles anyway would mean identifying items
    /// from whatever happened to be at those coordinates.
    /// </summary>
    [Fact]
    public void ACaptureWithNoSlotsFindsNothing() =>
        Assert.Empty(EquipmentSlotLocator.Locate(Sheet(800, 600)));

    /// <summary>Too few circles is not a grid either — three bronze roundels are furniture, not equipment.</summary>
    [Fact]
    public void TooFewCirclesIsNotAGrid()
    {
        var image = Sheet(600, 600);

        DrawSlot(image, 100, 100, 30);
        DrawSlot(image, 200, 100, 30);
        DrawSlot(image, 300, 100, 30);

        Assert.Empty(EquipmentSlotLocator.Locate(image));
    }

    /// <summary>
    /// Solid bronze furniture must be rejected. The sheet has a crest, panel borders and a stat plate, all
    /// the same hue as the rim, so the discriminator is that a slot is an ANNULUS — mostly hole.
    /// </summary>
    [Fact]
    public void SolidWarmBlocksAreNotSlots()
    {
        var image = Sheet(600, 600);
        var (b, g, r) = Palette[Ring];

        for (var i = 0; i < 12; i++)
        {
            image.Fill(new PixelRect(40 + (i % 4 * 130), 40 + (i / 4 * 160), 60, 60), b, g, r, 255);
        }

        Assert.Empty(EquipmentSlotLocator.Locate(image));
    }

    /// <summary>
    /// The artwork region excludes the rim, which is the mistake that silently defeated the whole pipeline
    /// once: a region containing the rim gives ArtworkBounds a second non-backdrop colour on its edges, so
    /// the bounding box grows to the whole tile and no normalisation happens.
    /// </summary>
    [Fact]
    public void TheArtworkRegionExcludesTheRim()
    {
        var found = EquipmentSlotLocator.Locate(SheetWithSlots(600, 900, radius: 30, rows: 6));

        foreach (var slot in found)
        {
            Assert.True(slot.Artwork.Width < slot.Ring.Width, "artwork must be inside the ring");
            Assert.True(slot.Artwork.X > slot.Ring.X, "artwork must be inset from the ring");
            Assert.True(slot.Artwork.Right < slot.Ring.Right, "artwork must stop before the ring");
        }
    }

    /// <summary>
    /// The hashed square is centred on BOTH axes. Deriving the vertical inset from the width was a real bug:
    /// tiles are only approximately square, so it pushed the crop off-centre vertically. Fixing it took
    /// confident identifications on a real capture from one to three.
    /// </summary>
    [Fact]
    public void TheArtworkSquareIsCentredOnBothAxes()
    {
        foreach (var slot in EquipmentSlotLocator.Locate(SheetWithSlots(600, 900, radius: 30, rows: 6)))
        {
            var leftGap = slot.Artwork.X - slot.Disc.X;
            var rightGap = slot.Disc.Right - slot.Artwork.Right;
            var topGap = slot.Artwork.Y - slot.Disc.Y;
            var bottomGap = slot.Disc.Bottom - slot.Artwork.Bottom;

            Assert.True(Math.Abs(leftGap - rightGap) <= 1, $"horizontal {leftGap} vs {rightGap}");
            Assert.True(Math.Abs(topGap - bottomGap) <= 1, $"vertical {topGap} vs {bottomGap}");
        }
    }

    /// <summary>Reading order, so slot N means the same thing on every capture.</summary>
    [Fact]
    public void SlotsComeBackTopToBottomThenLeftToRight()
    {
        var found = EquipmentSlotLocator.Locate(SheetWithSlots(600, 900, radius: 30, rows: 6));

        for (var i = 1; i < found.Count; i++)
        {
            var previous = found[i - 1].Ring;
            var current = found[i].Ring;

            Assert.True(
                current.Y > previous.Y || (current.Y >= previous.Y - 2 && current.X > previous.X),
                $"slot {i} at {current.X},{current.Y} came after {previous.X},{previous.Y}");
        }
    }
}
