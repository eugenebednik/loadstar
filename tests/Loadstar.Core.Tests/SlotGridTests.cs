using Loadstar.Core.Capture;
using Loadstar.Games.ThroneAndLiberty;

using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Fitting the detected circles to a two-column grid, and using the resulting position to constrain what an
/// item can be. Together these took precision on a real capture from 2 of 3 to 2 of 2 and detection from 14
/// circles to exactly the 13 slots that exist.
/// </summary>
public class SlotGridTests
{
    private static readonly (byte B, byte G, byte R) Rim = (71, 87, 113);
    private static readonly (byte B, byte G, byte R) Disc = (46, 18, 34);
    private static readonly (byte B, byte G, byte R) Panel = (66, 37, 26);

    private static Bgra32Image Sheet(int width, int height)
    {
        var stride = width * Bgra32Image.BytesPerPixel;
        var image = new Bgra32Image(new byte[stride * height], width, height, stride);

        image.Fill(image.Bounds, Panel.B, Panel.G, Panel.R, 255);

        return image;
    }

    private static void DrawSlot(Bgra32Image image, int cx, int cy, int radius)
    {
        for (var y = cy - radius; y <= cy + radius; y++)
        {
            for (var x = cx - radius; x <= cx + radius; x++)
            {
                if (x < 0 || y < 0 || x >= image.Width || y >= image.Height)
                {
                    continue;
                }

                var distance = Math.Sqrt(((x - cx) * (x - cx)) + ((y - cy) * (y - cy)));

                if (distance > radius)
                {
                    continue;
                }

                var part = distance >= radius - 4 ? Rim : Disc;

                image.Fill(new PixelRect(x, y, 1, 1), part.B, part.G, part.R, 255);
            }
        }
    }

    private static Bgra32Image Grid(int rows, params (int X, int Y)[] extras)
    {
        var image = Sheet(900, 900);

        for (var row = 0; row < rows; row++)
        {
            DrawSlot(image, 600, 80 + (row * 72), 30);
            DrawSlot(image, 700, 80 + (row * 72), 30);
        }

        foreach (var (x, y) in extras)
        {
            DrawSlot(image, x, y, 30);
        }

        return image;
    }

    /// <summary>Rows and columns are labelled, so a slot's position is usable downstream.</summary>
    [Fact]
    public void SlotsAreLabelledByRowAndColumn()
    {
        var found = EquipmentSlotLocator.Locate(Grid(rows: 6));

        Assert.Equal(12, found.Count);
        Assert.Equal(0, found[0].Row);
        Assert.Equal(0, found[0].Column);
        Assert.Equal(1, found[1].Column);
        Assert.Equal(5, found[^1].Row);
    }

    /// <summary>
    /// A circle of the right size in the wrong place is discarded. Size clustering cannot reject it — it IS
    /// the right size — and on a real sheet exactly one such stray appeared outside the equipment panel.
    /// </summary>
    [Fact]
    public void AStrayCircleOffTheGridIsDiscarded()
    {
        var found = EquipmentSlotLocator.Locate(Grid(rows: 6, extras: (180, 300)));

        Assert.Equal(12, found.Count);
        Assert.DoesNotContain(found, slot => slot.Ring.X < 400);
    }

    /// <summary>
    /// THE BUG THAT REPLACED IT, pinned. Splitting the columns at the widest gap in the sorted x values let
    /// one distant stray define the split: the gap either side of the OUTLIER was wider than the gap between
    /// the two real columns, so one column captured the stray and the other captured both real columns.
    /// Detection fell from 14 circles to 8. Clustering by population is immune, because a column holds many
    /// slots and a stray holds one.
    /// </summary>
    [Fact]
    public void AVeryDistantStrayDoesNotHijackTheColumnSplit()
    {
        var found = EquipmentSlotLocator.Locate(Grid(rows: 6, extras: (80, 450)));

        Assert.Equal(12, found.Count);
        Assert.Equal(2, found.Select(slot => slot.Column).Distinct().Count());
        Assert.Equal(6, found.Count(slot => slot.Column == 0));
    }

    /// <summary>Armour on top, accessories below, as read off a live character sheet.</summary>
    [Theory]
    [InlineData(0, "head")]
    [InlineData(1, "chest")]
    [InlineData(2, "legs")]
    [InlineData(3, "necklace")]
    [InlineData(5, "belt")]
    public void RowsMapToTheKindOfItemTheyHold(int row, string expected) =>
        Assert.Contains(expected, TlEquipmentLayout.CategoriesForRow(row));

    /// <summary>
    /// The exclusion that matters most: no row of this grid can hold a weapon, a consumable or an artifact,
    /// and those are most of the catalogue.
    /// </summary>
    [Theory]
    [InlineData("bow")]
    [InlineData("wand")]
    [InlineData("attack")]
    [InlineData("talistone1")]
    [InlineData("gemstone1")]
    [InlineData("stellarite")]
    public void NoRowEverHoldsSomethingThatIsNotWornEquipment(string category)
    {
        for (var row = 0; row < 8; row++)
        {
            Assert.DoesNotContain(category, TlEquipmentLayout.CategoriesForRow(row));
        }
    }

    /// <summary>An unexpected row degrades to an unconstrained search, never to a guaranteed miss.</summary>
    [Fact]
    public void AnOutOfRangeRowAllowsEverything() =>
        Assert.Equal(
            TlEquipmentLayout.Everything.Count,
            TlEquipmentLayout.CategoriesForRow(-1).Count);

    /// <summary>
    /// The filter in action: the same query resolves differently depending on what the slot can hold. A pair
    /// of trousers was matched into an earring slot before this existed.
    /// </summary>
    [Fact]
    public void TheCategoryFilterExcludesImplausibleItems()
    {
        var index = new IconIndex();

        index.Add("Glade Stalker Trousers", Bits(60), "legs");
        index.Add("Some Earring", Bits(95), "earring");

        var query = new IconHash(0, 0, 0, 0);

        Assert.Equal("Glade Stalker Trousers", index.MatchAcrossRenderings(query)?.Name);
        Assert.Equal("Some Earring", index.MatchAcrossRenderings(query, ["earring"])?.Name);
    }

    /// <summary>Nothing of the allowed kind in the index is a null, not a fallback to the wrong kind.</summary>
    [Fact]
    public void NoCandidateOfTheAllowedKindMatchesNothing()
    {
        var index = new IconIndex();

        index.Add("Glade Stalker Trousers", Bits(10), "legs");

        Assert.Null(index.MatchAcrossRenderings(new IconHash(0, 0, 0, 0), ["earring"]));
    }

    private static IconHash Bits(int count)
    {
        var words = new ulong[4];

        for (var i = 0; i < count; i++)
        {
            words[i / 64] |= 1UL << (i % 64);
        }

        return new IconHash(words[0], words[1], words[2], words[3]);
    }
}
