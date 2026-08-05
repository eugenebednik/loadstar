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
    public AiProviderKind Provider { get; init; } = AiProviderKind.Anthropic;

    /// <summary>Model id. Defaults to the most capable Claude model.</summary>
    public string Model { get; init; } = "claude-opus-5";

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

    /// <summary>Window title substring. A fallback, subordinate to <see cref="WindowProcessName"/>.</summary>
    public string WindowTitleMatch { get; init; } = "THRONE AND LIBERTY";

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

    /// <summary>Resolves the configured target into the form the capture source consumes.</summary>
    public Capture.WindowTarget ToWindowTarget() => new()
    {
        ProcessName = string.IsNullOrWhiteSpace(WindowProcessName)
            ? null
            : Capture.WindowTargeting.NormalizeProcessName(WindowProcessName),
        TitleMatch = WindowTitleMatch,
        AllowAnyProcess = AllowAnyProcess,
    };
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
    public double Opacity { get; init; } = 0.88;
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
    /// <summary>questlog.gg build URL, or the bare slug.</summary>
    public string? BuildUrl { get; init; }

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
    public string ServerTimeZone { get; init; } = "America/New_York";

    /// <summary>Minutes before a spawn to alert. Empty disables alerts.</summary>
    public IReadOnlyList<int> BossAlertMinutes { get; init; } = [15, 5];

    public bool BossAlertsEnabled { get; init; } = true;
}
