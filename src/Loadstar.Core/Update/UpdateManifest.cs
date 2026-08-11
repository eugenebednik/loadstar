using System.Text.Json;
using System.Text.Json.Serialization;

namespace Loadstar.Core.Update;

/// <summary>
/// What the newest release says about itself: its version, and one installer per language with a digest.
///
/// <para><b>Published as a release asset rather than read from the GitHub API.</b> The API is rate limited
/// per IP, which a desktop app checking on every launch would share with everyone behind the same NAT, and
/// it returns a large payload to answer one question. An asset at
/// <c>/releases/latest/download/latest.json</c> is a CDN redirect with no rate limit, and it is inherently
/// tied to whichever release GitHub considers latest — so there is no second place recording the version that
/// could disagree with the release itself.</para>
///
/// <para><b>The digest is not decoration.</b> Whatever this points at gets EXECUTED as an installer, so it is
/// verified before it runs. TLS already authenticates the host, but a truncated download is a normal event on
/// a desktop and a partially-written MSI is exactly the sort of thing that fails halfway through modifying
/// the machine.</para>
/// </summary>
public sealed record UpdateManifest(
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("installers")] IReadOnlyList<UpdateInstaller>? Installers)
{
    /// <summary>
    /// Parses the manifest, or returns null when it is missing, malformed or carries no usable installer.
    ///
    /// <para>Null rather than throwing: a failed update check must be invisible. The app is a tray tool
    /// somebody is using to play a game, and a dialog about a malformed JSON file is worse than silence.</para>
    /// </summary>
    public static UpdateManifest? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json);

            if (manifest?.Version is null || AppVersion.Parse(manifest.Version) is null)
            {
                return null;
            }

            return manifest.Installers is null or { Count: 0 } ? null : manifest;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The installer for a language, falling back to English.
    ///
    /// <para>English rather than nothing: a release that has not yet been built in some language should still
    /// be installable, and the installer's own UI language is cosmetic next to being on the current
    /// version.</para>
    /// </summary>
    public UpdateInstaller? For(string languageCode)
    {
        if (Installers is null)
        {
            return null;
        }

        return Installers.FirstOrDefault(i =>
                   string.Equals(i.Language, languageCode, StringComparison.OrdinalIgnoreCase))
               ?? Installers.FirstOrDefault(i =>
                   string.Equals(i.Language, "en", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>One language's installer, with the digest that authorises running it.</summary>
public sealed record UpdateInstaller(
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("file")] string? File,
    [property: JsonPropertyName("sha256")] string? Sha256,
    [property: JsonPropertyName("bytes")] long Bytes);
