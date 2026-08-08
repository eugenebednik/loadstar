using Loadstar.Core.Capture;
using Loadstar.Games.ThroneAndLiberty;

using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Icon normalisation, and the questlog URL rule.
///
/// <para>Both exist to serve local icon identification, which CLAUDE.md has wanted since the first
/// commit: a vision model asked to name a 40px icon returns plausible wrong names, so identification has
/// to be a deterministic local lookup.</para>
/// </summary>
public class ArtworkBoundsTests
{
    private static Bgra32Image Blank(int width, int height, byte b, byte g, byte r, byte a)
    {
        var stride = width * Bgra32Image.BytesPerPixel;
        var image = new Bgra32Image(new byte[stride * height], width, height, stride);

        image.Fill(image.Bounds, b, g, r, a);

        return image;
    }

    private static void Paint(Bgra32Image image, PixelRect rect, byte b, byte g, byte r, byte a) =>
        image.Fill(rect, b, g, r, a);

    /// <summary>
    /// The questlog case: transparent everywhere except the artwork. This is what makes the index side
    /// comparable to a capture — measured, the crop moved median nearest-neighbour separation across the
    /// real catalogue from 48 bits to 75.
    /// </summary>
    [Fact]
    public void AlphaBoundsFindTheOpaqueArtwork()
    {
        var image = Blank(64, 64, 0, 0, 0, 0);

        Paint(image, new PixelRect(10, 20, 30, 16), 200, 200, 200, 255);

        Assert.Equal(new PixelRect(10, 20, 30, 16), ArtworkBounds.FromAlpha(image));
    }

    /// <summary>
    /// Antialiasing and drop shadows leave a halo of nearly-invisible pixels. Including them would grow
    /// the box past the artwork and undo the normalisation.
    /// </summary>
    [Fact]
    public void NearlyTransparentHaloIsNotArtwork()
    {
        var image = Blank(64, 64, 0, 0, 0, 0);

        Paint(image, new PixelRect(4, 4, 56, 56), 200, 200, 200, 8);
        Paint(image, new PixelRect(20, 20, 10, 10), 200, 200, 200, 255);

        Assert.Equal(new PixelRect(20, 20, 10, 10), ArtworkBounds.FromAlpha(image));
    }

    /// <summary>The capture case: art over an opaque rarity disc, with the disc sampled from the corners.</summary>
    [Fact]
    public void BackdropBoundsFindArtOverAColouredTile()
    {
        var image = Blank(64, 64, 0x8E, 0x3F, 0x86, 255);

        Paint(image, new PixelRect(18, 14, 24, 30), 240, 240, 240, 255);

        Assert.Equal(new PixelRect(18, 14, 24, 30), ArtworkBounds.FromBackdrop(image, image.Bounds));
    }

    /// <summary>
    /// The rarity colour is NOT assumed. An orange heroic tile has to work as well as a purple epic one,
    /// and a hardcoded purple would have failed on exactly the best items a player owns.
    /// </summary>
    [Fact]
    public void TheTileColourIsSampledNotAssumed()
    {
        var image = Blank(64, 64, 0x20, 0x80, 0xE0, 255);

        Paint(image, new PixelRect(22, 22, 12, 12), 250, 250, 250, 255);

        Assert.Equal(new PixelRect(22, 22, 12, 12), ArtworkBounds.FromBackdrop(image, image.Bounds));
    }

    /// <summary>
    /// An empty slot is all backdrop. Returning the region rather than nothing means the caller hashes a
    /// uniform patch, which matches no item and so reports unidentified — right answer, no special case.
    /// </summary>
    [Fact]
    public void AUniformTileFallsBackToTheWholeRegion()
    {
        var image = Blank(40, 40, 0x8E, 0x3F, 0x86, 255);

        Assert.Equal(image.Bounds, ArtworkBounds.FromBackdrop(image, image.Bounds));
    }

    /// <summary>
    /// THE BUG THAT STOPPED THIS WORKING, pinned so it cannot come back silently. A region that includes
    /// the tile's bronze ring has a second non-backdrop colour touching its edges, so the bounding box
    /// grows to the whole region and no normalisation happens at all. Every slot then reported
    /// unidentified at 78–107 bits, which is near noise.
    ///
    /// <para>The lesson is about the CALLER: the region handed in has to be inside the ring. This class
    /// cannot rescue a region that is not, and it must not pretend to.</para>
    /// </summary>
    [Fact]
    public void ARegionIncludingTheRingDefeatsNormalisation()
    {
        var image = Blank(64, 64, 0x8E, 0x3F, 0x86, 255);

        // A bronze ring around the edge, as a too-generous crop would include.
        Paint(image, new PixelRect(0, 0, 64, 3), 0x50, 0x6E, 0x96, 255);
        Paint(image, new PixelRect(0, 61, 64, 3), 0x50, 0x6E, 0x96, 255);
        Paint(image, new PixelRect(26, 26, 12, 12), 240, 240, 240, 255);

        var bounds = ArtworkBounds.FromBackdrop(image, image.Bounds);

        Assert.Equal(image.Bounds.Width, bounds.Width);
    }

    /// <summary>
    /// The repeated stem is Unreal's Package.AssetName, not an extension. Every URL built from the path
    /// verbatim returns HTTP 200 with questlog's SPA shell as text/html — a success status carrying a web
    /// page, which is the worst way to be wrong.
    /// </summary>
    [Theory]
    [InlineData(
        "/assets/Game/Image/Icon/Item_128/Equip/Armor/P_Set_FA_M_PT_00022B.P_Set_FA_M_PT_00022B",
        "https://cdn.questlog.gg/throne-and-liberty/assets/Game/Image/Icon/Item_128/Equip/Armor/P_Set_FA_M_PT_00022B.webp")]
    [InlineData(
        "assets/Game/Image/Icon/Item_128/Equip/Acc/PC_Necklace_00006.PC_Necklace_00006",
        "https://cdn.questlog.gg/throne-and-liberty/assets/Game/Image/Icon/Item_128/Equip/Acc/PC_Necklace_00006.webp")]
    [InlineData(
        "/assets/Game/Image/Icon/Item_128/ETC/ICO_Adena",
        "https://cdn.questlog.gg/throne-and-liberty/assets/Game/Image/Icon/Item_128/ETC/ICO_Adena.webp")]
    public void IconUrlsDropTheRepeatedStemAndAddWebp(string path, string expected) =>
        Assert.Equal(expected, TlIconSource.UrlFor(path));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData("/trailing/")]
    public void AnUnusableIconPathYieldsNoUrl(string? path) => Assert.Null(TlIconSource.UrlFor(path));

    /// <summary>
    /// Cached by ICON, not by item: 1,773 items resolve to 1,522 distinct icons, so keying on the item id
    /// would download the same bytes 251 extra times.
    /// </summary>
    [Fact]
    public void TwoItemsSharingAnIconShareACacheFile()
    {
        const string path = "/assets/Game/Image/Icon/Item_128/Equip/Acc/IT_P_Ring_00069.IT_P_Ring_00069";

        Assert.Equal(
            TlIconSource.CacheFileNameFor(path),
            TlIconSource.CacheFileNameFor("assets/Game/Image/Icon/Item_128/Equip/Acc/IT_P_Ring_00069"));

        Assert.DoesNotContain('/', TlIconSource.CacheFileNameFor(path)!);
    }
}
