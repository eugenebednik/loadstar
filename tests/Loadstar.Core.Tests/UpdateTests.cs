using Loadstar.Core.Configuration;
using Loadstar.Core.Update;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// The update check's decision logic.
///
/// <para>The property that matters most is the <b>refusal to offer a downgrade</b>. CI numbers rolling builds
/// <c>major.minor.run_number</c>, so a development build is deliberately AHEAD of the release before it —
/// 0.23.62 against 0.22.0. Anyone on a rolling build must therefore be offered nothing, or they would be
/// walked through a download and a UAC prompt only for the installer to refuse with a downgrade error.</para>
/// </summary>
public sealed class UpdateTests
{
    [Theory]
    [InlineData("0.22.0", 0, 22, 0)]
    [InlineData("v0.22.0", 0, 22, 0)]
    [InlineData("0.22.0.0", 0, 22, 0)]        // an assembly reports four components
    [InlineData(" 1.4.37 ", 1, 4, 37)]
    public void VersionsParse(string text, int major, int minor, int build) =>
        Assert.Equal((major, minor, build), AppVersion.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0.22")]                       // too few components to compare
    [InlineData("nightly")]
    [InlineData("0.x.0")]
    [InlineData("-1.2.3")]
    public void RubbishVersionsAreRejected(string? text) => Assert.Null(AppVersion.Parse(text));

    /// <summary>
    /// The two forms of the same version must compare equal, not merely close. An assembly says 0.22.0.0 and
    /// a release says 0.22.0; comparing them with <see cref="Version"/> directly makes them different,
    /// because one has a revision of 0 and the other of -1 — which would offer the running version as an
    /// update, on every check, forever.
    /// </summary>
    [Fact]
    public void ThreeAndFourComponentFormsOfOneVersionAreNotAnUpdate()
    {
        Assert.False(AppVersion.IsNewer("0.22.0", "0.22.0.0"));
        Assert.False(AppVersion.IsNewer("0.22.0.0", "0.22.0"));
    }

    [Theory]
    [InlineData("0.24.0", "0.22.0", true)]
    [InlineData("1.0.0", "0.99.99", true)]
    [InlineData("0.22.1", "0.22.0", true)]
    [InlineData("0.22.0", "0.22.0", false)]
    public void NewerVersionsAreOffered(string candidate, string current, bool expected) =>
        Assert.Equal(expected, AppVersion.IsNewer(candidate, current));

    /// <summary>
    /// THE DOWNGRADE REFUSAL, spelled out with the real numbers from the versioning scheme.
    /// </summary>
    [Theory]
    [InlineData("0.22.0", "0.23.62")]          // rolling build, ahead of the release by design
    [InlineData("0.22.0", "0.22.5")]
    [InlineData("0.22.0", "1.0.0")]
    public void ARollingBuildIsNeverOfferedAnOlderRelease(string release, string running) =>
        Assert.False(AppVersion.IsNewer(release, running));

    [Fact]
    public void AnUnreadableVersionIsNotGroundsForAnUpdate()
    {
        Assert.False(AppVersion.IsNewer(null, "0.22.0"));
        Assert.False(AppVersion.IsNewer("0.24.0", null));
        Assert.False(AppVersion.IsNewer("garbage", "0.22.0"));
    }

    // ---------------------------------------------------------------- manifest

    private const string Good = """
        {
          "version": "0.24.0",
          "installers": [
            { "language": "en", "file": "Loadstar-0.24.0-x64-en.msi", "sha256": "AA", "bytes": 100 },
            { "language": "ru", "file": "Loadstar-0.24.0-x64-ru.msi", "sha256": "BB", "bytes": 101 }
          ]
        }
        """;

    [Fact]
    public void AGoodManifestParses()
    {
        var manifest = UpdateManifest.Parse(Good);

        Assert.NotNull(manifest);
        Assert.Equal("0.24.0", manifest!.Version);
        Assert.Equal(2, manifest.Installers!.Count);
    }

    [Fact]
    public void TheInstallerIsChosenByLanguage() =>
        Assert.Equal("Loadstar-0.24.0-x64-ru.msi", UpdateManifest.Parse(Good)!.For("ru")!.File);

    /// <summary>
    /// A language with no installer falls back to English rather than to nothing: the installer's own UI
    /// language is cosmetic next to being on the current version.
    /// </summary>
    [Fact]
    public void AMissingLanguageFallsBackToEnglish() =>
        Assert.Equal("Loadstar-0.24.0-x64-en.msi", UpdateManifest.Parse(Good)!.For("ja")!.File);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{ "installers": [] }""")]                                  // no version
    [InlineData("""{ "version": "0.24.0" }""")]                               // no installers
    [InlineData("""{ "version": "0.24.0", "installers": [] }""")]             // empty installers
    [InlineData("""{ "version": "nightly", "installers": [{ "language": "en" }] }""")]
    public void AnUnusableManifestIsRejectedRatherThanPartlyTrusted(string? json) =>
        Assert.Null(UpdateManifest.Parse(json));

    // ---------------------------------------------------------------- language mapping

    /// <summary>
    /// Every language maps to a code that matches a real installer filename. A typo here would silently send
    /// someone to a 404 and look like "no update available".
    /// </summary>
    [Fact]
    public void EveryLanguageMapsToAShippedInstallerCode()
    {
        string[] shipped = ["en", "ru", "uk", "es", "de", "fr", "ja", "ko", "zh"];

        foreach (var language in Enum.GetValues<AppLanguage>())
        {
            Assert.Contains(AppLanguages.InstallerCode(language), shipped);
        }
    }

    /// <summary>
    /// System resolves against the OS rather than defaulting to English — which is what separates this from
    /// IsoCode, whose job is different.
    /// </summary>
    [Fact]
    public void SystemResolvesRatherThanFallingStraightToEnglish()
    {
        var resolved = AppLanguages.Resolve(AppLanguage.System);

        Assert.Equal(AppLanguages.IsoCode(resolved), AppLanguages.InstallerCode(AppLanguage.System));
    }
}
