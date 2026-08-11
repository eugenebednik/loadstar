using Loadstar.Core.Capture;
using Loadstar.Games.ThroneAndLiberty;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Set-first equipment identification.
///
/// <para>The properties pinned here are the ones the measurement on a real character sheet established: a
/// set wins on the agreement of several slots rather than on one strong slot, a near-tie between two sets
/// resolves to nothing, and the piece COUNT is never derivable. That last one is the reported bug — advice
/// claiming two set pieces where the player had four — so it is asserted rather than assumed.</para>
/// </summary>
public sealed class GearSetIdentifierTests
{
    private static readonly string[] Armour = ["head", "chest", "hands", "legs", "feet"];

    /// <summary>
    /// A signature that is deterministic in <paramref name="seed"/> and, crucially, varies SPATIALLY per
    /// channel — a uniform tint is divided out by IconSignature's per-channel normalisation, so a fixture
    /// built that way would compare every icon as identical. See IconSignatureTests.
    /// </summary>
    private static IconSignature Signature(int seed)
    {
        const int size = 32;
        var stride = size * Bgra32Image.BytesPerPixel;
        var pixels = new byte[stride * size];

        var cx = 0.30 + (0.40 * ((seed * 7) % 11) / 11.0);
        var cy = 0.30 + (0.40 * ((seed * 5) % 9) / 9.0);

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var i = (y * stride) + (x * Bgra32Image.BytesPerPixel);
                var fx = (x + 0.5) / size;
                var fy = (y + 0.5) / size;
                var inside = Math.Sqrt(((fx - cx) * (fx - cx)) + ((fy - cy) * (fy - cy))) < 0.28;

                // Body and ground carry different channel mixes, so the three planes differ in layout.
                pixels[i] = (byte)(inside ? 40 + (seed * 17 % 200) : 180);
                pixels[i + 1] = (byte)(inside ? 200 : 60 + (seed * 29 % 150));
                pixels[i + 2] = (byte)(inside ? 90 + (seed * 11 % 150) : 120);
                pixels[i + 3] = 255;
            }
        }

        return IconSignature.Compute(new Bgra32Image(pixels, size, size, stride));
    }

    /// <summary>One set whose members are seeded so each slot's own piece is its nearest match.</summary>
    private static List<GearCandidate> Set(string key, int seedBase) =>
        [.. Armour.Select((category, i) => new GearCandidate(
            $"{key}_{category}",
            $"{key} {category}",
            category,
            key,
            key,
            Signature(seedBase + i)))];

    private static List<SlotSignature> Slots(IEnumerable<int> seeds) =>
        [.. seeds.Select((seed, i) => new SlotSignature(Armour[i], [Armour[i]], Signature(seed)))];

    [Fact]
    public void TheSetWhoseMembersMatchEveryTileWins()
    {
        var right = Set("right", 100);
        var wrong = Set("wrong", 500);

        // Tiles are exactly the right set's art, so the right set should win outright.
        var slots = Slots([100, 101, 102, 103, 104]);

        var verdict = GearSetIdentifier.Identify(slots, [.. right, .. wrong]);

        Assert.NotNull(verdict);
        Assert.Equal("right", verdict!.SetKey);
        Assert.True(verdict.MeanSimilarity > verdict.RunnerUpSimilarity);
    }

    /// <summary>
    /// Each tile is assigned its OWN piece rather than whichever member happened to score first, which is
    /// what the greedy pass exists to guarantee.
    /// </summary>
    [Fact]
    public void PiecesLandInTheirOwnSlots()
    {
        var set = Set("kit", 200);
        var verdict = GearSetIdentifier.Identify(Slots([200, 201, 202, 203, 204]), set);

        Assert.NotNull(verdict);

        foreach (var slot in verdict!.Slots)
        {
            Assert.Equal($"kit_{slot.SlotName}", slot.ItemId);
        }
    }

    /// <summary>
    /// THE REFUSAL. Two sets that explain the tiles equally well must produce nothing, because naming one
    /// would be a coin toss the caller cannot see.
    /// </summary>
    [Fact]
    public void TwoEquallyGoodSetsResolveToNothing()
    {
        var first = Set("first", 300);

        // A second set built from the same seeds: identical art under a different key, so the two are
        // genuinely indistinguishable.
        var second = first
            .Select(c => new GearCandidate(
                c.ItemId + "_alt", c.Name + " alt", c.Category, "second", "second", c.Signature))
            .ToList();

        Assert.Null(GearSetIdentifier.Identify(Slots([300, 301, 302, 303, 304]), [.. first, .. second]));
    }

    /// <summary>
    /// A set covering one or two slots cannot win on that alone, however well those slots match — the whole
    /// premise is that agreement across several independent slots is what carries the evidence.
    /// </summary>
    [Fact]
    public void ASetCoveringTooFewSlotsIsNotConsidered()
    {
        var tiny = Set("tiny", 400).Take(2).ToList();
        var broad = Set("broad", 700);

        // Tiles match the tiny set exactly on its two slots, and the broad set only loosely.
        var slots = Slots([400, 401, 902, 903, 904]);

        var verdict = GearSetIdentifier.Identify(slots, [.. tiny, .. broad]);

        Assert.True(verdict is null || verdict.SetKey != "tiny",
            "a two-slot set won despite MinimumSlotsCovered");
    }

    /// <summary>
    /// Slots the winning set cannot fill are reported with a null item rather than left out, so a caller
    /// cannot read a missing entry as an unexamined slot. Most sets have no cloak.
    /// </summary>
    [Fact]
    public void UncoveredSlotsAreReportedRatherThanOmitted()
    {
        var set = Set("kit", 200);
        var slots = Slots([200, 201, 202, 203, 204]);

        slots.Add(new SlotSignature("cloak", ["cloak"], Signature(999)));

        var verdict = GearSetIdentifier.Identify(slots, set);

        Assert.NotNull(verdict);

        var cloak = Assert.Single(verdict!.Slots, s => s.SlotName == "cloak");

        Assert.Null(cloak.ItemId);
        Assert.False(cloak.Confident);
        Assert.Equal(slots.Count, verdict.Slots.Count);
    }

    /// <summary>
    /// The count stays unknown, permanently and by construction. A correct piece scored 0.253 on the measured
    /// sheet while a piece from another set scored 0.627, so no threshold over these numbers can decide
    /// membership — and advice that states a count is the bug this whole design exists to stop repeating.
    /// </summary>
    [Fact]
    public void ThePieceCountIsNeverDerivable()
    {
        var verdict = GearSetIdentifier.Identify(Slots([200, 201, 202, 203, 204]), Set("kit", 200));

        Assert.NotNull(verdict);
        Assert.True(verdict!.CountIsUnknown);
    }

    /// <summary>
    /// Each slot appears EXACTLY once, even when a set holds several items of the same category so multiple
    /// pairs compete for one slot.
    ///
    /// <para>This catches a real bug in the greedy pass: claiming with <c>!taken.Add(a) || !taken.Add(b)</c>
    /// and undoing on failure short-circuits, so an already-taken slot got un-claimed and a worse pair could
    /// take it — producing two assignments for one slot. A set with one item per category cannot expose that,
    /// which is why this fixture deliberately has two.</para>
    /// </summary>
    [Fact]
    public void ASlotIsAssignedOnceEvenWhenSeveralPiecesCompeteForIt()
    {
        // Three head pieces and one of everything else: the head slot has three rival pairs.
        var set = Set("kit", 200);

        set.Add(new GearCandidate("kit_head_b", "kit head b", "head", "kit", "kit", Signature(880)));
        set.Add(new GearCandidate("kit_head_c", "kit head c", "head", "kit", "kit", Signature(881)));

        var verdict = GearSetIdentifier.Identify(Slots([200, 201, 202, 203, 204]), set);

        Assert.NotNull(verdict);

        var names = verdict!.Slots.Select(s => s.SlotName).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(5, names.Count);

        // And the head slot still gets the piece that actually matches it, not a rival that squeezed in.
        Assert.Equal("kit_head", Assert.Single(verdict.Slots, s => s.SlotName == "head").ItemId);
    }

    [Fact]
    public void NothingObservedMeansNoVerdict()
    {
        Assert.Null(GearSetIdentifier.Identify([], Set("kit", 200)));
        Assert.Null(GearSetIdentifier.Identify(Slots([1, 2, 3, 4, 5]), []));
    }
}
