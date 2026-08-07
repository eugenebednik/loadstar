using System.Net.Http;

using Loadstar.Core.Net;

namespace Loadstar.App;

/// <summary>
/// The reachability check behind <see cref="ConnectivityMonitor"/>.
///
/// <para><b>Two hosts, either one counts.</b> One host being down is not the internet being down, and
/// picking a single one would make this app's main button depend on one company's uptime.</para>
///
/// <para><b>HEAD, with a short timeout.</b> Nothing needs the body, and a probe that takes ten seconds to
/// fail is useless to a dialog waiting to enable a button. Four seconds is longer than any working
/// connection needs and short enough that two failures still resolve inside a poll interval.</para>
///
/// <para>Deliberately NOT the AI provider's endpoint. Probing it would need the key, would count against
/// rate limits, and would turn a firewall rule about one host into "you have no internet". A provider that
/// is unreachable while the internet works surfaces as an error from the real request, which the app
/// already reports properly.</para>
/// </summary>
internal static class InternetProbe
{
    /// <summary>Anycast endpoints that answer from almost anywhere, and are not this project's business.</summary>
    private static readonly string[] Hosts =
    [
        "https://cloudflare.com/cdn-cgi/trace",
        "https://www.google.com/generate_204",
    ];

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

    public static async Task<bool> IsReachableAsync(CancellationToken cancellationToken)
    {
        foreach (var host in Hosts)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, host);
                using var response = await Http
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                // Any answer at all proves the round trip. Not IsSuccessStatusCode: a 403 or a 405 came
                // from the far end, which is the only thing being asked about here.
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Try the next host before concluding anything.
            }
        }

        return false;
    }
}
