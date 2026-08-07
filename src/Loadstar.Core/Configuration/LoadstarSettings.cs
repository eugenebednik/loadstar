using System.Text.Json.Serialization;

namespace Loadstar.Core.Configuration;

/// <summary>
/// Everything the user can change. Persisted as JSON next to the executable's user data;
/// the API key is the one thing that never appears here — see <see cref="SecretStore"/>.
/// </summary>
public sealed record LoadstarSettings
{
    public string GameId { get; init; } = "throne-and-liberty";

    public AiSettings Ai { get; init; } = new();
    public CaptureSettings Capture { get; init; } = new();
    public OverlaySettings Overlay { get; init; } = new();
    public GameSettings Game { get; init; } = new();

    /// <summary>
    /// Interface language. <see cref="AppLanguage.System"/> follows Windows.
    /// <para>Independent of the game client's language and of the language the player types in —
    /// all three can differ, and commonly do for players whose language the game does not ship.</para>
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppLanguage Language { get; init; } = AppLanguage.System;

    /// <summary>False until the user completes the first-run consent screen. Gates all capture.</summary>
    public bool CaptureConsentGiven { get; init; }

    public string? ConsentVersionAccepted { get; init; }
}

public sealed record AiSettings
{
    /// <summary>
    /// Google, because it is the only provider with a free tier.
    ///
    /// <para>This defaulted to Anthropic, which is the better model but a worse default: Anthropic and
    /// OpenAI both bill per token from a prepaid balance and neither includes API access with a
    /// consumer subscription. So a new user following the shortest path hit a provider they could not
    /// use without first setting up billing — and the app cannot help them at all until they do.</para>
    ///
    /// <para>Gemini's free tier has rate limits and its requests may be used for training, both of
    /// which the settings dialog states plainly. That is a trade the user can see and change; a
    /// paywall on first run is not.</para>
    /// </summary>
    public AiProviderKind Provider { get; init; } = AiProviderKind.Google;

    /// <summary>Model id. Must belong to <see cref="Provider"/> above, or the factory rejects it.</summary>
    public string Model { get; init; } = "gemini-3.6-flash";

    /// <summary>Hard ceiling on spend. Analysis stops rather than silently costing more.</summary>
    public decimal MonthlyBudgetUsd { get; init; } = 10m;

    /// <summary>Reasoning effort for providers that support it.</summary>
    public string Effort { get; init; } = "medium";

    /// <summary>
    /// The model last chosen for each provider, so switching provider and switching back does not
    /// silently reset the choice to that provider's default.
    /// <para>Keyed by <see cref="AiProviderKind"/> name. <see cref="Model"/> stays the single value
    /// the request actually uses — this is only the memory behind the dropdown.</para>
    /// </summary>
    public IReadOnlyDictionary<string, string> ModelByProvider { get; init; }
        = new Dictionary<string, string>();
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AiProviderKind
{
    Anthropic,
    OpenAi,
    Google,
}

public sealed record CaptureSettings
{
    /// <summary>Seconds between snapshots. Below 30 costs more than it helps.</summary>
    public int IntervalSeconds { get; init; } = 120;

    /// <summary>
    /// Process name of the game client, e.g. <c>TL</c>. The reliable way to identify the window:
    /// a title substring matches whatever the player has open about the game, which once selected
    /// a browser showing a build guide.
    /// </summary>
    public string? WindowProcessName { get; init; }

    /// <summary>
    /// Window title substring. A fallback, subordinate to <see cref="WindowProcessName"/>.
    ///
    /// <para><b>Empty by default, and the game module supplies the fallback.</b> This used to default to
    /// "THRONE AND LIBERTY" — one game's title hardcoded into the settings record every game shares,
    /// which is precisely the thing that has to go before the player can choose a game. Core cannot know
    /// which game is selected, so it no longer pretends to.</para>
    /// </summary>
    public string WindowTitleMatch { get; init; } = string.Empty;

    /// <summary>Permits a title match to select a browser or chat app. Off unless the user means it.</summary>
    public bool AllowAnyProcess { get; init; }

