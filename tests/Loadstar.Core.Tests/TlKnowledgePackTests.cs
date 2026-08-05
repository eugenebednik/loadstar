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
}
