using Loadstar.Core.Model;
using Loadstar.Games.ThroneAndLiberty;

using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// The catalogue lookup that turns a build's item ids into names.
///
/// <para>The payload shapes below are copied from a live response for the pinned reference build
/// (questlog build 8344612, "T4 Seeker Magic HAE/END (Aelon Bow)") on 6 August 2026 — including the
/// details that are easy to get wrong from the API docs, which do not exist: item levels live in the KEYS
/// of <c>itemStats.main</c>, food reports level 0, and unfilled slots arrive as an empty id.</para>
/// </summary>
public class ReferenceLookupTests
{
    /// <summary>
    /// Four real entries. Shapes preserved: a T4 armour piece with a 51–80 range and a set, a T3 weapon
    /// that stops at 50, food keyed at level 0, and a talistone with no <c>main</c> block at all.
    /// </summary>
    private static EquipmentCatalog Catalog() => EquipmentCatalog.Parse(CatalogJson);

    private const string CatalogJson =
        """
        {
          "result": { "data": {
            "feet_aa_S1_fabric_002": {
              "id": "feet_aa_S1_fabric_002", "name": "Frigid Melody Shoes",
              "equipmentType": "feet", "grade": 41, "requiredLevel": 1,
              "setId": "set_aa_t4_fabric_002",
              "itemStats": { "main": { "51": {}, "60": {}, "80": {} } }
            },
            "head_aa_S1_fabric_002": {
              "id": "head_aa_S1_fabric_002", "name": "Frigid Melody Hat",
              "equipmentType": "head", "grade": 41, "requiredLevel": 1,
              "setId": "set_aa_t4_fabric_002",
              "itemStats": { "main": { "51": {}, "80": {} } }
            },
            "bow_aa_t3_boss_001": {
              "id": "bow_aa_t3_boss_001", "name": "Grand Aelon's Longbow of Blight",
              "equipmentType": "bow", "grade": 41, "requiredLevel": 1, "setId": null,
              "itemStats": { "main": { "21": {}, "50": {} } }
            },
            "Usable_Food_Result_008_kA": {
              "id": "Usable_Food_Result_008_kA", "name": "Rare BBQ Platter",
              "equipmentType": "attack", "grade": 31, "requiredLevel": 1, "setId": null,
              "itemStats": { "main": { "0": {} } }
            },
            "talistone_a_set_05_001": {
              "id": "talistone_a_set_05_001", "name": "Greedseeker Talistone I",
              "equipmentType": "talistone1", "grade": 31, "requiredLevel": 1,
              "setId": "set_a_artifact_set_005",
              "itemStats": { "main": null }
            }
          } }
        }
        """;

    private static TargetBuild Build(params (string Slot, string ItemId)[] slots) => new()
    {
        Id = "8344612",
        Name = "T4 Seeker Magic HAE/END (Aelon Bow)",
        Source = "questlog",
        WeaponTypes = ["bow", "wand"],
        Equipment = slots.ToDictionary(s => s.Slot, s => new TargetItem { ItemId = s.ItemId }),
    };

    [Fact]
    public void AResolvedIdBecomesANameAndARange()
    {
        var described = TlReferenceLookup.Describe("feet_aa_S1_fabric_002", Catalog());

        Assert.Contains("Frigid Melody Shoes", described);
        Assert.Contains("51", described);
        Assert.Contains("80", described);
    }

    /// <summary>
    /// The reason the range is a range. Every T4 piece reports 80, so printing a single maximum would say
    /// "80" for most of a build and read as its target. The T3 weapon's ceiling of 50 is the number that
    /// actually decides whether a T4 upgrade is worth it, and it only means something next to the floor.
    /// </summary>
    [Fact]
    public void ATierThreeWeaponsCeilingIsVisible()
    {
        var weapon = TlReferenceLookup.Describe("bow_aa_t3_boss_001", Catalog());

        Assert.Contains("Grand Aelon's Longbow of Blight", weapon);
        Assert.Contains("50", weapon);
        Assert.DoesNotContain("80", weapon);
    }

