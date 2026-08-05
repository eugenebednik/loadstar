using Loadstar.Core.Configuration;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Covers the per-provider key split, and specifically the migration off the single-key layout.
///
/// <para>The migration is the part that can silently cost someone their configured key, and it only
/// happens once per install — so it is not something a user would hit twice and report clearly.
/// The <see cref="OperatingSystem.IsWindows"/> guards are what let these live in a plain
/// <c>net8.0</c> test project: <see cref="SecretStore"/> is annotated Windows-only rather than the
/// assembly being retargeted, deliberately, so Loadstar.Core stays testable off-Windows.</para>
/// </summary>
public sealed class SecretStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "loadstar-secret-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Legacy_key_file_moves_into_the_anthropic_slot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_directory);

        // Deliberately not valid DPAPI ciphertext. Migration is a rename, so it must succeed
        // without decrypting — a decrypt-then-re-encrypt round trip would destroy a key belonging
        // to a Windows profile that has since changed, which is exactly when it is least
        // recoverable.
        File.WriteAllBytes(Path.Combine(_directory, "credentials.bin"), [1, 2, 3, 4]);

        _ = new SecretStore(_directory);

        Assert.False(File.Exists(Path.Combine(_directory, "credentials.bin")));
        Assert.True(File.Exists(Path.Combine(_directory, "credentials-anthropic.bin")));
    }

    [Fact]
    public void Migration_never_overwrites_a_key_already_stored_for_anthropic()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_directory);

        var destination = Path.Combine(_directory, "credentials-anthropic.bin");

        File.WriteAllBytes(destination, [9, 9, 9]);
        File.WriteAllBytes(Path.Combine(_directory, "credentials.bin"), [1, 2, 3]);

        _ = new SecretStore(_directory);

        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(destination));
    }

    [Fact]
    public void Keys_are_held_separately_per_provider()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new SecretStore(_directory);

        store.Save(AiProviderKind.Anthropic, "sk-ant-example");
        store.Save(AiProviderKind.Google, "AIza-example");

        // The point of the split: trying a second provider must not cost you the first one's key.
        Assert.Equal("sk-ant-example", store.Load(AiProviderKind.Anthropic));
        Assert.Equal("AIza-example", store.Load(AiProviderKind.Google));
        Assert.Null(store.Load(AiProviderKind.OpenAi));
    }

    [Fact]
    public void Clearing_one_provider_leaves_the_others_alone()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new SecretStore(_directory);

        store.Save(AiProviderKind.Anthropic, "sk-ant-example");
        store.Save(AiProviderKind.OpenAi, "sk-openai-example");

        store.Clear(AiProviderKind.OpenAi);

        Assert.Null(store.Load(AiProviderKind.OpenAi));
        Assert.Equal("sk-ant-example", store.Load(AiProviderKind.Anthropic));
    }

    [Fact]
    public void Resolve_prefers_the_stored_key_over_the_environment()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new SecretStore(_directory);

        store.Save(AiProviderKind.Anthropic, "stored");

        Assert.Equal("stored", store.Resolve(AiProviderKind.Anthropic));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
