using Loadstar.Core.Model;

namespace Loadstar.Core.Ai;

/// <summary>
/// A vision-capable chat provider. Implementations own their wire format and nothing else —
/// prompt construction and response parsing are shared, so adding a provider means writing
/// one HTTP call, not re-deriving the advice logic.
/// </summary>
public interface IAiProvider
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

    public int MaxOutputTokens { get; init; } = 2048;
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
