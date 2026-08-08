using Loadstar.Games.ThroneAndLiberty;

using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Suggesting builds when the player has pinned none: roles read from tags, and the equipment grid order.
/// </summary>
public class BuildSuggestionTests
{
    private static BuildCandidate Build(
        string name, string[] tags, int likes30 = 0, int likes = 0, int daysOld = 1) => new()
    {
        Slug = name.Replace(' ', '-'),
        Name = name,
        Tags = tags,
        LikesLast30Days = likes30,
        Likes = likes,
        UpdatedAt = DateTimeOffset.UtcNow.AddDays(-daysOld),
        WeaponTypes = ["bow", "wand"],
    };

    /// <summary>
    /// Roles come from the author's tags, never from a table of what each class can do. There are 45 weapon
    /// pairs and no published roster, so a table would be invention — and the tags reproduce the game
    /// correctly anyway: a live query returned healer and dps for bow+wand, tank and dps for sword+gauntlet,
    /// and dps alone for dagger+greatsword.
    /// </summary>
    [Theory]
    [InlineData("healer", "healer")]
    [InlineData("tank", "tank")]
    [InlineData("dps", "dps")]
    [InlineData("support", "support")]
    public void TheRoleIsReadFromTheTags(string tag, string expected) =>
        Assert.Equal(expected, Build("x", [tag, "pve"]).Role);

    /// <summary>An untagged build claims no role rather than being assumed to be DPS.</summary>
    [Fact]
    public void AnUntaggedBuildHasNoRole() => Assert.Null(Build("x", ["endgame-build"]).Role);

    /// <summary>
    /// Healer outranks dps when both are present, because a build tagged both is a healer build that can
    /// also do damage rather than the other way round.
    /// </summary>
    [Fact]
    public void HealerWinsOverDpsWhenBothAreTagged() =>
        Assert.Equal("healer", Build("x", ["dps", "healer", "pve"]).Role);

    /// <summary>
    /// Recency is the trust signal. A build keeps its lifetime likes across a patch that rewrote the item
    /// system, so age has to be visible independently of popularity.
    /// </summary>
    [Fact]
    public void RecencyIsIndependentOfPopularity()
    {
        Assert.True(Build("fresh", ["pve"], likes: 3, daysOld: 5).IsRecent);
        Assert.False(Build("stale", ["pve"], likes: 9000, daysOld: 200).IsRecent);
    }

    [Fact]
    public void AnUndatedBuildIsNotAssumedRecent()
    {
        var candidate = Build("x", ["pve"]) with { UpdatedAt = null };

        Assert.False(candidate.IsRecent);
    }

    /// <summary>The URL is built from the slug, so it can be quoted rather than composed by the model.</summary>
    [Fact]
    public void TheUrlIsDerivedFromTheSlug() =>
        Assert.Equal(
            "https://questlog.gg/throne-and-liberty/en/character-builder/T4-Seeker",
            Build("T4 Seeker", ["pve"]).Url);

    /// <summary>
    /// The grid order as stated by the product owner: head, cloak, chest, gloves, trousers, shoes, necklace,
    /// bracelet, ring, ring, earrings, belt, brooch — top to bottom, left to right.
    /// </summary>
    [Theory]
    [InlineData(0, "head")]
    [InlineData(1, "cloak")]
    [InlineData(3, "hands")]
    [InlineData(4, "legs")]
    [InlineData(7, "bracelet")]
    [InlineData(8, "ring")]
    [InlineData(9, "ring")]
    [InlineData(10, "earring")]
    [InlineData(12, "brooch")]
    public void TheGridOrderIsExact(int index, string expected)
    {
        Assert.Equal(expected, TlEquipmentLayout.SlotNameForIndex(index));
        Assert.Equal([expected], TlEquipmentLayout.CategoriesForIndex(index));
    }

    [Fact]
    public void ThereAreThirteenSlots() => Assert.Equal(13, TlEquipmentLayout.Order.Count);

    /// <summary>
    /// The two identifications the icon matcher had already produced, graded against the confirmed order.
    /// Both land in the slot their category belongs to, which is what turned them from plausible into
    /// checked.
    /// </summary>
    [Fact]
    public void TheVerifiedMatchesLandInTheRightSlots()
    {
        Assert.Equal("bracelet", TlEquipmentLayout.SlotNameForIndex(7));
        Assert.Equal("ring", TlEquipmentLayout.SlotNameForIndex(8));
    }

    /// <summary>
    /// Off-grid degrades to an unconstrained search, never to an empty candidate set. An index into the
    /// wrong list guarantees a miss; no index merely forgoes a filter.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(13)]
    [InlineData(99)]
    public void AnOutOfRangeIndexAllowsEverything(int index)
    {
        Assert.Null(TlEquipmentLayout.SlotNameForIndex(index));
        Assert.Equal(TlEquipmentLayout.Everything.Count, TlEquipmentLayout.CategoriesForIndex(index).Count);
    }

    /// <summary>Nothing in this grid is ever a weapon, a consumable or an artifact.</summary>
    [Theory]
    [InlineData("bow")]
    [InlineData("sword2h")]
    [InlineData("attack")]
    [InlineData("talistone1")]
    [InlineData("stellarite")]
    public void TheGridNeverHoldsSomethingUnwearable(string category) =>
        Assert.DoesNotContain(category, TlEquipmentLayout.Everything);
}
