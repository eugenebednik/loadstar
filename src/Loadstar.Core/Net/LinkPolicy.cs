namespace Loadstar.Core.Net;

/// <summary>
/// Decides which links the app will open in the player's browser.
///
/// <para><b>Why there is a policy at all.</b> The advice text is model output, and that output is shaped by
/// screenshots of a game and by build names other players wrote — neither of which this app controls. A link
/// is the one part of an answer that does something when touched, so the host is checked here rather than
/// trusted from the string. Anything not allowed stays plain text the player can read and copy, which costs
/// them one paste and costs an attacker the click.</para>
///
/// <para>In Core rather than beside the window that uses it, so the rule can be tested. A host allowlist
/// with no tests is a rule nobody has checked.</para>
/// </summary>
public static class LinkPolicy
{
    /// <summary>
    /// The only site whose links open. questlog is where builds live and is the sole reason the advice
    /// carries a URL at all.
    /// </summary>
    public const string AllowedHost = "questlog.gg";

    /// <summary>
    /// Whether <paramref name="link"/> may be opened, and its parsed form when so.
    /// </summary>
    public static bool IsAllowed(string? link, out Uri? uri)
    {
        uri = null;

        if (string.IsNullOrWhiteSpace(link))
        {
            return false;
        }

        if (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out var parsed))
        {
            return false;
        }

        // http and https only. A file:, javascript: or ms-settings: URI handed to ShellExecute does
        // something quite different from opening a web page, and several of those somethings are worse.
        if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        if (!IsAllowedHost(parsed.Host))
        {
            return false;
        }

        uri = parsed;

        return true;
    }

    /// <summary>
    /// Whether a host is the allowed site or a subdomain of it.
    ///
    /// <para><b>Matched on the END with a leading dot, never with Contains.</b> A host like
    /// <c>questlog.gg.example.com</c> contains the allowed name and belongs to somebody else entirely, and
    /// <c>notquestlog.gg</c> ends with it without the dot. Both are the standard way an allowlist gets
    /// walked past.</para>
    /// </summary>
    private static bool IsAllowedHost(string host) =>
        host.Equals(AllowedHost, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith($".{AllowedHost}", StringComparison.OrdinalIgnoreCase);
}
