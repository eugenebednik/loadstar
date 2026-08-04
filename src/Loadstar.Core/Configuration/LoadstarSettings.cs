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
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AiProviderKind
{
    Anthropic,
    OpenAi,
}

public sealed record CaptureSettings
{
    /// <summary>Seconds between snapshots. Below 30 costs more than it helps.</summary>
    public int IntervalSeconds { get; init; } = 120;

    /// <summary>Window title substring used to find the game. Configurable because clients get renamed.</summary>
    public string WindowTitleMatch { get; init; } = "THRONE AND LIBERTY";

    /// <summary>
    /// Optional crop, as fractions of the window (0..1). Restricting this is the main lever
    /// for keeping other players' names out of what gets sent to the provider.
    /// </summary>
    public CaptureRegion? Region { get; init; }

    /// <summary>Only capture when the user presses the hotkey, rather than on a timer.</summary>
    public bool ManualOnly { get; init; }
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
}

public sealed record GameSettings
{
    /// <summary>questlog.gg build URL, or the bare slug.</summary>
    public string? BuildUrl { get; init; }

    /// <summary>Region driving the boss schedule: Americas, Europe, or Asia.</summary>
    public string Region { get; init; } = "Americas";

    /// <summary>IANA timezone for the player's server, used to resolve schedule times.</summary>
    public string ServerTimeZone { get; init; } = "America/New_York";

    /// <summary>Minutes before a spawn to alert. Empty disables alerts.</summary>
    public IReadOnlyList<int> BossAlertMinutes { get; init; } = [15, 5];
}
