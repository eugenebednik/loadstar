using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Loadstar.Core.Configuration;

/// <summary>
/// Stores one API key per provider, each encrypted with Windows DPAPI and scoped to the current
/// user. The ciphertext is useless to another account on the same machine and to another machine
/// entirely, which is the whole reason this isn't just a field in settings.json.
///
/// <para>Keys are held per provider rather than as a single value, because switching provider must
/// not throw away the key for the one you switched away from. A user trying Gemini for an afternoon
/// should find their Anthropic key still there afterwards.</para>
///
/// <para>Windows-only by construction. The rest of Loadstar.Core is platform-neutral so it stays
/// testable off-Windows, so this type is annotated rather than the whole assembly retargeted —
/// callers get a compile-time error instead of a runtime failure on another OS.</para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SecretStore
{
    private readonly string _directory;

    /// <summary>Ties the ciphertext to this application, so an unrelated blob won't decrypt.</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Loadstar.ApiKey.v1");

    /// <summary>
    /// Where the key lived when there was only one provider. Migrated on first use — see
    /// <see cref="MigrateLegacyKey"/>.
    /// </summary>
    private const string LegacyFileName = "credentials.bin";

    public SecretStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _directory = directory;

        MigrateLegacyKey();
    }

    public bool HasKey(AiProviderKind provider) => File.Exists(PathFor(provider));

    public void Save(AiProviderKind provider, string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        var cipher = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(apiKey),
            Entropy,
            DataProtectionScope.CurrentUser);

        File.WriteAllBytes(PathFor(provider), cipher);
    }

    /// <summary>
    /// Returns null when no key is stored, or when the stored blob can't be decrypted — which
    /// happens legitimately if the user's Windows profile changed. Callers re-prompt rather
    /// than treating it as a crash.
    /// </summary>
    public string? Load(AiProviderKind provider)
    {
        var path = PathFor(provider);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var plain = ProtectedData.Unprotect(
                File.ReadAllBytes(path),
                Entropy,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>
    /// The key to actually use: the encrypted store first, then the provider's conventional
    /// environment variable.
    ///
    /// <para>Centralised because the two shells previously each spelled this order out with
    /// <c>ANTHROPIC_API_KEY</c> hardcoded. That was already a latent inconsistency, and with three
    /// providers it would have become three copies of a two-branch lookup that must agree.</para>
    /// </summary>
    public string? Resolve(AiProviderKind provider)
    {
        var stored = Load(provider);

        if (!string.IsNullOrWhiteSpace(stored))
        {
            return stored;
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(
            Ai.AiCatalog.For(provider).EnvironmentVariable);

        return string.IsNullOrWhiteSpace(fromEnvironment) ? null : fromEnvironment;
    }

    public void Clear(AiProviderKind provider)
    {
        var path = PathFor(provider);

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string PathFor(AiProviderKind provider) =>
        Path.Combine(_directory, $"credentials-{provider.ToString().ToLowerInvariant()}.bin");

    /// <summary>
    /// Moves a pre-multi-provider key into the Anthropic slot.
    ///
    /// <para>A <b>rename</b>, deliberately, rather than decrypt-then-re-encrypt. The entropy is
    /// unchanged, so the bytes stay valid where they land — and that means the migration also
    /// succeeds for a blob that can no longer be decrypted at all (a changed Windows profile). A
    /// decrypt/re-encrypt round trip would quietly destroy that key instead of leaving it in place
    /// for the user to overwrite.</para>
    ///
    /// <para>Silent on failure: a locked or unreadable legacy file leaves the user with an empty key
    /// field, which is recoverable by typing it again. Throwing here would take down app startup
    /// over a file that is, by this point, obsolete.</para>
    /// </summary>
    private void MigrateLegacyKey()
    {
        var legacy = Path.Combine(_directory, LegacyFileName);
        var destination = PathFor(AiProviderKind.Anthropic);

        if (!File.Exists(legacy) || File.Exists(destination))
        {
            return;
        }

        try
        {
            File.Move(legacy, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left in place; the user re-enters the key. See the note above.
        }
    }
}
