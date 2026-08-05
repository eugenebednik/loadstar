using System.Text.Json;
using Loadstar.Core.Configuration;
using Loadstar.Core.Model;

namespace Loadstar.Core.Ai;

/// <summary>
/// OpenAI, over the Chat Completions API.
///
/// <para>Raw HTTP rather than an SDK: this is one JSON body and one response shape, and the
/// dependency would earn nothing. The block ordering deliberately matches
/// <see cref="AnthropicProvider"/> — images first, instruction last — so that switching provider
/// changes who answers and not what they were asked.</para>
/// </summary>
public sealed class OpenAiProvider : HttpAiProvider
{
    private const string Endpoint = "https://api.openai.com/v1/chat/completions";

    public OpenAiProvider(string apiKey, HttpClient? http = null) : base(http)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        Http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    }

    public override string Name => "OpenAI";

    public override IReadOnlyList<string> SupportedModels =>
        [.. AiCatalog.UsableModels(AiProviderKind.OpenAi).Select(m => m.Id)];

    public override async Task<AiResponse> AnalyzeAsync(AiRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var content = new List<object>();

        foreach (var image in request.Images)
        {
            content.Add(new
            {
                type = "image_url",
                image_url = new { url = $"data:{image.MediaType};base64,{Convert.ToBase64String(image.Png)}" },
            });

            if (!string.IsNullOrWhiteSpace(image.Label))
            {
                content.Add(new { type = "text", text = $"(above: {image.Label})" });
            }
        }

        content.Add(new { type = "text", text = request.UserPrompt });

        var body = new
        {
            model = request.Model,

            // `max_completion_tokens`, not the older `max_tokens`: the newer reasoning-capable models
            // reject the legacy field, and this one counts reasoning tokens against the ceiling too —
            // which is the honest accounting for a budget that has to hold.
            max_completion_tokens = request.MaxOutputTokens,

            messages = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content },
            },
        };

        using var document = await PostJsonAsync(Endpoint, body, cancellationToken).ConfigureAwait(false);

        return new AiResponse
        {
            Text = ReadText(document.RootElement),
            Usage = ReadUsage(document.RootElement, request.Model),
        };
    }

    private static string ReadText(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            throw new AiProviderException("OpenAI returned no choices.");
        }

        var message = choices[0].GetProperty("message");

        // A refusal arrives as a populated `refusal` with `content` null. Surfacing it as an empty
        // answer would send the caller to the JSON parser to fail there instead, which reads as a
        // malformed reply rather than as a decision the model made.
        if (message.TryGetProperty("refusal", out var refusal)
            && refusal.ValueKind == JsonValueKind.String)
        {
            throw new AiProviderException($"OpenAI declined the request: {refusal.GetString()}");
        }

        return message.TryGetProperty("content", out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString() ?? string.Empty
            : string.Empty;
    }

    private static TokenUsage? ReadUsage(JsonElement root, string model)
    {
        if (!root.TryGetProperty("usage", out var usage))
        {
            return null;
        }

        var input = usage.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : 0;
        var output = usage.TryGetProperty("completion_tokens", out var c) ? c.GetInt32() : 0;

        return new TokenUsage
        {
            InputTokens = input,
            OutputTokens = output,
            EstimatedCostUsd = AiCatalog.EstimateCostUsd(AiProviderKind.OpenAi, model, input, output) ?? 0m,
        };
    }
}
