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
    /// NONE, deliberately. This game declares no fixed privacy mask.
    ///
    /// <para><b>There used to be one</b> — the bottom-left 32% by 28%, aimed at the party list and chat,
    /// because those carry other players' names and whatever they typed. It was removed on 2026-08-10 after
    /// it was seen blacking out the character sheet's stat column in a real capture. That is the single
    /// highest-value region in the game (see CLAUDE.md: item level per slot, gear score, the stat block), so
    /// the mask was destroying exactly the data the product exists to read.</para>
    ///
    /// <para><b>It could not be fixed by moving it, because the chat window moves.</b> It is draggable and
    /// resizable, so no fixed rectangle covers it on every player's screen — and this file already knew that:
    /// <see cref="ForScreen"/> hands the full window to anything draggable for precisely this reason. The
    /// mask was a fixed crop over a movable panel, which is the one thing the surrounding code says not to
    /// do.</para>
    ///
    /// <para><b>Detecting it instead was considered and rejected.</b> The chat panel is translucent, so it
    /// has no stable colour; it has no consistent border; and its content is ordinary text. Compare the
    /// equipment slots, which are locatable only because they have a hard signature — bronze rings with a
    /// measurable red-minus-blue margin and circular geometry. Chat has nothing equivalent, and a mask that
    /// guesses wrong is worse than none: guess small and privacy is not protected, guess large and the stats
    /// go dark again.</para>
    ///
    /// <para><b>What protects the player instead is stronger, and already shipped.</b> The ask dialog shows
    /// every queued screenshot at the moment of sending, says outright that this exact image is what gets
    /// sent, and puts a delete button on each one. That is informed consent over a preview rather than a
    /// blanket rectangle the player never sees and cannot verify. Anyone who wants their chat out of frame
    /// can also close or move it before capturing, which a mask cannot be relied on to do for them.</para>
    ///
    /// <para>The masking machinery in the capture pipeline stays: it is generic, it paints before encoding so
    /// masked pixels never reach a file, and another game module may well need it. This game just declares
    /// none.</para>
    /// </summary>
    public static readonly IReadOnlyList<CaptureRegion> PrivacyMasks = [];

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
