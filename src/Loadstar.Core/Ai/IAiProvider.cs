using Loadstar.Core.Model;

namespace Loadstar.Core.Ai;

/// <summary>
/// A vision-capable chat provider. Implementations own their wire format and nothing else —
/// prompt construction and response parsing are shared, so adding a provider means writing
/// one HTTP call, not re-deriving the advice logic.
/// </summary>
public interface IAiProvider : IDisposable
{
    string Name { get; }

    /// <summary>Model ids this provider will accept, most capable first.</summary>
    IReadOnlyList<string> SupportedModels { get; }

    /// <summary>
    /// Send the screenshots plus context and get back raw JSON matching the advice schema.
    /// Throws <see cref="AiProviderException"/> on transport or auth failure; a malformed
    /// model response is the caller's problem to parse and retry.
    /// </summary>
    Task<AiResponse> AnalyzeAsync(AiRequest request, CancellationToken cancellationToken);
}

public sealed record AiRequest
{
    public required string Model { get; init; }
    public required string SystemPrompt { get; init; }
    public required string UserPrompt { get; init; }

    /// <summary>PNG-encoded captures. Usually one; more when several screens were needed.</summary>
    public required IReadOnlyList<CapturedImage> Images { get; init; }

    /// <summary>
    /// Ceiling on the reply.
    ///
    /// <para>Sized for more than the visible answer on purpose. Current models reason before
    /// answering and those tokens come out of this same budget, so a ceiling fitted to the JSON
    /// alone gets spent on thinking and returns a truncated object — which surfaces as a parse
    /// failure and reads like a prompt bug rather than a budget one.</para>
    /// </summary>
    public int MaxOutputTokens { get; init; } = 8000;

    /// <summary>
    /// How hard to think, for providers that expose it: <c>low</c>, <c>medium</c>, <c>high</c> or
    /// <c>max</c>. Anything else leaves the provider's own default in place rather than guessing.
    /// </summary>
    public string? Effort { get; init; }
}

public sealed record CapturedImage
{
    public required byte[] Png { get; init; }
    public string MediaType => "image/png";

    /// <summary>What this image shows, so the model can tell captures apart.</summary>
    public string? Label { get; init; }
}

public sealed record AiResponse
{
    public required string Text { get; init; }
    public TokenUsage? Usage { get; init; }
}

public class AiProviderException : Exception
{
    public AiProviderException(string message, Exception? inner = null) : base(message, inner) { }

    /// <summary>True when retrying later might work — rate limits, 5xx, timeouts.</summary>
    public bool IsTransient { get; init; }
}