    /// <summary>
    /// Food reports item level 0, which is not a level. Printing "item level 0" next to a sandwich would
    /// invite advice about upgrading it.
    /// </summary>
    [Fact]
    public void FoodGetsANameButNoItemLevel()
    {
        var food = TlReferenceLookup.Describe("Usable_Food_Result_008_kA", Catalog());

        Assert.Contains("Rare BBQ Platter", food);
        Assert.DoesNotContain("item level", food);
    }

    /// <summary>
    /// The rule that matters most: an id the catalogue does not have produces nothing, so the caller's
    /// bare id survives. A plausible wrong name is indistinguishable from a right one until the player
    /// goes looking for an item that does not exist.
    /// </summary>
    [Fact]
    public void AnUnknownIdResolvesToNothingRatherThanAGuess()
    {
        Assert.Equal(string.Empty, TlReferenceLookup.Describe("belt_aa_t9_invented_001", Catalog()));
        Assert.Equal(string.Empty, TlReferenceLookup.Describe(string.Empty, Catalog()));
        Assert.Equal(string.Empty, TlReferenceLookup.Describe("feet_aa_S1_fabric_002", catalog: null));
    }

    /// <summary>
    /// Builds arrive with <c>castle</c>, <c>boonstone</c> and <c>riftstone</c> present but empty when the
    /// author has not filled them. They must not appear as items, or the model gets asked to advise on a
    /// slot that holds nothing.
    /// </summary>
    [Fact]
    public void EmptySlotsAreNotListedAsEquipment()
    {
        var prompt = TlSystemPrompt.Build(
            Build(
                ("main_hand", "bow_aa_t3_boss_001"),
                ("castle", string.Empty),
                ("boonstone", string.Empty),
                ("riftstone", string.Empty)),
            ["pve"],
            catalog: Catalog());

        Assert.Contains("covers 1 slots", prompt);
        Assert.DoesNotContain("- castle:", prompt);
        Assert.DoesNotContain("- riftstone:", prompt);
    }