    /// <summary>
    /// Optional crop, as fractions of the window (0..1). Restricting this is the main lever
    /// for keeping other players' names out of what gets sent to the provider.
    /// </summary>
    public CaptureRegion? Region { get; init; }

    /// <summary>
    /// Only capture when the user presses the hotkey, rather than on a timer.
    /// <para>Defaults to true: the product is "ask it when you want to know", so a user-initiated
    /// snapshot is the primary path and timed capture is the opt-in.</para>
    /// </summary>
    public bool ManualOnly { get; init; } = true;

    /// <summary>
    /// Resolves the configured target into the form the capture source consumes.
    /// </summary>
    /// <param name="defaultProcessName">
    /// The selected game's process name, used when the player has not picked a window. Process match is
    /// the primary route because matching on title once selected a Firefox window that had a build page
    /// open, and the cost of that mistake is a private screen sent to a third party.
    /// </param>
    /// <param name="defaultTitleMatch">
    /// The selected game's window title, used only when nothing else identifies the window. Supplied by
    /// the caller because Core does not know which game is selected.
    /// </param>
    public Capture.WindowTarget ToWindowTarget(
        string? defaultProcessName = null,
        string? defaultTitleMatch = null)
    {
        var process = string.IsNullOrWhiteSpace(WindowProcessName) ? defaultProcessName : WindowProcessName;
        var title = string.IsNullOrWhiteSpace(WindowTitleMatch) ? defaultTitleMatch : WindowTitleMatch;

        return new Capture.WindowTarget
        {
            ProcessName = string.IsNullOrWhiteSpace(process)
                ? null
                : Capture.WindowTargeting.NormalizeProcessName(process),
            TitleMatch = title ?? string.Empty,
            AllowAnyProcess = AllowAnyProcess,
        };
    }
}

public sealed record CaptureRegion
{
    public double Left { get; init; }
    public double Top { get; init; }
    public double Width { get; init; } = 1.0;
    public double Height { get; init; } = 1.0;
}

public sealed record OverlaySettings
{
    public double Left { get; init; } = 24;
    public double Top { get; init; } = 24;
    public double Width { get; init; } = 380;
    /// <summary>
    /// Overlay translucency. Lower than it looks like it should be, deliberately: this sits on top of
    /// a game the player is actually looking at, and 0.88 read as a solid panel covering the corner of
    /// the screen rather than as an overlay. Legibility still wins over subtlety, so it stops well
    /// short of the 0.2 floor the overlay clamps to.
    /// </summary>
    public double Opacity { get; init; } = 0.72;
    public bool ClickThrough { get; init; } = true;
    public string ToggleHotkey { get; init; } = "Ctrl+Alt+L";
    public string CaptureHotkey { get; init; } = "Ctrl+Alt+S";

    /// <summary>Show the transparent boss countdown on screen.</summary>
    public bool ShowBossCountdown { get; init; }

    /// <summary>Where the countdown sits, in screen pixels.</summary>
    public double CountdownLeft { get; init; } = 24;

    public double CountdownTop { get; init; } = 24;

    /// <summary>
    /// Click-through, and therefore immovable. Off by default: click-through and draggable are
    /// mutually exclusive, so the overlay has to start movable or the user can never position it.
    /// </summary>
    public bool CountdownLocked { get; init; }
}

public sealed record GameSettings
{
    /// <summary>
    /// questlog.gg build URL, or the bare slug. <b>Optional.</b>
    ///
    /// <para>It used to be required before any advice was given, which put a chore in front of the
    /// first useful answer for no good reason: most of what the advice is built on is visible on the
    /// screen. When it is set it still wins, because it states the player's intended axis and role —
    /// the one thing a screenshot cannot reveal.</para>
    /// </summary>
    public string? BuildUrl { get; init; }

