using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Loadstar.Core.Configuration;

/// <summary>
/// Stores the provider API key encrypted with Windows DPAPI, scoped to the current user.
/// The ciphertext is useless to another account on the same machine and to another machine
/// entirely, which is the whole reason this isn't just a field in settings.json.
///
/// <para>Windows-only by construction. The rest of Loadstar.Core is platform-neutral so it stays
/// testable off-Windows, so this type is annotated rather than the whole assembly retargeted —
/// callers get a compile-time error instead of a runtime failure on another OS.</para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SecretStore
{
    private readonly string _path;

    /// <summary>Ties the ciphertext to this application, so an unrelated blob won't decrypt.</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Loadstar.ApiKey.v1");

    public SecretStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "credentials.bin");
    }

    public bool HasKey => File.Exists(_path);

    public void Save(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        var cipher = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(apiKey),
            Entropy,
            DataProtectionScope.CurrentUser);

        File.WriteAllBytes(_path, cipher);
    }

    /// <summary>
    /// Returns null when no key is stored, or when the stored blob can't be decrypted — which
    /// happens legitimately if the user's Windows profile changed. Callers re-prompt rather
    /// than treating it as a crash.
    /// </summary>
    public string? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var plain = ProtectedData.Unprotect(
                File.ReadAllBytes(_path),
                Entropy,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void Clear()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
