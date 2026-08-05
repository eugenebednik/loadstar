using Loadstar.Core.Ai;
using Loadstar.Core.Configuration;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Guards the provider catalogue and the model resolution around it.
///
/// <para>The failures worth testing here are the quiet ones. A provider missing from the catalogue
/// throws deep inside a settings dialog; a stale model id sent to the wrong provider comes back as
/// a 404 naming a model the user never chose; a priced model with only half its rates filled in
/// produces a cost estimate that is wrong rather than absent. None of those announce themselves.</para>
/// </summary>
public sealed class AiCatalogTests
{
    [Fact]
    public void Every_provider_kind_has_a_catalogue_entry()
    {
        // AiProviderKind is what settings persist and what the factory switches on. A kind without
        // an entry compiles fine and throws only when someone selects it.
        foreach (var kind in Enum.GetValues<AiProviderKind>())
        {
            var info = AiCatalog.For(kind);

            Assert.NotEmpty(info.Models);
            Assert.NotEmpty(info.DisplayName);
            Assert.NotEmpty(info.EnvironmentVariable);
        }
    }

    [Fact]
    public void Every_offered_model_supports_vision()
    {
        // The entire input to this app is a screenshot, so a text-only model cannot do anything at
        // all. Offering one would fail mid-request and read like a capture bug.
        foreach (var kind in Enum.GetValues<AiProviderKind>())
        {
            Assert.All(AiCatalog.UsableModels(kind), model => Assert.True(model.SupportsVision));
        }
    }

    [Fact]
    public void Default_model_is_one_the_provider_actually_lists()
    {
        foreach (var kind in Enum.GetValues<AiProviderKind>())
        {
            Assert.NotNull(AiCatalog.FindModel(kind, AiCatalog.For(kind).DefaultModel));
        }
    }

    [Fact]
    public void Prices_are_either_complete_or_absent()
    {
        // Half a price is worse than none: it silently prices one side of the call at zero, so the
        // estimate comes out low and a spend ceiling built on it would not hold.
        foreach (var kind in Enum.GetValues<AiProviderKind>())
        {
            Assert.All(
                AiCatalog.For(kind).Models,
                model => Assert.Equal(
                    model.InputUsdPerMillion.HasValue,
                    model.OutputUsdPerMillion.HasValue));
        }
    }

    [Fact]
    public void Cost_is_null_when_the_model_is_unpriced()
    {
        // Null, not zero. Zero reads as "this call was free", which would let a budget be breached
        // without anything looking wrong.
        Assert.Null(AiCatalog.EstimateCostUsd(AiProviderKind.Anthropic, "some-future-model", 1000, 1000));
    }

    [Fact]
    public void Cost_uses_per_million_rates()
    {
        // Opus 5 at $5 in / $25 out: 1M input + 1M output = $30.
        var cost = AiCatalog.EstimateCostUsd(AiProviderKind.Anthropic, "claude-opus-5", 1_000_000, 1_000_000);

        Assert.Equal(30m, cost);
    }

    [Fact]
    public void Model_renders_as_its_bare_id()
    {
        // A ComboBox displays ToString() and copies it into Text when an item is picked, so a
        // decorated string here becomes the model id saved to settings — and makes the settings
        // window's match-or-append lookup miss every time, appending a duplicate on each restore.
        // Both bugs were real; this is the invariant that prevents them.
        var model = AiCatalog.FindModel(AiProviderKind.Anthropic, "claude-opus-5");

        Assert.NotNull(model);
        Assert.Equal("claude-opus-5", model!.ToString());
        Assert.NotEqual(model.ToString(), model.Describe());
    }

    [Fact]
    public void Every_model_renders_as_something_resolvable()
    {
        // Round trip: whatever the picker shows must find its way back to a catalogue entry.
        foreach (var kind in Enum.GetValues<AiProviderKind>())
        {
            Assert.All(
                AiCatalog.For(kind).Models,
                model => Assert.NotNull(AiCatalog.FindModel(kind, model.ToString())));
        }
    }

    [Theory]
    [InlineData("claude-haiku-4-5-20251001", "claude-haiku-4-5")]
    [InlineData("claude-opus-4-5-20251101", "claude-opus-4-5")]
    [InlineData("claude-opus-5", "claude-opus-5")]
    [InlineData("gpt-4o", "gpt-4o")]
    // Not a date: eight digits are required, and this has seven.
    [InlineData("model-1234567", "model-1234567")]
    public void Dated_snapshot_ids_reduce_to_their_alias(string id, string expected)
    {
        Assert.Equal(expected, AiCatalog.NormalizeModelId(id));
    }

    [Fact]
    public void A_dated_snapshot_id_still_finds_its_price()
    {
        // The models endpoint returns older models only in dated form. Before this fallback every
        // one of them showed "price unknown" the moment the user pressed Refresh.
        var model = AiCatalog.FindModel(AiProviderKind.Anthropic, "claude-haiku-4-5-20251001");

        Assert.NotNull(model);
        Assert.Equal(1m, model!.InputUsdPerMillion);
    }

    [Fact]
    public void Model_ids_are_unique_across_providers()
    {
        // ResolveModel detects a foreign model id by scanning the other providers' lists, so an id
        // appearing in two catalogues would make that check ambiguous.
        var all = AiCatalog.All.SelectMany(p => p.Models.Select(m => m.Id)).ToList();

        Assert.Equal(all.Count, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData(AiProviderKind.Anthropic, "claude-sonnet-5", "claude-sonnet-5")]
    [InlineData(AiProviderKind.Google, "gemini-2.5-flash", "gemini-2.5-flash")]
    public void Resolve_keeps_a_model_that_belongs_to_the_provider(
        AiProviderKind kind, string configured, string expected)
    {
        Assert.Equal(expected, AiProviderFactory.ResolveModel(kind, configured));
    }

    [Fact]
    public void Resolve_keeps_an_unknown_model_so_newer_ids_still_work()
    {
        // The model field is free text precisely so a model released after this build can be used.
        // Rejecting an unrecognised id would make the app the obstacle.
        Assert.Equal(
            "claude-something-7",
            AiProviderFactory.ResolveModel(AiProviderKind.Anthropic, "claude-something-7"));
    }

    [Fact]
    public void Resolve_replaces_a_model_left_over_from_another_provider()
    {
        // Switching provider without touching the model field is the obvious user path, and it is
        // the one that produces the least helpful error if it goes through unmodified.
        var resolved = AiProviderFactory.ResolveModel(AiProviderKind.Google, "claude-opus-5");

        Assert.Equal(AiCatalog.For(AiProviderKind.Google).DefaultModel, resolved);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_falls_back_to_the_default_when_nothing_is_configured(string? configured)
    {
        var resolved = AiProviderFactory.ResolveModel(AiProviderKind.OpenAi, configured);

        Assert.Equal(AiCatalog.For(AiProviderKind.OpenAi).DefaultModel, resolved);
    }

    [Fact]
    public void Gemini_model_ids_survive_the_qualified_form_the_list_endpoint_returns()
    {
        // models.list returns "models/gemini-2.5-pro"; the generateContent URL already contains
        // "models/", so passing it through unchanged yields "models/models/…" and a 404.
        Assert.Equal("gemini-2.5-pro", GoogleProvider.NormalizeModel("models/gemini-2.5-pro"));
        Assert.Equal("gemini-2.5-pro", GoogleProvider.NormalizeModel("gemini-2.5-pro"));
    }
}
