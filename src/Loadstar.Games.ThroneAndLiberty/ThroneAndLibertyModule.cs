using Loadstar.Core.Configuration;
using Loadstar.Core.Games;

namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// Throne and Liberty as a registered game module.
///
/// <para>Everything here already existed and was reached directly by the app — the capture geometry from
/// <see cref="ScreenRegions"/>, the window title from a literal in the shared settings record, the
/// knowledge metadata from <see cref="TlKnowledgePack"/>. This gathers it behind one seam so a second
/// game is a sibling folder rather than an edit to Core.</para>
///
/// <para><b>The process name is <c>TL</c>, and it is measured rather than guessed</b> — read off a live
/// client on 2026-08-04, build 1.443.22.7936, and matching what a working install has stored. It matters
/// that this is confirmed: process match is the PRIMARY way the window is found, because matching on
/// window title once selected a Firefox window that had a build page open, and the cost of being wrong
/// is sending a private screen to a third party. Shipping the real name means the default configuration
/// is correct out of the box instead of asking every player to pick their window.</para>
/// </summary>
public sealed class ThroneAndLibertyModule : IGameModule
{
    /// <summary>Matches the existing <see cref="LoadstarSettings.GameId"/> default, so nothing migrates.</summary>
    public string Id => "throne-and-liberty";

    public string DisplayName => "Throne and Liberty";

    /// <inheritdoc />
    public string? DefaultProcessName => "TL";

    public string DefaultWindowTitleMatch => "THRONE AND LIBERTY";

    public CaptureRegion FullWindow => ScreenRegions.FullWindow;

    public IReadOnlyList<CaptureRegion> PrivacyMasks => ScreenRegions.PrivacyMasks;

    public int KnowledgeTokens => TlKnowledgePack.EstimatedTokens;

    public IReadOnlyList<string> KnowledgeSections => TlKnowledgePack.Sections;

    /// <summary>
    /// The variant a player is on, which for this game is not a detail.
    ///
    /// <para>Russian servers run a T1-era build under a different publisher, predating the 4.0.0 item
    /// rewrite — Item Level, Succession, Trait Unlockstones and the 4.x currencies do not exist there,
    /// while Enhancement, Transfer and Sync, which the advice never mentions, may still be live. So a
    /// module is not always one ruleset, and the prompt already handles this by detecting a Russian
    /// client and saying its knowledge does not apply.</para>
    ///
    /// <para>Recorded here as documentation rather than behaviour: nothing dispatches on it yet, and the
    /// detection that matters happens from the screenshot, where the evidence actually is.</para>
    /// </summary>
    public static IReadOnlyList<string> KnownVariants =>
    [
        "global (Amazon Games) — patch 4.5.0, the version all knowledge here describes",
        "russia (Astrum) — T1 era, pre-4.0.0; advice must not be applied to it",
    ];
}
