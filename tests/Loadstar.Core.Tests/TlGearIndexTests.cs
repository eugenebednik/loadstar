using Loadstar.Core.Capture;
using Loadstar.Games.ThroneAndLiberty;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// The shipped gear index.
///
/// <para>These exist because the index is an embedded resource, and the failure mode of a missing or
/// mismatched one is <b>silent</b>: identification simply returns null forever and the app looks like it
/// never had the feature. A build that drops the resource, or a descriptor change that invalidates it, has to
/// break a test rather than quietly stop working.</para>
/// </summary>
public sealed class TlGearIndexTests
{
    [Fact]
    public void TheIndexIsEmbeddedAndLoads()
    {
        Assert.NotEmpty(TlGearIndex.All);

        // Roughly what a character sheet's twelve categories hold. Asserted as a floor rather than an exact
        // count, since a patch legitimately adds items — but an index that fell to a handful is broken.
        Assert.True(TlGearIndex.All.Count > 700,
            $"the index carries only {TlGearIndex.All.Count} items, which is too few to be complete");
    }

    [Fact]
    public void EverySignatureMatchesTheCurrentDescriptor()
    {
        var expected = IconSignature.Grid * IconSignature.Grid * 3;

        // A length mismatch means the index was built by a different version of IconSignature, so its numbers
        // are not comparable to a freshly computed one. TlGearIndex refuses such a file wholesale; this
        // asserts the shipped one is not that file.
        Assert.All(TlGearIndex.All, candidate =>
            Assert.Equal(expected, candidate.Signature.Length));
    }

    [Fact]
    public void ItemsCarryTheFieldsIdentificationNeeds()
    {
        Assert.All(TlGearIndex.All, candidate =>
        {
            Assert.False(string.IsNullOrWhiteSpace(candidate.ItemId));
            Assert.False(string.IsNullOrWhiteSpace(candidate.Name));
            Assert.False(string.IsNullOrWhiteSpace(candidate.Category));
        });

        // Set grouping is what the whole approach rests on, so most items must have a key. Not all: an item
        // with a one-word name and no setId legitimately has nothing to group by.
        var grouped = TlGearIndex.All.Count(c => c.SetKey is not null);

        Assert.True(grouped > TlGearIndex.All.Count / 2,
            $"only {grouped} of {TlGearIndex.All.Count} items have a set key");
    }

    /// <summary>
    /// The known set is present with all five of its pieces. This is the ground truth the whole design was
    /// measured against, so its absence would invalidate every number recorded in CLAUDE.md.
    /// </summary>
    [Fact]
    public void TheReferenceSetIsPresentInFull()
    {
        var frigid = TlGearIndex.All
            .Where(c => c.Name.Contains("Frigid Melody", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Equal(5, frigid.Count);

        // All five share one key, which is what lets them be scored as a set rather than five loose items.
        Assert.Single(frigid.Select(c => c.SetKey).Distinct(StringComparer.Ordinal));

        foreach (var slot in new[] { "head", "chest", "hands", "legs", "feet" })
        {
            Assert.Contains(frigid, c => string.Equals(c.Category, slot, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// An image with no slot grid produces no verdict. Identification runs over every queued screen, so a
    /// world-view capture reaching it is the normal case, not an edge one — and a set named from open ground
    /// would be pure invention.
    /// </summary>
    [Fact]
    public void AnImageWithNoSlotGridIdentifiesNothing()
    {
        const int size = 200;
        var stride = size * Bgra32Image.BytesPerPixel;
        var pixels = new byte[stride * size];

        for (var i = 0; i < pixels.Length; i += Bgra32Image.BytesPerPixel)
        {
            pixels[i] = 90;
            pixels[i + 1] = 70;
            pixels[i + 2] = 60;
            pixels[i + 3] = 255;
        }

        Assert.Null(TlGearIndex.Identify(new Bgra32Image(pixels, size, size, stride)));
    }

    [Fact]
    public void DescribeSaysNothingWithoutAVerdict() => Assert.Null(TlGearIndex.Describe(null));

    /// <summary>
    /// The description must state that the count is unknown. That sentence is the fix for the reported bug —
    /// advice claiming two set pieces where the player had four — so it is asserted rather than trusted.
    /// </summary>
    [Fact]
    public void DescribeRefusesToImplyAPieceCount()
    {
        var verdict = new GearVerdict(
            "set_x",
            "Example Set",
            1.2,
            0.7,
            [new SlotAssignment("head", "x_head", "Example Hat", 1.5, true),
             new SlotAssignment("cloak", null, null, 0, false)]);

        var text = TlGearIndex.Describe(verdict);

        Assert.NotNull(text);
        Assert.Contains("Example Set", text);
        Assert.Contains("NOT known", text, StringComparison.Ordinal);
        Assert.Contains("tooltip", text, StringComparison.OrdinalIgnoreCase);

        // A slot the set cannot fill must read as "something else", never as an unnamed guess.
        Assert.Contains("something else", text, StringComparison.OrdinalIgnoreCase);
    }
}
