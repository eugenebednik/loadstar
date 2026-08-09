using System.Net.Http;

using Loadstar.Core.Ai;

namespace Loadstar.App;

/// <summary>
/// Turns a failed request into something the player can read and act on.
///
/// <para><b>Why this exists.</b> Failures were shown as the raw exception message, in English, under an
/// English title, whatever language the app was set to. A player on Russian who hit Gemini's "model is
/// overloaded" got untranslated provider jargon and no indication of whose fault it was or whether retrying
/// would help — which are the three things that actually matter about a failure.</para>
///
/// <para><b>Whose fault it is comes first.</b> A provider under load is not the player's problem to solve,
/// and saying so plainly stops them re-checking their API key and their network over somebody else's
/// outage. The provider is NAMED rather than called "the service", because anonymity just invites the
/// question and the answer is already known.</para>
/// </summary>
internal static class AiFailure
{
    /// <summary>
    /// Whether the failure was the provider's rather than ours or the player's.
    ///
    /// <para>Rides on <see cref="AiProviderException.IsTransient"/>, which the providers already set for rate
    /// limits, 5xx and timeouts — exactly the cases where waiting is the correct response. Checked on the
    /// inner exception too, since a provider failure often arrives wrapped.</para>
    /// </summary>
    public static bool IsProviderOutage(Exception ex) =>
        ex is AiProviderException { IsTransient: true }
        || ex.InnerException is AiProviderException { IsTransient: true };

    /// <summary>The message to show, in the player's language.</summary>
    /// <param name="providerName">Display name, so an outage is attributed rather than anonymous.</param>
    public static string Describe(Exception ex, string providerName)
    {
        ArgumentNullException.ThrowIfNull(ex);

        if (IsProviderOutage(ex))
        {
            return string.Format(Strings.Get("error.providerBusy"), providerName);
        }

        return ex switch
        {
            AdviceParseException => string.Format(Strings.Get("error.unreadableReply"), providerName),
            AiProviderException => string.Format(Strings.Get("error.providerRefused"), providerName),
            HttpRequestException or TaskCanceledException => Strings.Get("error.network"),
            _ => Strings.Get("error.unexpected"),
        };
    }

    /// <summary>
    /// The dialog title, also localised. An English title over translated body text reads as a half-finished
    /// translation, which is a worse impression than either alone.
    /// </summary>
    public static string Title(Exception ex) =>
        IsProviderOutage(ex) ? Strings.Get("error.title.busy") : Strings.Get("error.title.failed");
}
