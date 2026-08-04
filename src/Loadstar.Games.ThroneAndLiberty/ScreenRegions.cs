using Loadstar.Core.Configuration;

namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// Capture region presets, expressed as fractions of the game window so they survive any
/// resolution. Calibrated against a live 16:10 client on 2026-08-03 — re-check after a UI patch.
/// </summary>
public static class ScreenRegions
{
    /// <summary>
    /// The currency and token bar. It spans the full width of the very top of the screen:
    /// gold and the common tokens sit at the left, event and dungeon currencies at the right.
    /// The player has to expand it first ("View all currency"), which is a first-run
    /// instruction, not something we can do for them — see <see cref="Notes"/>.
    /// </summary>
    public static readonly CaptureRegion CurrencyBar = new()
    {
        Left = 0.0,
        Top = 0.0,
        Width = 1.0,
        Height = 0.035,
    };

    /// <summary>
    /// Character and equipment panel. Roughly the centre of the screen when open; generous
    /// bounds because the panel's position shifts a little with UI scale.
    /// </summary>
    public static readonly CaptureRegion CharacterPanel = new()
    {
        Left = 0.10,
        Top = 0.08,
        Width = 0.70,
        Height = 0.80,
    };

    public static readonly CaptureRegion InventoryPanel = new()
    {
        Left = 0.45,
        Top = 0.08,
        Width = 0.50,
        Height = 0.80,
    };

    /// <summary>
    /// Regions we deliberately never capture. The bottom-left corner carries party list and
    /// chat, which means other players' names and whatever they typed. None of that helps the
    /// advice and all of it would be sent to a third-party API, so it is excluded by default.
    /// </summary>
    public static readonly IReadOnlyList<CaptureRegion> PrivacyExclusions =
    [
        new CaptureRegion { Left = 0.0, Top = 0.72, Width = 0.32, Height = 0.28 },
    ];

    /// <summary>
    /// Findings from calibrating against the live client that the vision prompt has to account
    /// for. These are product constraints, not implementation details.
    /// </summary>
    public const string Notes = """
        The currency bar renders as ICONS PLUS NUMBERS WITH NO TEXT LABELS. A model looking at
        a cropped currency bar sees eight coloured icons and eight numbers, and has no reliable
        way to name them — the names only exist in hover tooltips, and hovering would mean
        sending input to the game, which this project does not do.

        So we do not ask the model to guess. On first run the user opens the full currency
        window once, which lists every currency by name next to its icon. That single capture
        becomes a per-user reference the model is given alongside every later currency crop,
        turning an unanswerable identification problem into a lookup.

        The bar must be expanded by the player before it shows anything beyond gold. Collapsed
        is the default state, so the first-run flow has to say so explicitly.
        """;
}
