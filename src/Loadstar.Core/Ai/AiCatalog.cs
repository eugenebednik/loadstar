using Loadstar.Core.Configuration;

namespace Loadstar.Core.Ai;

/// <summary>
/// What providers exist, which of their models can do the job, and what they cost.
///
/// <para>One table, read by everything: the settings dropdowns, the pre-flight validation, the
/// factory, and the cost estimate. Splitting it produced the obvious bug — a model offered in the UI
/// that the factory could not construct — so the rule is that nothing else hardcodes a model id.</para>
///
/// <para><b>Vision is a hard requirement, not a preference.</b> Loadstar's entire input is a
/// screenshot, so a text-only model cannot do anything here at all. Models are therefore filtered on
/// <see cref="AiModelInfo.SupportsVision"/> before they are ever offered, because the alternative is
/// a confusing mid-request failure that reads like a bug in the capture pipeline.</para>
/// </summary>
public static class AiCatalog
{
    /// <summary>
    /// When the prices below were last checked, so staleness is visible rather than assumed.
    /// Providers change prices and the numbers here are only ever an estimate.
    /// </summary>
    public const string PricesVerifiedOn = "2026-08-04";

    public static IReadOnlyList<AiProviderInfo> All { get; } =
    [
        new AiProviderInfo
        {
            Kind = AiProviderKind.Anthropic,
            DisplayName = "Anthropic (Claude)",
            EnvironmentVariable = "ANTHROPIC_API_KEY",
            KeyPlaceholder = "sk-ant-…",
            ConsoleUrl = "https://console.anthropic.com/settings/keys",
            BillingNote = "Per-token billing from a prepaid balance. A Claude Pro/Max "
                + "subscription does not include API access.",
            // Verified against this account's own /v1/models on 2026-08-04, so these are ids the
            // API actually serves rather than ids recalled from documentation.
            //
            // Ordered most capable first, which makes Models[0] the default. Opus 5 rather than
            // Fable 5 deliberately: Fable is the stronger model but costs double, and the default
            // is what every user pays until they change it.
            Models =
            [
                // Prices from Anthropic's published per-million rates. Every current Claude model is
                // vision-capable, so nothing here is filtered out.
                new() { Id = "claude-opus-5", DisplayName = "Claude Opus 5", InputUsdPerMillion = 5m, OutputUsdPerMillion = 25m, ContextTokens = 1_000_000 },
                new() { Id = "claude-fable-5", DisplayName = "Claude Fable 5 (most capable)", InputUsdPerMillion = 10m, OutputUsdPerMillion = 50m, ContextTokens = 1_000_000 },
                new() { Id = "claude-sonnet-5", DisplayName = "Claude Sonnet 5", InputUsdPerMillion = 3m, OutputUsdPerMillion = 15m, ContextTokens = 1_000_000 },
                new() { Id = "claude-opus-4-8", DisplayName = "Claude Opus 4.8", InputUsdPerMillion = 5m, OutputUsdPerMillion = 25m, ContextTokens = 1_000_000 },
                new() { Id = "claude-haiku-4-5", DisplayName = "Claude Haiku 4.5", InputUsdPerMillion = 1m, OutputUsdPerMillion = 5m, ContextTokens = 200_000 },
            ],
        },

        new AiProviderInfo
        {
            Kind = AiProviderKind.OpenAi,
            DisplayName = "OpenAI",
            EnvironmentVariable = "OPENAI_API_KEY",
            KeyPlaceholder = "sk-…",
            ConsoleUrl = "https://platform.openai.com/api-keys",
            BillingNote = "Per-token billing from a prepaid balance. A ChatGPT Plus/Pro "
                + "subscription does not include API access.",
            // Prices read from OpenAI's published pricing page on 2026-08-04. Standard tier,
            // short-context rates — long context and cached input are billed differently, so treat
            // the estimate as an estimate.
            //
            // Models[0] is the default, and it is the balanced flagship rather than the top one,
            // for the same reason Opus 5 leads the Anthropic list: the default is what every user
            // pays until they change it.
            Models =
            [
                new() { Id = "gpt-5.6-terra", DisplayName = "GPT-5.6 Terra", InputUsdPerMillion = 2m, OutputUsdPerMillion = 12m },
                new() { Id = "gpt-5.6-sol", DisplayName = "GPT-5.6 Sol (most capable)", InputUsdPerMillion = 5m, OutputUsdPerMillion = 30m },
                new() { Id = "gpt-5.6-luna", DisplayName = "GPT-5.6 Luna", InputUsdPerMillion = 0.20m, OutputUsdPerMillion = 1.20m },
                new() { Id = "gpt-4.1", DisplayName = "GPT-4.1", InputUsdPerMillion = 2m, OutputUsdPerMillion = 8m, ContextTokens = 1_000_000 },
                new() { Id = "gpt-4o", DisplayName = "GPT-4o", InputUsdPerMillion = 2.50m, OutputUsdPerMillion = 10m, ContextTokens = 128_000 },
                new() { Id = "gpt-4o-mini", DisplayName = "GPT-4o mini", InputUsdPerMillion = 0.15m, OutputUsdPerMillion = 0.60m, ContextTokens = 128_000 },
            ],
        },

        new AiProviderInfo
        {
            Kind = AiProviderKind.Google,
            DisplayName = "Google (Gemini)",
            EnvironmentVariable = "GEMINI_API_KEY",
            KeyPlaceholder = "AIza…",
            ConsoleUrl = "https://aistudio.google.com/apikey",
            BillingNote = "Free tier available, with rate limits — the one provider that runs "
                + "without a billing account. Free-tier requests may be used for training.",
            // Prices read from Google's published pricing page on 2026-08-04, paid standard tier.
            //
            // Gemini 2.0 Flash is deliberately absent: it was shut down on 2026-06-01. It was in an
            // earlier draft of this list, which is a good illustration of why the bundled lists
            // carry a verification date and why Refresh exists.
            //
            // 2.5 Pro's rate steps up above a 200k-token prompt. The rate here is the lower band,
            // which is the one that applies — a capture plus the knowledge pack runs well under
            // 200k — but it means the estimate understates an unusually large request.
            Models =
            [
                new() { Id = "gemini-3.6-flash", DisplayName = "Gemini 3.6 Flash", InputUsdPerMillion = 1.50m, OutputUsdPerMillion = 7.50m },
                new() { Id = "gemini-3.5-flash", DisplayName = "Gemini 3.5 Flash", InputUsdPerMillion = 1.50m, OutputUsdPerMillion = 9m },
                new() { Id = "gemini-2.5-pro", DisplayName = "Gemini 2.5 Pro", InputUsdPerMillion = 1.25m, OutputUsdPerMillion = 10m, ContextTokens = 1_000_000 },
                new() { Id = "gemini-2.5-flash", DisplayName = "Gemini 2.5 Flash", InputUsdPerMillion = 0.30m, OutputUsdPerMillion = 2.50m, ContextTokens = 1_000_000 },
                new() { Id = "gemini-3.5-flash-lite", DisplayName = "Gemini 3.5 Flash-Lite", InputUsdPerMillion = 0.30m, OutputUsdPerMillion = 2.50m },
                new() { Id = "gemini-2.5-flash-lite", DisplayName = "Gemini 2.5 Flash-Lite", InputUsdPerMillion = 0.10m, OutputUsdPerMillion = 0.40m },
            ],
        },
    ];

