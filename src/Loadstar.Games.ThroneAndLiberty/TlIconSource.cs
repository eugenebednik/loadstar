namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// Turns a catalogue icon path into the URL that actually serves the image.
///
/// <para><b>The rule is not guessable and was not guessed.</b> The catalogue stores paths like
/// <c>/assets/Game/Image/Icon/Item_128/Equip/Armor/P_Set_FA_M_PT_00022B.P_Set_FA_M_PT_00022B</c>. The
/// repeated stem after the dot looks like a file extension and is not — it is Unreal's
/// <c>Package.AssetName</c> convention, so the real filename is the stem once, with <c>.webp</c>. Every
/// obvious URL built from the path verbatim returns HTTP 200 with questlog's SPA shell as
/// <c>text/html</c>, which is the worst kind of wrong answer: a success status carrying a web page where
/// an image was expected.</para>
///
/// <para>Found by reading questlog's own Nuxt bundle, which builds icon URLs against
/// <c>https://cdn.questlog.gg/throne-and-liberty</c>. Verified against three items: real
/// <c>image/webp</c>, RIFF magic bytes, 200x200.</para>
///
/// <para><b>Why this matters at all.</b> CLAUDE.md has said from the start that a vision model asked to
/// name a 40px icon returns plausible wrong names, and that a local index is "deterministic, free, and
/// offline — strictly better". It was never built because nobody had established that the icons could be
/// obtained. They can.</para>
/// </summary>
public static class TlIconSource
{
    /// <summary>The host questlog's own client uses. Not a CDN this project controls.</summary>
    public const string BaseUrl = "https://cdn.questlog.gg/throne-and-liberty";

    /// <summary>
    /// The URL for a catalogue <c>icon</c> path, or null when the path is absent or malformed.
    ///
    /// <para>Null rather than a constructed-anyway URL: 251 of the catalogue's 1,773 items share an icon
    /// with another item and some carry none at all, and a request for a nonexistent asset costs a round
    /// trip to learn nothing.</para>
    /// </summary>
    public static string? UrlFor(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return null;
        }

        var path = iconPath.Trim().Replace('\\', '/');

        if (!path.StartsWith('/'))
        {
            path = '/' + path;
        }

        var slash = path.LastIndexOf('/');

        if (slash < 0 || slash == path.Length - 1)
        {
            return null;
        }

        var directory = path[..slash];
        var name = path[(slash + 1)..];

        // Take everything before the FIRST dot. Package.AssetName repeats the stem, and some names
        // legitimately contain no dot at all.
        var dot = name.IndexOf('.');
        var stem = dot < 0 ? name : name[..dot];

        return stem.Length == 0 ? null : $"{BaseUrl}{directory}/{stem}.webp";
    }

    /// <summary>
    /// A stable, filesystem-safe cache filename for an icon path.
    ///
    /// <para>Keyed on the icon rather than the item id on purpose: icons are shared between items, so
    /// keying on the id would download the same bytes several times. 1,773 items resolve to 1,522
    /// distinct icons.</para>
    /// </summary>
    public static string? CacheFileNameFor(string? iconPath)
    {
        var url = UrlFor(iconPath);

        if (url is null)
        {
            return null;
        }

        var relative = url[BaseUrl.Length..].TrimStart('/');

        return relative.Replace('/', '_');
    }
}
