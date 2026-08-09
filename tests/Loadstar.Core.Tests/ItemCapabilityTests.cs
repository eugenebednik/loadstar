using Loadstar.Games.ThroneAndLiberty;

using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// What the catalogue says a gear piece is CAPABLE of, as opposed to what it currently carries.
///
/// <para>The payload shapes are copied from a live response for Frigid Melody Greaves on 8 August 2026,
/// including the two that are easy to get wrong: <c>main</c> nests its values under armor/shield/mainhand
/// groups that are mostly null, while a sibling <c>extra</c> block is keyed by level directly.</para>
/// </summary>
public class ItemCapabilityTests
{
    private static EquipmentCatalog Catalog() => EquipmentCatalog.Parse(
        """
        {
          "result": { "data": {
            "legs_aa_S1_fabric_002": {
              "id": "legs_aa_S1_fabric_002", "name": "Frigid Melody Greaves",
              "equipmentType": "legs", "grade": 41, "requiredLevel": 1, "setId": "set_aa_t4_fabric_002",
              "itemStats": {
                "main": {
                  "51": { "armor": { "melee_armor": 332, "range_armor": 298 },
                          "extra": null, "shield": null, "offhand": null, "mainhand": null },
                  "80": { "armor": { "melee_armor": 470, "range_armor": 421 },
                          "extra": null, "shield": null, "offhand": null, "mainhand": null }
                },
                "extra": {
                  "51": { "con": 5, "dex": 6, "hp_max": 500 },
                  "80": { "con": 7, "dex": 8, "hp_max": 1040 }
                },
                "traits": {
                  "hp_max": [150, 300, 450, 600],
                  "magic_evasion": [400, 800, 1200, 1600],
                  "melee_evasion": [400, 800, 1200, 1600],
                  "debuff_taken_duration_modifier": [-150, -300, -450, -600]
                },
                "resonance": {
                  "hp_max": { "tiers": [260, 390, 470, 520], "probability": 14 },
                  "cost_max": { "tiers": [260, 390, 470, 520], "probability": 21 }
                }
              }
            },
            "belt_plain": {
              "id": "belt_plain", "name": "Plain Belt", "equipmentType": "belt", "grade": 21,
              "requiredLevel": 1, "setId": null, "itemStats": { "main": null }
            }
          } }
        }
        """);

    /// <summary>
    /// Floor and ceiling only. Thirty levels of interpolation nobody asks about would be thirty times the
    /// memory for the same two useful numbers.
    /// </summary>
    [Fact]
    public void StatsAreReadAtBothEndsOfTheLevelRange()
    {
        var legs = Catalog().Find("legs_aa_S1_fabric_002")!;

        Assert.Equal(332, legs.StatsAtFloor["melee_armor"]);
        Assert.Equal(470, legs.StatsAtCeiling["melee_armor"]);
        Assert.Equal(500, legs.StatsAtFloor["hp_max"]);
        Assert.Equal(1040, legs.StatsAtCeiling["hp_max"]);
    }

    /// <summary>
    /// The nesting is the trap: main's values sit under armor/shield/mainhand groups, most of them null on any
    /// given item, and the sibling extra block is keyed by level directly. Both are flattened, because a
    /// caller wants the piece's stats rather than a tour of the payload.
    /// </summary>
    [Fact]
    public void NestedAndSiblingStatBlocksAreBothFlattened()
    {
        var legs = Catalog().Find("legs_aa_S1_fabric_002")!;

        Assert.Contains("range_armor", legs.StatsAtFloor.Keys);
        Assert.Contains("con", legs.StatsAtFloor.Keys);
    }

    /// <summary>Traits are valued at the fourth pip, which is the ceiling a trait can be levelled to.</summary>
    [Fact]
    public void TraitOptionsCarryTheFinalPipValue()
    {
        var legs = Catalog().Find("legs_aa_S1_fabric_002")!;

        Assert.Equal(4, legs.TraitOptions.Count);
        Assert.Equal(600, legs.TraitOptions["hp_max"]);
        Assert.Equal(1600, legs.TraitOptions["magic_evasion"]);
    }

    /// <summary>Traits can be negative — a reduced debuff duration is a benefit expressed as a minus.</summary>
    [Fact]
    public void ANegativeTraitSurvives() =>
        Assert.Equal(-600, Catalog().Find("legs_aa_S1_fabric_002")!.TraitOptions["debuff_taken_duration_modifier"]);

    /// <summary>
    /// Resonance is ROLLED rather than chosen, and opening a slot costs 1,500,000 Sollant plus three stones.
    /// A resonance named without its odds is a lottery presented as a purchase.
    /// </summary>
    [Fact]
    public void ResonanceCarriesItsOdds()
    {
        var legs = Catalog().Find("legs_aa_S1_fabric_002")!;

        Assert.Equal(520, legs.ResonanceOptions["hp_max"].TopTier);
        Assert.Equal(14, legs.ResonanceOptions["hp_max"].ProbabilityPercent);
        Assert.Equal(21, legs.ResonanceOptions["cost_max"].ProbabilityPercent);
    }

    /// <summary>An item with no stat block parses to empty maps rather than throwing.</summary>
    [Fact]
    public void AnItemWithoutStatsIsEmptyNotBroken()
    {
        var belt = Catalog().Find("belt_plain")!;

        Assert.Empty(belt.StatsAtFloor);
        Assert.Empty(belt.TraitOptions);
        Assert.Empty(belt.ResonanceOptions);
        Assert.Equal(string.Empty, TlReferenceLookup.DescribeCapability(belt.Id, Catalog()));
    }

    /// <summary>
    /// The actionable part: the traits the build has NOT taken. Since 4.0.0 gear drops with no traits at all
    /// and they are unlocked with stones, so the catalogue's list is the menu and the build's is a selection
    /// from it — the difference is a priceable action on gear the player already wears.
    /// </summary>
    [Fact]
    public void OnlyTheUntakenTraitsAreNamed()
    {
        var line = TlReferenceLookup.DescribeCapability(
            "legs_aa_S1_fabric_002", Catalog(), ["hp_max", "magic_evasion"]);

        Assert.Contains("2 of 4 traits free", line);
        Assert.Contains("melee_evasion", line);
        Assert.DoesNotContain("magic_evasion,", line);
    }

    /// <summary>A piece with nothing left to unlock says so, rather than listing an empty set.</summary>
    [Fact]
    public void APieceWithEverythingTakenSaysSo()
    {
        var line = TlReferenceLookup.DescribeCapability(
            "legs_aa_S1_fabric_002",
            Catalog(),
            ["hp_max", "magic_evasion", "melee_evasion", "debuff_taken_duration_modifier"]);

        Assert.Contains("all 4 trait options taken", line);
    }

    /// <summary>
    /// The headline stat is shown as floor to ceiling, so the headroom in a slot is a subtraction. It is the
    /// number that decides whether spending on a piece can achieve anything at all.
    /// </summary>
    [Fact]
    public void TheHeadlineStatShowsItsRange()
    {
        var line = TlReferenceLookup.DescribeCapability("legs_aa_S1_fabric_002", Catalog());

        Assert.Contains("1040", line);
        Assert.Contains("500", line);
    }

    /// <summary>An unknown id contributes nothing rather than a fabricated capability.</summary>
    [Fact]
    public void AnUnknownItemContributesNothing() =>
        Assert.Equal(string.Empty, TlReferenceLookup.DescribeCapability("not_a_real_item", Catalog()));
}