    public static AiProviderInfo For(AiProviderKind kind) =>
        All.FirstOrDefault(p => p.Kind == kind)
        ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, "No such provider in the catalogue.");

    /// <summary>
    /// The catalogue entry for a model, or null when it isn't one we ship metadata for.
    ///
    /// <para>Null is an ordinary outcome, not an error: the model field is free text precisely so a
    /// model released after this build can be typed in. Callers treat an unknown model as usable but
    /// unpriced rather than rejecting it.</para>
    /// </summary>
    public static AiModelInfo? FindModel(AiProviderKind kind, string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        var wanted = modelId.Trim();
        var models = For(kind).Models;

        var exact = models.FirstOrDefault(m => m.Id.Equals(wanted, StringComparison.OrdinalIgnoreCase));

        if (exact is not null)
        {
            return exact;
        }

        // Fall back to the undated alias. The models endpoint returns older models only in their
        // dated form (claude-haiku-4-5-20251001), which matches no catalogue entry — so without
        // this, every model behind an alias reported "price unknown" the moment Refresh was used.
        var alias = NormalizeModelId(wanted);

        return alias.Equals(wanted, StringComparison.OrdinalIgnoreCase)
            ? null
            : models.FirstOrDefault(m => m.Id.Equals(alias, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Strips a trailing <c>-YYYYMMDD</c> snapshot suffix, leaving the alias.
    ///
    /// <para><c>claude-haiku-4-5-20251001</c> and <c>claude-haiku-4-5</c> are the same model — the
    /// first pins a snapshot, the second follows the latest. Treating them as unrelated made the
    /// refreshed list show both and price neither.</para>
    /// </summary>
    public static string NormalizeModelId(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId) || modelId.Length < 10 || modelId[^9] != '-')
        {
            return modelId ?? string.Empty;
        }

        for (var i = modelId.Length - 8; i < modelId.Length; i++)
        {
            if (!char.IsAsciiDigit(modelId[i]))
            {
                return modelId;
            }
        }

        return modelId[..^9];
    }

    /// <summary>
    /// Estimated USD for a call, or null when the model's price isn't known.
    ///
    /// <para>Null rather than zero, and the distinction carries weight: zero reads as "this was
    /// free", which would let a spend ceiling be breached silently. An absent number is honest about
    /// being absent, which is the behaviour <see cref="AnthropicProvider"/> chose originally and
    /// which survives here now that some prices are actually known.</para>
    /// </summary>
    public static decimal? EstimateCostUsd(AiProviderKind kind, string? modelId, int inputTokens, int outputTokens)
    {
        if (FindModel(kind, modelId) is not { InputUsdPerMillion: { } input, OutputUsdPerMillion: { } output })
        {
            return null;
        }

        return ((inputTokens * input) + (outputTokens * output)) / 1_000_000m;
    }

    /// <summary>
    /// The models worth offering for a provider: vision-capable, most capable first.
    /// </summary>
    public static IReadOnlyList<AiModelInfo> UsableModels(AiProviderKind kind) =>
        [.. For(kind).Models.Where(m => m.SupportsVision)];
}

