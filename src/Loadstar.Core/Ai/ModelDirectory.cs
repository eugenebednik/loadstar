using System.Text.Json;
using Loadstar.Core.Configuration;

namespace Loadstar.Core.Ai;

/// <summary>
/// Asks a provider what models it actually has.
///
/// <para>The bundled lists in <see cref="AiCatalog"/> are a starting point that ages the moment a
/// provider ships something new. This is the authority — the same choice made for the questlog
/// server list, and for the same reason: a hardcoded roster is wrong on a schedule nobody
/// controls.</para>
///
/// <para>Results merge back onto the catalogue so a known model keeps its price, and an unknown one
/// is offered without one rather than being hidden.</para>
/// </summary>
public static class ModelDirectory
{
    public static async Task<IReadOnlyList<AiModelInfo>> ListAsync(
        AiProviderKind kind,
        string apiKey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        var ids = kind switch
        {
            AiProviderKind.Anthropic => await ListAnthropicAsync(apiKey, cancellationToken).ConfigureAwait(false),
            AiProviderKind.OpenAi => await ListOpenAiAsync(apiKey, cancellationToken).ConfigureAwait(false),
            AiProviderKind.Google => await ListGoogleAsync(apiKey, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No provider for this kind."),
        };

        return
        [
            .. ids
                // Collapse a model's dated snapshot and its alias into one entry — they are the
                // same model, and listing both just doubles the list with ids that look like noise.
                // The shorter of the pair wins, which is always the alias.
                .GroupBy(AiCatalog.NormalizeModelId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(id => id.Length).ThenBy(id => id, StringComparer.Ordinal).First())
                // Keep the id the provider actually reported — that one is certain to work — and
                // take the display name and pricing from the catalogue.
                .Select(id => new { Id = id, Known = AiCatalog.FindModel(kind, id) })

                // Unpriced models are dropped. An account can still reach superseded models it once
                // used, and they arrive as bare dated ids with no price — clutter above a picker
                // whose job is "choose what to spend money on".
                //
                // The cost of this filter is that a model newer than this build is invisible here
                // too, since nothing bundled can price it. That is survivable only because the model
                // box is free text: a new id can be typed and will be used as given. If that ever
                // stops being true, this filter has to go.
                .Where(entry => entry.Known is { InputUsdPerMillion: not null, OutputUsdPerMillion: not null })
                .Select(entry => entry.Known! with { Id = entry.Id })

                // Catalogue order, so the list opens on the most capable.
                .OrderBy(model => CatalogueRank(kind, model.Id))
                .ThenBy(model => model.Id, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>
    /// Position in the bundled catalogue, or <see cref="int.MaxValue"/> for anything not in it.
    /// </summary>
    private static int CatalogueRank(AiProviderKind kind, string modelId)
    {
        var models = AiCatalog.For(kind).Models;

        for (var i = 0; i < models.Count; i++)
        {
            if (models[i].Id.Equals(AiCatalog.NormalizeModelId(modelId), StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    /// <summary>
    /// Through the official SDK, unlike its siblings here.
    ///
    /// <para>Deliberate: <see cref="AnthropicProvider"/> talks to Claude through the SDK, and having
    /// one Claude call go through it while another hand-rolls the same HTTP is the kind of split
    /// that leaves two auth paths to keep in step.</para>
    /// </summary>
    private static async Task<IReadOnlyList<string>> ListAnthropicAsync(
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var client = new global::Anthropic.AnthropicClient(
            new global::Anthropic.Core.ClientOptions { ApiKey = apiKey });

        try
        {
            var page = await client.Models.List(cancellationToken: cancellationToken).ConfigureAwait(false);

            return [.. page.Items.Select(model => model.ID)];
        }
        catch (global::Anthropic.Exceptions.AnthropicException ex)
        {
            throw new AiProviderException($"Could not list Anthropic models: {ex.Message}", ex);
        }
    }

    private static async Task<IReadOnlyList<string>> ListOpenAiAsync(
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        using var document = await GetJsonAsync(http, "https://api.openai.com/v1/models", "OpenAI", cancellationToken)
            .ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("data", out var data))
        {
            return [];
        }

        return [.. data.EnumerateArray()
            .Select(entry => entry.TryGetProperty("id", out var id) ? id.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Where(LooksLikeChatModel)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)];
    }

    private static async Task<IReadOnlyList<string>> ListGoogleAsync(
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);

        using var document = await GetJsonAsync(
            http,
            "https://generativelanguage.googleapis.com/v1beta/models?pageSize=200",
            "Gemini",
            cancellationToken).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("models", out var models))
        {
            return [];
        }

        return [.. models.EnumerateArray()
            // The list carries embedding and other non-chat models; the capability list is the
            // provider's own statement of what each one can do, so filter on that rather than on a
            // guess about the name.
            .Where(model => model.TryGetProperty("supportedGenerationMethods", out var methods)
                && methods.ValueKind == JsonValueKind.Array
                && methods.EnumerateArray().Any(m => m.GetString() == "generateContent"))
            .Select(model => model.TryGetProperty("name", out var name) ? name.GetString() : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => GoogleProvider.NormalizeModel(name!))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Filters OpenAI's catalogue down to things that could plausibly read a screenshot.
    ///
    /// <para>A heuristic, and knowingly so — that endpoint returns every model on the account with no
    /// capability flags, so speech, embedding and image-generation ids sit alongside chat ones with
    /// nothing to tell them apart but the name. Being slightly over-eager is the right failure
    /// direction here: the model box is free text, so an id wrongly filtered out can still be typed,
    /// whereas a list of eighty entries is unusable.</para>
    /// </summary>
    private static bool LooksLikeChatModel(string id)
    {
        string[] excluded =
        [
            "audio", "realtime", "transcribe", "tts", "whisper", "embedding", "moderation",
            "dall-e", "image", "search", "instruct", "codex", "computer-use",
        ];

        if (excluded.Any(token => id.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // "gpt-…" or a reasoning model like "o3"/"o4-mini".
        return id.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase)
            || (id.Length > 1 && (id[0] == 'o' || id[0] == 'O') && char.IsDigit(id[1]));
    }

    private static async Task<JsonDocument> GetJsonAsync(
        HttpClient http,
        string url,
        string providerName,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new AiProviderException($"Could not reach {providerName}: {ex.Message}", ex) { IsTransient = true };
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiProviderException($"{providerName} timed out.", ex) { IsTransient = true };
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new AiProviderException(
                    $"{providerName} refused the model list ({(int)response.StatusCode}). "
                    + "Check the API key.");
            }

            return JsonDocument.Parse(payload);
        }
    }
}
