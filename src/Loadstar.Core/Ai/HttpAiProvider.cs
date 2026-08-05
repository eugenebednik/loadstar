using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Loadstar.Core.Ai;

/// <summary>
/// Shared plumbing for the providers Loadstar talks to over plain HTTP.
///
/// <para>Anthropic goes through its official SDK; OpenAI and Google do not have one here, and adding
/// two more dependencies to send one JSON body each is a poor trade. What they <b>do</b> need to
/// share is the failure mapping, because <see cref="AiProviderException.IsTransient"/> decides
/// whether the caller retries — and getting that wrong per provider means either hammering a
/// provider that rejected the key, or giving up on a blip.</para>
/// </summary>
public abstract class HttpAiProvider : IAiProvider, IDisposable
{
    private readonly bool _ownsClient;

    protected HttpAiProvider(HttpClient? http)
    {
        _ownsClient = http is null;

        // Generous, because a vision request with a 2560x1600 screenshot and a long system prompt is
        // not a fast call, and a timeout misreads as a provider fault.
        Http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
    }

    protected HttpClient Http { get; }

    protected static JsonSerializerOptions JsonOptions { get; } = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public abstract string Name { get; }

    public abstract IReadOnlyList<string> SupportedModels { get; }

    public abstract Task<AiResponse> AnalyzeAsync(AiRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// POSTs a JSON body and returns the parsed response, converting every failure mode into an
    /// <see cref="AiProviderException"/> with <c>IsTransient</c> set truthfully.
    /// </summary>
    protected async Task<JsonDocument> PostJsonAsync(
        string url,
        object body,
        CancellationToken cancellationToken)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(body, JsonOptions),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response;

        try
        {
            response = await Http.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new AiProviderException($"Could not reach {Name}: {ex.Message}", ex) { IsTransient = true };
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Cancellation requested by the caller is not our problem to relabel; a TaskCanceled
            // without one is HttpClient's timeout, which is worth retrying.
            throw new AiProviderException($"{Name} timed out.", ex) { IsTransient = true };
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw Describe(response.StatusCode, payload);
            }

            try
            {
                return JsonDocument.Parse(payload);
            }
            catch (JsonException ex)
            {
                throw new AiProviderException($"{Name} returned a response that was not JSON.", ex);
            }
        }
    }

    /// <summary>
    /// Turns a failed status into an exception that says something useful.
    ///
    /// <para>The provider's own message is included verbatim, because the informative ones are
    /// exactly the cases a generic string would bury — "your credit balance is too low" arrives as a
    /// plain 400, indistinguishable from a malformed request unless the body is shown.</para>
    /// </summary>
    private AiProviderException Describe(HttpStatusCode status, string payload)
    {
        var detail = ExtractMessage(payload);
        var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $": {detail}";

        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new AiProviderException($"{Name} rejected the API key{suffix}"),

            // A 429 is normally a rate limit and worth retrying — but providers also return it for
            // an exhausted balance, which no amount of waiting fixes. Gemini reports "prepayment
            // credits are depleted" as a 429; retrying that forever would be the wrong response to
            // a message that is telling you to go and pay.
            HttpStatusCode.TooManyRequests when LooksLikeBillingExhaustion(detail) =>
                new AiProviderException($"{Name} has no credit left{suffix}"),

            HttpStatusCode.TooManyRequests =>
                new AiProviderException($"{Name} rate limit reached{suffix}") { IsTransient = true },

            HttpStatusCode.RequestEntityTooLarge =>
                new AiProviderException($"{Name} refused the request as too large{suffix} "
                    + "Try a tighter capture region."),

            >= HttpStatusCode.InternalServerError =>
                new AiProviderException($"{Name} returned a server error ({(int)status}){suffix}") { IsTransient = true },

            _ => new AiProviderException($"{Name} request failed ({(int)status}){suffix}"),
        };
    }

    /// <summary>
    /// Whether an error message is about money rather than pacing.
    ///
    /// <para>String matching, which is unlovely — but the status code genuinely cannot distinguish
    /// these two, and calling a depleted balance "transient" is the more expensive mistake: it
    /// invites a retry loop against a condition only the user can clear. A missed match just leaves
    /// the old behaviour.</para>
    /// </summary>
    internal static bool LooksLikeBillingExhaustion(string detail) =>
        detail.Contains("credit", StringComparison.OrdinalIgnoreCase)
        || detail.Contains("billing", StringComparison.OrdinalIgnoreCase)
        || detail.Contains("insufficient funds", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Digs the human-readable message out of an error body. Both providers wrap it in
    /// <c>{"error": {"message": ...}}</c>; anything else falls back to a trimmed slice of the raw
    /// body, which still beats reporting only a status code.
    /// </summary>
    private static string ExtractMessage(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);

            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString() ?? string.Empty;
                }

                if (error.TryGetProperty("message", out var message))
                {
                    return message.GetString() ?? string.Empty;
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON. Fall through to the raw slice.
        }

        return payload.Length > 300 ? payload[..300] + "…" : payload;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing && _ownsClient)
        {
            Http.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