    /// <summary>
    /// The hedge the catalogue replaces. With names resolved the prompt must stop saying ids are opaque —
    /// an instruction not to translate them, sitting next to lines that translate them, is a contradiction
    /// the model has to pick a side of.
    /// </summary>
    [Fact]
    public void TheOpaqueIdHedgeIsGoneOnceNamesResolve()
    {
        var build = Build(("main_hand", "bow_aa_t3_boss_001"), ("feet", "feet_aa_S1_fabric_002"));

        var resolved = TlSystemPrompt.Build(build, ["pve"], catalog: Catalog());

        Assert.DoesNotContain("opaque", resolved, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Grand Aelon's Longbow of Blight", resolved);
        Assert.Contains("USE THE NAME", resolved);
    }

    /// <summary>
    /// No catalogue is the normal case on a first launch, an offline machine, or a questlog outage. The
    /// prompt must still assemble, still list the ids, and still forbid inventing names for them.
    /// </summary>
    [Fact]
    public void WithoutACatalogueThePromptStillListsTheIds()
    {
        var prompt = TlSystemPrompt.Build(
            Build(("main_hand", "bow_aa_t3_boss_001")), ["pve"], catalog: null);

        Assert.Contains("bow_aa_t3_boss_001", prompt);
        Assert.Contains("NEVER invent a name", prompt);
        Assert.DoesNotContain("Grand Aelon", prompt);
    }

    /// <summary>
    /// The budget the lookup exists to protect. Resolving 27 names is worth roughly a thousand characters;
    /// it must not be the thing that pushes the prompt over.
    /// </summary>
    [Fact]
    public void ResolvingNamesStaysWithinTheBudget()
    {
        var build = Build(
            ("feet", "feet_aa_S1_fabric_002"),
            ("head", "head_aa_S1_fabric_002"),
            ("main_hand", "bow_aa_t3_boss_001"),
            ("attack", "Usable_Food_Result_008_kA"),
            ("talistone1", "talistone_a_set_05_001"));

        var bare = TlSystemPrompt.Build(build, ["pve"]).Length;
        var resolved = TlSystemPrompt.Build(build, ["pve"], catalog: Catalog()).Length;

        Assert.True(resolved > bare, "resolving should add the names");
        Assert.True(
            (resolved - bare) / 4 < 400,
            $"resolution added ~{(resolved - bare) / 4} tokens for 5 slots");
        Assert.True(resolved / 4 < 23_000, $"resolved prompt is ~{resolved / 4} tokens");
    }

    /// <summary>
    /// The failure modes, exercised through the real cache path rather than argued about.
    ///
    /// <para>Each of these throws out of <see cref="EquipmentCatalog.Parse"/> — an outage page has no
    /// <c>result.data</c> and raises <c>InvalidOperationException</c>, an interrupted write leaves a
    /// zero-byte file and raises <c>ArgumentException</c>, and truncated JSON raises <c>JsonException</c>.
    /// None may reach the caller: this runs in the advice path, and losing item NAMES has to degrade to
    /// unresolved ids, never to no answer.</para>
    ///
    /// <para>Offline on purpose. Each case writes a poisoned cache, and the refetch that follows has no
    /// network to reach — so this covers the corrupt-cache branch and the fetch-failure branch together,
    /// which is exactly the pairing that happens on a plane.</para>
    /// </summary>
    [Theory]
    [InlineData("", "an interrupted write")]
    [InlineData("   ", "whitespace only")]
    [InlineData("{\"error\":{\"message\":\"UNAUTHORIZED\"}}", "an API error object")]
    [InlineData("<!doctype html><title>502 Bad Gateway</title>", "an outage page")]
    [InlineData("{\"result\":{\"data\":", "truncated JSON")]
    [InlineData("{\"result\":{\"data\":[]}}", "an array where an object belongs")]
    public async Task AnUnusableCatalogueDegradesInsteadOfThrowing(string poison, string because)
    {
        var directory = Path.Combine(Path.GetTempPath(), "loadstar-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, "equipment-catalog.cache.json"), poison);

            using var http = new HttpClient(new OfflineHandler()) { Timeout = TimeSpan.FromSeconds(5) };

            var catalog = await new QuestlogClient(http)
                .GetEquipmentCatalogAsync(directory, CancellationToken.None);

            Assert.Null(catalog);
            Assert.True(catalog is null, $"{because} must not produce a catalogue");

            // And the prompt still assembles, with the ids intact and the do-not-invent rule in force.
            var prompt = TlSystemPrompt.Build(
                Build(("main_hand", "bow_aa_t3_boss_001")), ["pve"], catalog: catalog);

            Assert.Contains("bow_aa_t3_boss_001", prompt);
            Assert.Contains("NEVER invent a name", prompt);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The positive control the theory above needs. Every case there asserts null, which a method that
    /// always returned null would also satisfy — so one case has to prove the cache path actually works.
    ///
    /// <para>It doubles as the offline requirement: a fresh cache and no network must still resolve names,
    /// because a month-long cache whose only purpose is avoiding a 10.4 MB refetch is worthless if it
    /// cannot be read without one.</para>
    /// </summary>
    [Fact]
    public async Task AValidCacheResolvesWithNoNetwork()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loadstar-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, "equipment-catalog.cache.json"), CatalogJson);

            using var http = new HttpClient(new OfflineHandler()) { Timeout = TimeSpan.FromSeconds(5) };

            var catalog = await new QuestlogClient(http)
                .GetEquipmentCatalogAsync(directory, CancellationToken.None);

            Assert.NotNull(catalog);
            Assert.Equal(5, catalog.Count);
            Assert.Equal("Frigid Melody Shoes", catalog.Find("feet_aa_S1_fabric_002")?.Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>No network, the way a machine with the game open but no connection behaves.</summary>
    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No such host is known.");
    }
}