public sealed record AiProviderInfo
{
    public required AiProviderKind Kind { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Checked when no key is stored, so a key already exported for other tooling works.</summary>
    public required string EnvironmentVariable { get; init; }

    public required string KeyPlaceholder { get; init; }

    /// <summary>Where the user goes to create a key. Shown in settings; nothing opens it automatically.</summary>
    public required string ConsoleUrl { get; init; }

    /// <summary>
    /// One line on how this provider charges. Present because the subscription-versus-API
    /// distinction catches people out — a paid plan on the consumer product grants no API access on
    /// either Anthropic or OpenAI, and finding that out via a failed request is a poor introduction.
    /// </summary>
    public required string BillingNote { get; init; }

    public required IReadOnlyList<AiModelInfo> Models { get; init; }

    /// <summary>
    /// Whether <see cref="Models"/> can be trusted as the current, complete list.
    ///
    /// <para>True for all three today: every entry was checked against the provider's own live model
    /// list or pricing page on <see cref="AiCatalog.PricesVerifiedOn"/>. It is not a permanent
    /// property — a bundled list of third-party identifiers goes stale on a schedule nobody here
    /// controls, which is exactly what Refresh is for, and why an earlier draft of this file listed
    /// a Gemini model that had already been shut down.</para>
    ///
    /// <para>Set it false when adding a provider whose models are guessed rather than checked; the
    /// settings window then tells the user to press Refresh before trusting the list.</para>
    /// </summary>
    public bool SeedIsAuthoritative { get; init; } = true;

    public string DefaultModel => Models[0].Id;
}

public sealed record AiModelInfo
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>
    /// Defaults true because every model listed here is vision-capable. A text-only model is useless
    /// to Loadstar, so the flag exists to keep one out of the dropdown if the list ever grows one.
    /// </summary>
    public bool SupportsVision { get; init; } = true;

    /// <summary>Null when unknown, which suppresses the cost estimate rather than faking it.</summary>
    public decimal? InputUsdPerMillion { get; init; }

    public decimal? OutputUsdPerMillion { get; init; }

    public int? ContextTokens { get; init; }

    /// <summary>Name plus price, for a hint label beside the picker.</summary>
    public string Describe() => InputUsdPerMillion is { } input && OutputUsdPerMillion is { } output
        ? $"{DisplayName} · ${input:0.##}/${output:0.##} per Mtok"
        : DisplayName;

    /// <summary>
    /// The bare model id — <b>not</b> the friendly description.
    ///
    /// <para>Load-bearing, because this is what a ComboBox displays and what its <c>Text</c> becomes
    /// when an item is picked. Returning a decorated string here broke two things at once: the id
    /// saved to settings became "Claude Opus 5 · $5/$25 per Mtok", and <c>SetComboText</c> — which
    /// appends the value as a new item when it matches none — appended a duplicate id on every
    /// restore, so the list grew each time the window settled. The price belongs in a label, not in
    /// a value that round-trips through configuration.</para>
    /// </summary>
    public override string ToString() => Id;
}
