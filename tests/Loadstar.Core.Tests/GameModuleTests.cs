using Loadstar.Core.Configuration;
using Loadstar.Core.Games;
using Loadstar.Games.ThroneAndLiberty;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// The seam a game picker needs. One game ships today, so most of what matters here is that the seam
/// behaves correctly when the stored game is NOT the one that ships — which is the situation a second
/// game, or a downgrade, creates.
/// </summary>
public sealed class GameModuleTests
{
    private static GameCatalog Catalog => new(new ThroneAndLibertyModule());

    [Fact]
    public void ThroneAndLibertyResolvesByItsStoredId()
    {
        // The id must match the existing settings default or every install would look unknown.
        Assert.Equal("throne-and-liberty", new LoadstarSettings().GameId);
        Assert.NotNull(Catalog.Find("throne-and-liberty"));
        Assert.Equal("Throne and Liberty", Catalog.Find("throne-and-liberty")!.DisplayName);
    }

    /// <summary>
    /// An unknown game returns null rather than quietly substituting another. Advising on a game the
    /// player is not playing is the confidently-wrong failure this codebase keeps guarding against.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("some-other-mmo")]
    public void AnUnknownGameIsNotSilentlySubstituted(string? id)
    {
        Assert.Null(Catalog.Find(id));
    }

    [Fact]
    public void IdLookupIsCaseInsensitiveBecauseItComesFromAFileAPersonCanEdit()
    {
        Assert.NotNull(Catalog.Find("Throne-And-Liberty"));
        Assert.NotNull(Catalog.Find("THRONE-AND-LIBERTY"));
    }

    /// <summary>Two modules sharing an id would make the stored game resolve to whichever registered last.</summary>
    [Fact]
    public void DuplicateIdsAreRejectedAtConstruction()
    {
        Assert.Throws<ArgumentException>(() =>
            new GameCatalog(new ThroneAndLibertyModule(), new ThroneAndLibertyModule()));

        Assert.Throws<ArgumentException>(() => new GameCatalog());
    }

    /// <summary>With one game there is nothing to choose, so a picker should not be shown.</summary>
    [Fact]
    public void HasChoiceIsFalseWhileOnlyOneGameShips()
    {
        Assert.False(Catalog.HasChoice);
        Assert.Single(Catalog.All);
        Assert.Equal("throne-and-liberty", Catalog.Default.Id);
    }

    /// <summary>
    /// The process name must be the MEASURED one. Getting this wrong is a privacy failure, not a
    /// cosmetic one: title matching once selected a Firefox window with a build page open.
    /// </summary>
    [Fact]
    public void TheModuleShipsTheConfirmedProcessName()
    {
        var module = new ThroneAndLibertyModule();

        Assert.Equal("TL", module.DefaultProcessName);
        Assert.Equal("THRONE AND LIBERTY", module.DefaultWindowTitleMatch);
    }

    /// <summary>
    /// Stored settings must win over the module's defaults, or an upgrade would override a window the
    /// player deliberately picked.
    /// </summary>
    [Fact]
    public void StoredCaptureSettingsBeatTheModuleDefaults()
    {
        var module = new ThroneAndLibertyModule();

        var configured = new CaptureSettings { WindowProcessName = "SomethingElse", WindowTitleMatch = "MY WINDOW" }
            .ToWindowTarget(module.DefaultProcessName, module.DefaultWindowTitleMatch);

        Assert.Equal("SomethingElse", configured.ProcessName);
        Assert.Equal("MY WINDOW", configured.TitleMatch);

        // And an empty settings record falls through to the module, so a fresh install is correct.
        var fresh = new CaptureSettings()
            .ToWindowTarget(module.DefaultProcessName, module.DefaultWindowTitleMatch);

        Assert.Equal("TL", fresh.ProcessName);
        Assert.Equal("THRONE AND LIBERTY", fresh.TitleMatch);
    }

    /// <summary>
    /// Core must no longer carry one game's window title. That literal in the shared settings record is
    /// exactly what had to go before a second game was possible.
    /// </summary>
    [Fact]
    public void CoreNoLongerHardcodesOneGamesWindowTitle()
    {
        Assert.Equal(string.Empty, new CaptureSettings().WindowTitleMatch);
    }

    /// <summary>The module exposes the capture geometry the app used to reach for directly.</summary>
    [Fact]
    public void TheModuleCarriesItsOwnCaptureGeometry()
    {
        var module = new ThroneAndLibertyModule();

        Assert.Equal(1.0, module.FullWindow.Width);
        Assert.Equal(1.0, module.FullWindow.Height);
        // EMPTY, and asserted rather than left unstated. This game declares no fixed mask because the chat
        // panel it was meant to cover is draggable and resizable, and the fixed rectangle was black-boxing the
        // character sheet's stat column instead — see ScreenRegions.PrivacyMasks. Asserting emptiness means
        // re-adding a blanket mask breaks a test that says why it was removed, rather than silently
        // reintroducing the bug.
        Assert.Empty(module.PrivacyMasks);

        // Knowledge metadata, for the settings page.
        Assert.True(module.KnowledgeTokens > 1000);
        Assert.NotEmpty(module.KnowledgeSections);
    }
}
