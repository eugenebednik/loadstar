using Loadstar.Core.Configuration;

namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// What we crop before sending a capture to the model.
///
/// The important lesson from calibrating against a live client: **most of Throne and Liberty's
/// panels are user-movable**, so cropping to a fixed rectangle is wrong for almost everything.
/// The player drags the inventory somewhere else and the crop silently starts capturing floor
/// tiles. Only the currency bar is safe to crop, because it is anchored to the screen edge.
///
/// Everything else is captured as the full window, and locating the panel is the model's job —
/// which it is good at, unlike naming icons.
///
/// Calibrated 2026-08-03 against patch 4.5.0 on a 16:10 client.
/// </summary>
public static class ScreenRegions
{
    /// <summary>
    /// The currency and token bar: full width, hard against the top edge, about 3.5% tall.
    /// Anchored to the screen rather than draggable, so this crop is stable.
    ///
    /// Left to right: Lucent, Sollant, Contract, Guild, Restoration, Ornate, Loyalty, Boost
    /// Ticket. Icons and numbers only — see <see cref="Notes"/>.
    /// </summary>
    public static readonly CaptureRegion CurrencyBar = new()
    {
        Left = 0.0,
        Top = 0.0,
        Width = 1.0,
        Height = 0.035,
    };

    /// <summary>
    /// The whole client area. Used for every movable panel — inventory, character sheet,
    /// merchants. Costs more tokens than a crop and is worth it: a crop that misses the panel
    /// costs the same and returns nothing.
    /// </summary>
    public static readonly CaptureRegion FullWindow = new()
    {
        Left = 0.0,
        Top = 0.0,
        Width = 1.0,
        Height = 1.0,
    };

    /// <summary>
    /// Which capture each in-game screen wants. Anything movable gets the full window.
    /// </summary>
    public static CaptureRegion ForScreen(TlScreen screen) => screen switch
    {
        TlScreen.CurrencyBar => CurrencyBar,

        // Character sheet is full-screen by design; inventory and merchants are draggable.
        // All three therefore need the whole window.
        TlScreen.CharacterSheet => FullWindow,
        TlScreen.Inventory => FullWindow,
        TlScreen.Merchant => FullWindow,

        _ => FullWindow,
    };

    /// <summary>
    /// Areas to blank out before sending, wherever they land. The bottom-left corner carries
    /// the party list and chat — other players' names and whatever they typed. None of it
    /// improves the advice and all of it would leave the machine, so it is masked by default.
    /// </summary>
    public static readonly IReadOnlyList<CaptureRegion> PrivacyMasks =
    [
        new CaptureRegion { Left = 0.0, Top = 0.72, Width = 0.32, Height = 0.28 },
    ];

    /// <summary>
    /// Findings from the live client that the vision prompt and the identification pipeline
    /// have to account for. These are product constraints, not trivia.
    /// </summary>
    public const string Notes = """
        READABILITY VARIES ENORMOUSLY BY SCREEN. Treat them differently:

        Character sheet — the good one. Full-screen, and genuinely text-rich: named stats with
        values (Strength 40, Dexterity 80, Wisdom 96 …), weapon and defence effects, and
        crucially AN ITEM LEVEL NUMBER ON EVERY EQUIPMENT SLOT (72, 75, 71, 50 …). That is the
        single highest-value capture in the game: item level per slot is exactly what a target
        build compares against, and a slot sitting at 50 among neighbours at 72+ is a concrete,
        actionable gap. Prefer this screen over everything else.

        Currency bar — numbers, no names. Eight icons and eight numbers. Names live only in
        hover tooltips, and hovering is input, which this project does not do. Resolved with a
        one-time named-currency reference capture instead of guessing.

        Inventory — icons and stack counts, no names. Rarity is legible from tile border colour;
        capacity reads plainly (101/160). Item identity does not. Resolved by matching tiles
        against a local icon index built from questlog's catalogue, before the model sees them.

        PANELS MOVE. Inventory and most windows are draggable, so fixed crops are unsafe. Only
        the currency bar is edge-anchored. Everything else: capture the full window and let the
        model locate the panel.
        """;
}

public enum TlScreen
{
    Unknown = 0,
    CurrencyBar,
    CharacterSheet,
    Inventory,
    Merchant,
}
