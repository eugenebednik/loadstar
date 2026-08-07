using System.Text.Json;
using Loadstar.Core.Configuration;
using Loadstar.Core.Model;

namespace Loadstar.Core.Ai;

/// <summary>
/// Google Gemini, over the Generative Language API.
///
/// <para>The one provider here with a free tier, which makes it the option that works without a
/// billing account — at the cost of rate limits, and with free-tier requests eligible for training.
/// Both facts belong in front of the user before they choose it, so they live in
/// <see cref="AiCatalog"/> and are shown in settings.</para>
///
/// <para>Block ordering matches the other two providers — images first, instruction last — so the
/// model choice changes who answers, not what they were asked.</para>
/// </summary>
public sealed class GoogleProvider : HttpAiProvider
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    public GoogleProvider(string apiKey, HttpClient? http = null) : base(http)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        // The header form, not the `?key=` query parameter the quickstarts use. A key in a URL leaks
        // into proxy logs, crash reports and anything that records a request line; a header does not.
        Http.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);
    }

    public override string Name => "Google Gemini";

    public override IReadOnlyList<string> SupportedModels =>
        [.. AiCatalog.UsableModels(AiProviderKind.Google).Select(m => m.Id)];

    public override async Task<AiResponse> AnalyzeAsync(AiRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parts = new List<object>();

        foreach (var image in request.Images)
        {
            parts.Add(new
            {
                inlineData = new
                {
                    mimeType = image.MediaType,
                    data = Convert.ToBase64String(image.Png),
                },
            });

            if (!string.IsNullOrWhiteSpace(image.Label))
            {
                parts.Add(new { text = $"(above: {image.Label})" });
            }
        }

        parts.Add(new { text = request.UserPrompt });

        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = request.SystemPrompt } } },
            contents = new object[] { new { role = "user", parts } },
            generationConfig = new { maxOutputTokens = request.MaxOutputTokens },
        };

        var url = $"{BaseUrl}/{Uri.EscapeDataString(NormalizeModel(request.Model))}:generateContent";

        using var document = await PostJsonAsync(url, body, cancellationToken).ConfigureAwait(false);

        return new AiResponse
        {
            Text = ReadText(document.RootElement),
            Usage = ReadUsage(document.RootElement, request.Model),
        };
    }

    /// <summary>
    /// Accepts either <c>gemini-2.5-pro</c> or the fully qualified <c>models/gemini-2.5-pro</c>.
    /// The list endpoint returns the qualified form, so a model picked from Refresh would otherwise
    /// produce a URL with <c>models/models/</c> in it and a puzzling 404.
    /// </summary>
    public static string NormalizeModel(string model) =>
        model.StartsWith("models/", StringComparison.OrdinalIgnoreCase) ? model["models/".Length..] : model;

    private static string ReadText(JsonElement root)
    {
        // A safety block comes back with no candidates at all, and the reason sits in a sibling
        // object — reporting "empty response" here would send someone hunting for a parser bug.
        if (root.TryGetProperty("promptFeedback", out var feedback)
            && feedback.TryGetProperty("blockReason", out var blocked))
        {
            throw new AiProviderException($"Gemini blocked the request: {blocked.GetString()}");
        }

        if (!root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            throw new AiProviderException("Gemini returned no candidates.");
        }

        var candidate = candidates[0];

        var text = candidate.TryGetProperty("content", out var content)
            && content.TryGetProperty("parts", out var parts)
            && parts.ValueKind == JsonValueKind.Array
                ? string.Join("\n", parts.EnumerateArray()
                    .Where(part => part.TryGetProperty("text", out _))
                    .Select(part => part.GetProperty("text").GetString())
                    .Where(value => !string.IsNullOrEmpty(value)))
                : string.Empty;

        // Gemini thinks by default and those tokens count against maxOutputTokens, so a ceiling sized
        // for the answer alone can be spent on reasoning and leave the answer unfinished.
        //
        // THIS IS CHECKED WHETHER OR NOT THERE IS TEXT, and that is the whole point. It used to be
        // guarded by `string.IsNullOrWhiteSpace(text)`, which caught only total starvation — the case
        // where reasoning ate everything and nothing came back. The commoner and nastier case is
        // PARTIAL: reasoning eats most of the budget, the model starts its JSON object, and the reply
        // is cut off mid-way. That returned a truncated string as though it were complete, and the
        // failure surfaced downstream as "The model's reply contained no JSON object" — which points
        // at the prompt, the parser, anything except the token ceiling that actually caused it.
        //
        // Observed in the field: a 658-character reply, cut off mid-object, reported as malformed JSON.
        if (candidate.TryGetProperty("finishReason", out var reason)
            && reason.GetString() is { } finish
            && !finish.Equals("STOP", StringComparison.OrdinalIgnoreCase))
        {
            var truncated = !string.IsNullOrWhiteSpace(text);

            throw new AiProviderException(
                $"Gemini stopped before finishing (finishReason: {finish}). "
                + (truncated
                    ? $"It returned {text.Length} characters of an incomplete answer, which cannot be used. "
                    : "It returned nothing at all. ")
                + "If this is MAX_TOKENS, the reply budget was spent on reasoning — raise MaxOutputTokens.")
            {
                // Worth retrying: the same request under a larger budget, or a shorter reasoning pass,
                // may well succeed. Nothing about the request itself is invalid.
                IsTransient = finish.Equals("MAX_TOKENS", StringComparison.OrdinalIgnoreCase),
            };
        }

        return text;
    }

    private static TokenUsage? ReadUsage(JsonElement root, string model)
    {
        if (!root.TryGetProperty("usageMetadata", out var usage))
        {
            return null;
        }

        var input = usage.TryGetProperty("promptTokenCount", out var p) ? p.GetInt32() : 0;
        var output = usage.TryGetProperty("candidatesTokenCount", out var c) ? c.GetInt32() : 0;

        // Reasoning tokens are reported separately but billed as output, so folding them in is what
        // makes the estimate match the invoice.
        if (usage.TryGetProperty("thoughtsTokenCount", out var thoughts))
        {
            output += thoughts.GetInt32();
        }

        return new TokenUsage
        {
            InputTokens = input,
            OutputTokens = output,
            EstimatedCostUsd = AiCatalog.EstimateCostUsd(AiProviderKind.Google, model, input, output) ?? 0m,
        };
    }
}
