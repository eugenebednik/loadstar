using Loadstar.Core.Configuration;

namespace Loadstar.Core.Games;

/// <summary>
/// One game Loadstar can advise on.
///
/// <para><b>Why this exists now.</b> The player will choose their game when the app loads, and until
/// there is a seam there is nothing to choose between — Throne and Liberty is currently wired in
/// directly, right down to a <c>"THRONE AND LIBERTY"</c> default sitting in the shared settings record.
/// This interface is what a second game slots into.</para>
///
/// <para><b>Scope, honestly.</b> This first cut covers what CHOOSING a game needs: identity for the
/// picker, how to find the game's window, and where on that window it is not safe to look. Prompt
/// building and the boss timer are still Throne and Liberty types held directly by the app — they carry
/// game-specific shapes (<c>DerivedTargets</c>, <c>BossSpawn</c>) that a shared interface cannot express
/// without inventing a lowest common denominator before there is a second game to learn from. Those are
/// the next two seams, and guessing at them now would produce an abstraction fitted to one example.</para>
///
/// <para><b>Dependency direction.</b> This lives in Core and game modules depend on Core, never the
/// reverse — so Core cannot enumerate the modules. The composition root registers them; see
/// <see cref="GameCatalog"/>.</para>
/// </summary>
public interface IGameModule
{
    /// <summary>
    /// Stable identifier, stored in settings. Kebab-case, matching the existing
    /// <see cref="LoadstarSettings.GameId"/> value so no migration is needed.
    /// </summary>
    string Id { get; }

    /// <summary>The name shown in the picker. Not localised: it is the game's own title.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Process name to capture, without the extension — the PRIMARY way the window is found.
    ///
    /// <para>Primary rather than the title because matching on title once selected a Firefox window: the
    /// player had a build page open and the tab title contained the game's name. A process name does not
    /// collide with browser tabs, and the cost of getting this wrong is sending someone's private screen
    /// to a third party.</para>
    /// </summary>
    string? DefaultProcessName { get; }

    /// <summary>
    /// Window-title substring, used only as a fallback when no process is configured. Subordinate to
    /// <see cref="DefaultProcessName"/> for the reason given there.
    /// </summary>
    string DefaultWindowTitleMatch { get; }

    /// <summary>The whole game window — the default capture, since panels are draggable.</summary>
    CaptureRegion FullWindow { get; }

    /// <summary>
    /// Areas blacked out before a capture leaves the machine. Party lists and chat are other people's
    /// names, and they are nobody's business but theirs.
    /// </summary>
    IReadOnlyList<CaptureRegion> PrivacyMasks { get; }

    /// <summary>
    /// Rough size of the knowledge this module carries, for the settings page. Shown so the token cost
    /// of a session is visible rather than mysterious.
    /// </summary>
    int KnowledgeTokens { get; }

    /// <summary>Knowledge section names, for the settings page.</summary>
    IReadOnlyList<string> KnowledgeSections { get; }
}
