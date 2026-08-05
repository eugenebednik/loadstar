using Loadstar.Core.Configuration;

namespace Loadstar.Core.Ai;

/// <summary>
/// Builds the provider a setting names.
///
/// <para>Exists so that the two shells — the tray app and the console PoC — cannot drift apart on
/// which provider a given configuration means. They previously each constructed
/// <see cref="AnthropicProvider"/> directly, which was fine while there was one provider and would
/// have become two places to add each new one.</para>
/// </summary>
public static class AiProviderFactory
{
    /// <summary>
    /// <paramref name="http"/> is for tests; leaving it null lets each provider own its client and
    /// its timeout.
    /// </summary>
    public static IAiProvider Create(AiProviderKind kind, string apiKey, HttpClient? http = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        return kind switch
        {
            AiProviderKind.Anthropic => new AnthropicProvider(apiKey),
            AiProviderKind.OpenAi => new OpenAiProvider(apiKey, http),
            AiProviderKind.Google => new GoogleProvider(apiKey, http),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No provider for this kind."),
        };
    }

    /// <summary>
    /// The model to send, given what the user configured.
    ///
    /// <para>Guards the case that produces the least helpful error message in the whole app: a model
    /// id left over from a different provider. Switching Anthropic to Google without touching the
    /// model field would otherwise send <c>claude-opus-5</c> to Gemini and return a 404 naming a
    /// model the user never chose. Falling back to the provider's default is the recoverable
    /// behaviour.</para>
    /// </summary>
    public static string ResolveModel(AiProviderKind kind, string? configuredModel)
    {
        var info = AiCatalog.For(kind);

        if (string.IsNullOrWhiteSpace(configuredModel))
        {
            return info.DefaultModel;
        }

        var model = configuredModel.Trim();

        // A known model for this provider is obviously fine. An unknown one is kept too — the field
        // is free text precisely so a model newer than this build can be used, and rejecting it
        // would make the app the thing standing between the user and a working model.
        if (AiCatalog.FindModel(kind, model) is not null)
        {
            return model;
        }

        return BelongsToAnotherProvider(kind, model) ? info.DefaultModel : model;
    }

    private static bool BelongsToAnotherProvider(AiProviderKind kind, string model) =>
        AiCatalog.All
            .Where(p => p.Kind != kind)
            .Any(p => p.Models.Any(m => m.Id.Equals(model, StringComparison.OrdinalIgnoreCase)));
}
