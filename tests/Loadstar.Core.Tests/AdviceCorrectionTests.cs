using Loadstar.Core.Model;
using Loadstar.Games.ThroneAndLiberty;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Three corrections that came from the advice engine getting things wrong in the field. Each one read
/// as precise and confident, which is what made it damaging — so each is pinned here rather than left
/// to survive on prose alone.
/// </summary>
public sealed class AdviceCorrectionTests
{
    private static TargetBuild BuildWithEquipment => new()
    {
        Id = "8344612",
        Name = "T4 Seeker Magic HAE/END (Aelon Bow)",
        Source = "questlog",
        Tags = ["pve"],
        WeaponTypes = ["bow", "wand"],
        Equipment = new Dictionary<string, TargetItem>
        {
            ["legs"] = new() { ItemId = "legs_fabric_aa_t3_normal_001" },
            ["belt"] = new() { ItemId = "belt_aa_S1_003" },
        },
    };

    /// <summary>
    /// ONLY ALLOCATED POINTS MOVE. The engine told the player to take points out of Fortitude 71 and
    /// Dexterity 86; they had allocated nothing to Fortitude, so it could not be lowered at all.
    /// </summary>
    [Fact]
    public void StatRulesSayOnlyTheAllocatedPartIsMovable()
    {
        var prompt = TlSystemPrompt.Build(null, []);

        Assert.Contains("total = 10 (everyone's floor) + allocated + equipment + Stellar Journey",
            prompt, StringComparison.Ordinal);
        Assert.Contains("Only `allocated` can be moved", prompt, StringComparison.Ordinal);

        // The floor is the part that makes a stat immovable, and it has to be stated as such.
        Assert.Contains("EVERY STAT HAS A FLOOR", prompt, StringComparison.Ordinal);

        // And the hard rule that would have prevented the bad answer.
        Assert.Contains("DO NOT propose a specific move", prompt, StringComparison.Ordinal);
        Assert.Contains("excess points", prompt, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// THE WATERMARK IS ABOUT DROPS, NOT WORN GEAR. The engine claimed two equipped level-50 pieces were
    /// holding the watermark back; per-slot levels have nothing to do with it.
    /// </summary>
    [Fact]
    public void WatermarkRulesForbidInferringItFromEquippedSlots()
    {
        var prompt = TlSystemPrompt.Build(null, []);

        Assert.Contains("EVER DROPPED", prompt, StringComparison.Ordinal);
        Assert.Contains("CANNOT read the watermark off the equipment slots", prompt, StringComparison.Ordinal);

        // The drop curve, which is what makes "raise your watermark" priceable rather than glib.
        Assert.Contains("79 → 80", prompt, StringComparison.Ordinal);
        Assert.Contains("5%", prompt, StringComparison.Ordinal);

        // Marked as community data rather than published rates.
        Assert.Contains("community guide, not official notes", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// AN ITEM IN THE BUILD IS A DESTINATION. Two level-50 pieces were flagged as "lagging" when both
    /// were deliberate best-in-slot choices in the player's own build.
    ///
    /// <para>Only appears when a build is actually pinned — there is nothing to defer to otherwise.</para>
    /// </summary>
    [Fact]
    public void APinnedBuildsItemsAreNotTreatedAsGaps()
    {
        var prompt = TlSystemPrompt.Build(BuildWithEquipment, ["pve"]);

        Assert.Contains("AN ITEM IN THIS LIST IS A DESTINATION. NEVER CALL IT A GAP.",
            prompt, StringComparison.Ordinal);
        Assert.Contains("A low item level is not evidence of a bad item", prompt, StringComparison.Ordinal);

        // The crossover thresholds must be explicitly scoped to generic pieces, or they get applied
        // over a named item again.
        Assert.Contains("51 / 61 / 71", prompt, StringComparison.Ordinal);
        Assert.Contains("do not override a named item in a build", prompt, StringComparison.Ordinal);

        // With no build pinned there is nothing to defer to, so the section stays out.
        Assert.DoesNotContain("NEVER CALL IT A GAP", TlSystemPrompt.Build(null, []), StringComparison.Ordinal);
    }

    /// <summary>
    /// The reply came back as Russian prose with English category labels. The contract now says which
    /// fields are shown to the player and which are parsed, because that distinction is the whole cause.
    /// </summary>
    [Fact]
    public void OutputContractSeparatesPlayerFacingFieldsFromParsedOnes()
    {
        var prompt = TlSystemPrompt.Build(null, []);

        Assert.Contains("Shown to the player, so they go in the reply language", prompt, StringComparison.Ordinal);
        Assert.Contains("Parsed by code", prompt, StringComparison.Ordinal);

        // `category` is the field that actually came back wrong.
        Assert.Contains("`category` is", prompt, StringComparison.Ordinal);
        Assert.Contains("[Stat Points]", prompt, StringComparison.Ordinal);
    }

    /// <summary>The corrections must reach the embedded knowledge pack too, not only the prompt prose.</summary>
    [Theory]
    [InlineData("Only the ALLOCATED points are redistributable")]
    [InlineData("ever DROPPED")]
    [InlineData("cannot read the watermark off the equipment slots")]
    public void KnowledgePackCarriesTheCorrections(string fragment)
    {
        Assert.Contains(fragment, TlKnowledgePack.Text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The two research findings that change what advice is even POSSIBLE for a given player, rather
    /// than merely informing it. Both are about availability, and getting them wrong produces advice the
    /// player cannot act on at all.
    /// </summary>
    [Theory]
    // Guild level is the whole guild's accumulated daily activity, so a solo player cannot fix it.
    [InlineData("a player cannot fix this themselves")]
    // Two guilds per boonstone war, guilds only.
    [InlineData("it is scenery")]
    // And a boonstone buff inflates the character sheet without labelling itself.
    [InlineData("INFLATES THE CHARACTER SHEET")]
    // Dimensional Trials use tiers; stars belong to the older co-op dungeons.
    [InlineData("TIERS, not stars")]
    // The arena normalises gear, so gear advice is the wrong answer there.
    [InlineData("Equalized")]
    public void AvailabilityFindingsReachTheKnowledgePack(string fragment)
    {
        Assert.Contains(fragment, TlKnowledgePack.Text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Material sources, so a cost can be named with somewhere to get it. "You need three Growthstones"
    /// is not actionable until the player knows what to run.
    /// </summary>
    [Theory]
    [InlineData("Quality → Rare → Precious → Epic", "the Growthstone rarity ladder")]
    [InlineData("SENIOR crafter", "Epic stones need a senior crafter, not any city")]
    [InlineData("Morphstones and Growthstones", "the Codex is a materials source, not just story")]
    [InlineData("Rune Chance Chest", "Dimensional Trials are where rune volume comes from")]
    public void MaterialSourcesReachTheKnowledgePack(string fragment, string why)
    {
        // Whitespace-normalised, because these documents are hard-wrapped prose and a phrase can land
        // across a line break — "Rune Chance Chest" did. A fragment test that depends on where a line
        // happens to wrap fails for a reason that has nothing to do with the fact being present.
        Assert.True(Flattened.Contains(Flatten(fragment), StringComparison.OrdinalIgnoreCase), why);
    }

    /// <summary>The pack with every run of whitespace collapsed to one space, for fragment matching.</summary>
    private static readonly string Flattened = Flatten(TlKnowledgePack.Text);

    private static string Flatten(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

    /// <summary>
    /// Every gap file must keep saying what it does NOT know. The pack's value depends on a reader being
    /// able to tell a measurement from an absence, and an unmarked gap reads as completeness.
    /// </summary>
    [Fact]
    public void ThePackKeepsDeclaringItsOwnGaps()
    {
        Assert.Contains("Still not captured", TlKnowledgePack.Text, StringComparison.Ordinal);
        Assert.Contains("Searched and NOT found", TlKnowledgePack.Text, StringComparison.Ordinal);
        Assert.Contains("is not published", TlKnowledgePack.Text, StringComparison.Ordinal);
    }
}
