using Anthropic;
using Anthropic.Core;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Loadstar.Core.Model;

namespace Loadstar.Core.Ai;

/// <summary>
/// Claude, through Anthropic's official SDK.
///
/// <para>Owns the wire format and nothing else. Prompt construction and response parsing are shared
/// across providers, so adding OpenAI later means writing one HTTP call rather than re-deriving how
/// advice works.</para>
/// </summary>
public sealed class AnthropicProvider : IAiProvider, IDisposable
{
    private readonly AnthropicClient _client;

    public AnthropicProvider(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        _client = new AnthropicClient(new ClientOptions { ApiKey = apiKey });
    }

    public string Name => "Anthropic";

    public IReadOnlyList<string> SupportedModels =>
        [.. AiCatalog.UsableModels(Configuration.AiProviderKind.Anthropic).Select(m => m.Id)];

    public async Task<AiResponse> AnalyzeAsync(AiRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var content = new List<ContentBlockParam>();

        // Images first, then the instruction. Vision models attend better to a question asked
        // after the material it refers to, and it keeps the images at a stable prefix position for
        // when this grows into the multi-turn conversation.
        foreach (var image in request.Images)
        {
            content.Add(new ContentBlockParam(new ImageBlockParam(
                new ImageBlockParamSource(new Base64ImageSource
                {
                    Data = Convert.ToBase64String(image.Png),
                    MediaType = MediaType.ImagePng,
                }))));

            if (!string.IsNullOrWhiteSpace(image.Label))
            {
                content.Add(new ContentBlockParam(new TextBlockParam($"(above: {image.Label})")));
            }
        }

        content.Add(new ContentBlockParam(new TextBlockParam(request.UserPrompt)));

        var parameters = new MessageCreateParams
        {
            Model = request.Model,
            MaxTokens = request.MaxOutputTokens,
            System = new MessageCreateParamsSystem(request.SystemPrompt),
            Messages =
            [
                new MessageParam
                {
                    Role = Role.User,
                    Content = new MessageParamContent(content),
                },
            ],
        };

        // Thinking is left at the model's own default — on Claude Opus 5 that means adaptive, and it
        // is wanted here: the advice this app produces turns on breakpoint arithmetic and cost
        // comparisons, which is exactly the work reasoning improves. What it costs is budget, which
        // is why AiRequest.MaxOutputTokens is sized for reasoning plus answer rather than answer
        // alone. Effort is the dial for that trade.
        if (ParseEffort(request.Effort) is { } effort)
        {
            parameters = parameters with { OutputConfig = new OutputConfig { Effort = effort } };
        }

        try
        {
            var message = await _client.Messages.Create(parameters, cancellationToken).ConfigureAwait(false);

            return new AiResponse
            {
                Text = ConcatenateText(message),
                Usage = ReadUsage(message, request.Model),
            };
        }
        catch (AnthropicRateLimitException ex)
        {
            throw new AiProviderException("Anthropic rate limit reached.", ex) { IsTransient = true };
        }
        catch (Anthropic5xxException ex)
        {
            throw new AiProviderException("Anthropic returned a server error.", ex) { IsTransient = true };
        }
        catch (AnthropicUnauthorizedException ex)
        {
            throw new AiProviderException("Anthropic rejected the API key.", ex);
        }
        catch (AnthropicIOException ex)
        {
            throw new AiProviderException($"Could not reach Anthropic: {ex.Message}", ex) { IsTransient = true };
        }
        catch (AnthropicException ex)
        {
            throw new AiProviderException($"Anthropic request failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Joins the text blocks of a reply, ignoring any non-text blocks.
    /// </summary>
    private static string ConcatenateText(Message message)
    {
        var text = message.Content
            .Select(block => block.TryPickText(out var textBlock) ? textBlock.Text : null)
            .Where(value => !string.IsNullOrEmpty(value));

        return string.Join("\n", text);
    }

    /// <summary>
    /// Maps the configured effort onto the SDK enum, or null to leave the provider's default alone.
    ///
    /// <para>Only the values the SDK actually defines are mapped. An unrecognised setting produces
    /// null rather than a nearest guess, because silently downgrading <c>xhigh</c> to <c>high</c>
    /// would change what the user asked for while appearing to honour it.</para>
    /// </summary>
    private static Effort? ParseEffort(string? effort) => effort?.Trim().ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "medium" => Effort.Medium,
        "high" => Effort.High,
        "max" => Effort.Max,
        _ => null,
    };

    /// <summary>
    /// Token counts, priced from <see cref="AiCatalog"/>.
    ///
    /// <para>This used to report a hard 0 on the reasoning that a wrong number in a spend estimate
    /// is worse than an obviously absent one. That reasoning still holds and is now enforced one
    /// level down: the catalogue returns null for a model whose price it does not carry, and null
    /// still becomes 0 here. What changed is only that the models we <b>do</b> ship rates for now
    /// produce a real figure instead of a placeholder.</para>
    /// </summary>
    private static TokenUsage? ReadUsage(Message message, string model)
    {
        if (message.Usage is not { } usage)
        {
            return null;
        }

        var input = (int)usage.InputTokens;
        var output = (int)usage.OutputTokens;

        return new TokenUsage
        {
            InputTokens = input,
            OutputTokens = output,
            EstimatedCostUsd = AiCatalog.EstimateCostUsd(
                Configuration.AiProviderKind.Anthropic, model, input, output) ?? 0m,
        };
    }

    public void Dispose() => _client.Dispose();
}
