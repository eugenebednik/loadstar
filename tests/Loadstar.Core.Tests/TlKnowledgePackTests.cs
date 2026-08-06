using Loadstar.Core.Model;
using Loadstar.Games.ThroneAndLiberty;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Guards the mechanics reference that gets embedded into the system prompt.
///
/// <para>The failure this exists to catch is silent: if the embedded resources stop being included,
/// the app still builds, still runs, still answers — just with no game knowledge, giving generic
/// advice that looks fine. Exactly the shape of the bug where the posture scanner's glob quietly
/// skipped the application assembly.</para>
/// </summary>
public sealed class TlKnowledgePackTests
{
    [Fact]
    public void KnowledgeFilesAreActuallyEmbedded()
    {
        Assert.NotEmpty(TlKnowledgePack.Sections);
        Assert.True(
            TlKnowledgePack.EstimatedTokens > 1000,
            $"knowledge pack is only ~{TlKnowledgePack.EstimatedTokens} tokens — it is probably not loading");
    }

    [Theory]
    // Facts that were wrong or missing in published guides, so their presence proves the pack is
    // the corrected version rather than something generic.
    [InlineData("120", "rune level cap — guides say 60")]
    [InlineData("Chaos rune counts as any type", "chaos wildcard behaviour")]
    [InlineData("8,000 per week", "Flame of Purification cap, the real Redfrost constraint")]
    [InlineData("220 points maximum per weapon", "mastery ceiling")]
    [InlineData("totals across ALL weapons", "the cross-weapon threshold insight")]
    [InlineData("sealing the item resets the counter", "inheritance counter is recoverable")]
    [InlineData("no traits", "gear arrives without traits")]
    [InlineData("base = 10 + allocated", "allocated-vs-total conversion")]
    [InlineData("PvP buys accuracy", "the measured build axis")]
    public void PackCarriesTheCorrectedFacts(string fragment, string why)
    {
        Assert.True(TlKnowledgePack.Text.Contains(fragment, StringComparison.OrdinalIgnoreCase), why);
    }

    [Fact]
    public void PackKeepsItsHedgesRatherThanStatingUnverifiedThingsFlatly()
    {
        // Several facts are player-reported or from stale guides. The pack must carry the same
        // uncertainty the data has, or the model will present guesses as fact.
        Assert.Contains("UNVERIFIED", TlKnowledgePack.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not quote different rates", TlKnowledgePack.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void PackIsByteStableAcrossCalls()
    {
        // The prompt is the cached prefix. Any instability here silently costs full price on every
        // request, so this is a cost regression test as much as a correctness one.
        Assert.Equal(TlKnowledgePack.Text, TlKnowledgePack.Text);
        Assert.Same(TlKnowledgePack.Text, TlKnowledgePack.Text);
    }

    [Fact]
    public void SystemPromptEmbedsThePackAndStaysWithinABudget()
    {
        var build = new TargetBuild { Id = "b", Name = "Test", Source = "questlog.gg" };

        var prompt = TlSystemPrompt.Build(build, ["pve", "healer"]);

        Assert.Contains(TlKnowledgePack.Text, prompt, StringComparison.Ordinal);

        // Not a hard limit so much as a tripwire. The pack is cheap because it is cached, but an
        // unbounded prompt dilutes attention and makes the model worse at the specific rules that
        // matter. If this fires, the answer is usually to move data into a local lookup rather than
        // to raise the number.
        var tokens = prompt.Length / 4;
        Assert.True(tokens < 25_000, $"system prompt has grown to ~{tokens} tokens; review what belongs in a lookup instead");
    }

    [Fact]
    public void SystemPromptRemainsFreeOfAnythingVolatile()
    {
        var build = new TargetBuild { Id = "b", Name = "Test", Source = "questlog.gg" };

        // A clock, a session id or a counter anywhere in the prefix invalidates the cache on every
        // single request. SystemPromptBuilder has no access to the time, deliberately.
        var first = TlSystemPrompt.Build(build, ["pve"]);
        var second = TlSystemPrompt.Build(build, ["pve"]);

        Assert.Equal(first, second);
        Assert.DoesNotContain(DateTime.UtcNow.Year.ToString() + "-", first, StringComparison.Ordinal);
    }

    /// <summary>
    /// The per-class profiles are measured data, so what matters is that the pack carries the CAVEATS
    /// with the numbers. A share of trait picks across strangers' builds reads exactly like a stat
    /// target, and the model will present it as one unless told not to.
    /// </summary>
    [Theory]
    [InlineData("NOT as a target", "the framing that stops a measurement becoming a benchmark")]
    [InlineData("questlog is unmoderated", "why popularity is not proof")]
    [InlineData("Popularity is self-reinforcing", "the copying feedback loop")]
    [InlineData("Author tags are unusable", "the negative finding — 11% coverage, 12 classes with none")]
    [InlineData("Ask the player their axis", "what to do instead of inferring PvE/PvP from a class")]
    [InlineData("A trait at 1.0x is the meta, not the class", "lift over baseline, not raw frequency")]
    [InlineData("too few to characterise", "classes below threshold are marked, not filled in")]
    public void ClassProfilesShipWithTheirCaveats(string fragment, string why)
    {
        Assert.True(TlKnowledgePack.Text.Contains(fragment, StringComparison.Ordinal), why);
    }

    /// <summary>
    /// Every class must appear, or the model will silently have nothing to say about the ones missing —
    /// and the classes most likely to be dropped are the newest, which are the ones no community guide
    /// covers either.
    /// </summary>
    [Fact]
    public void EveryClassAppearsInTheProfiles()
    {
        foreach (var name in TlClasses.All)
        {
            Assert.True(
                TlKnowledgePack.Text.Contains($"### {name} —", StringComparison.Ordinal),
                $"{name} has no profile section");
        }
    }

    /// <summary>
    /// The three near-universal traits must be named as such. Without the baseline, a per-class
    /// frequency of 85% reads as a strong class signal when it is just the game's meta.
    /// </summary>
    [Fact]
    public void ProfilesNameTheUniversalTraitsSoTheyAreNotMistakenForSignal()
    {
        foreach (var universal in new[] { "all_double_attack", "all_accuracy", "all_critical_attack" })
        {
            Assert.Contains(universal, TlKnowledgePack.Text, StringComparison.Ordinal);
        }

        // And the stat baseline, for the same reason: Perception leads everywhere, so a class leading
        // on Perception is not deviating.
        Assert.Contains("Perception dominates everywhere", TlKnowledgePack.Text, StringComparison.Ordinal);
    }
}