    /// <summary>
    /// The two weapon ids last read off the player's character sheet, e.g. <c>["orb", "wand"]</c>.
    ///
    /// <para>Two weapons name a class (<see cref="Loadstar.Core.Configuration.GameSettings"/> has no
    /// dependency on the table, but see <c>TlClasses</c>), which is what lets the app look up what the
    /// community plays for that class and OFFER a build instead of demanding one.</para>
    ///
    /// <para>Persisted because the read and the use are necessarily on different turns: the model has
    /// to see a character sheet before the weapons are known, and by then the request that would have
    /// used them has already gone. Storing them means the offer is ready the next time the player asks
    /// anything, rather than needing them to open the character sheet twice.</para>
    ///
    /// <para>Empty is the honest initial state, and it must never be guessed at — a wrong pair names a
    /// different class entirely and would recommend builds for a character the player is not playing.</para>
    /// </summary>
    public IReadOnlyList<string> LastWeapons { get; init; } = [];

    /// <summary>
    /// Whether <see cref="LastWeapons"/> is settled, or still a single unconfirmed guess.
    ///
    /// <para><b>Weapon detection has to be right rather than plausible</b>, because nothing downstream
    /// contradicts it: a wrong pair names a different class, and every recommendation afterwards is
    /// confidently aimed at a character the player is not playing. So a pair earns this flag one of
    /// three ways, and only these three:</para>
    ///
    /// <list type="number">
    /// <item>The player said so — in Settings, or by answering the confirmation. Always wins.</item>
    /// <item>The model read it from TEXT: a weapon tooltip, the Weapon Mastery screen, the skills
    /// screen. It is reliable at text.</item>
    /// <item>The model recognised the slot ARTWORK twice, on separate captures, and got the same
    /// answer. One icon read is a guess; two agreeing is evidence.</item>
    /// </list>
    ///
    /// <para>Unconfirmed weapons are still usable — they are what the confirmation question is built
    /// from — but they must never silently drive advice as though they were known.</para>
    /// </summary>
    public bool WeaponsConfirmed { get; init; }

    /// <summary>
    /// Whether the player has already been offered a recommended build target.
    ///
    /// <para>Exists so the offer happens ONCE. A tool that asks the same setup question on every
    /// capture is more annoying than one that never asks, and the offer is a footnote to a real answer
    /// rather than the answer.</para>
    /// </summary>
    public bool BuildOfferShown { get; init; }

    /// <summary>
    /// Region slug driving the boss schedule. Uses questlog's own values — <c>americas</c>,
    /// <c>europe</c>, <c>japan-oceania</c> — rather than prettier names, so the setting joins
    /// directly to the live server list without a translation table.
    /// </summary>
    public string Region { get; init; } = "americas";

    /// <summary>
    /// The specific server the player is on, e.g. <c>Eclipse</c>. Boss times differ by region, so
    /// the countdown stays disabled until this is chosen rather than showing times for the wrong
    /// part of the world.
    /// </summary>
    public string? ServerName { get; init; }

    /// <summary>IANA timezone for the player's server, used to resolve schedule times.</summary>
    /// <summary>
    /// Timezone the schedule's slot times are expressed in. <b>Null means the player's own machine
    /// zone, and that is the intended value</b> — this is deliberately not exposed in Settings.
    ///
    /// <para>Asking was a mistake, stated plainly by the product owner: nobody except the game knows
    /// what timezone a server runs in, so the question has no answer the player can give reliably. It
    /// produced exactly the failure that reasoning predicts — a stored <c>America/New_York</c> on a
    /// Pacific machine, putting every countdown three hours out while still showing plausible evening
    /// times. The old default here was that same string, so every install started wrong.</para>
    ///
    /// <para>Local is right because the slot times were read off a live client's own schedule panel,
    /// and that panel renders in the player's local time — the numbers Loadstar counts down to are the
    /// numbers the game shows. Kept as a settable field with no UI so a future correction is a config
    /// edit rather than a code change.</para>
    /// </summary>
    public string? ServerTimeZone { get; init; }

    /// <summary>Minutes before a spawn to alert. Empty disables alerts.</summary>
    public IReadOnlyList<int> BossAlertMinutes { get; init; } = [15, 5];

    public bool BossAlertsEnabled { get; init; } = true;
}
