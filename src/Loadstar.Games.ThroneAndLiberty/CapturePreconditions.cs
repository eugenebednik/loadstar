namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// Things that must be true of the game's UI before a capture is worth anything.
///
/// These are not warnings we log and move on from. If the currency bar is collapsed, the
/// assistant is being asked to plan a spending strategy while able to see one number — it will
/// still produce confident-sounding advice, and that advice will be wrong. Surfacing that
/// loudly is more useful than silently degrading.
/// </summary>
public static class CapturePreconditions
{
    /// <summary>
    /// Below this many distinct currencies in a capture, we assume the bar is collapsed rather
    /// than assuming the player genuinely holds almost nothing. A live client with the bar
    /// expanded shows eight or more entries across the top strip.
    /// </summary>
    public const int ExpectedMinimumCurrencies = 4;

    public static readonly PreconditionCheck CurrencyBarExpanded = new()
    {
        Id = "currency-bar-expanded",
        Title = "Expand the currency bar",
        Severity = PreconditionSeverity.Blocking,
        UserInstruction = """
            Throne and Liberty collapses the currency bar by default, showing only gold.

            Click the arrow at the top-left of the screen ("View all currency") so the full row
            of tokens and currencies is visible along the top edge, and leave it expanded while
            Loadstar is running.

            Without it, the assistant can see your gold and nothing else. It cannot tell you
            whether to spend Sollant, dungeon tokens, or event currency, because it cannot see
            that you have any — and advice about resources it can't see is advice you shouldn't
            act on.
            """,
    };

    public static readonly PreconditionCheck CurrencyNamesCaptured = new()
    {
        Id = "currency-names-captured",
        Title = "Capture the currency reference once",
        Severity = PreconditionSeverity.Recommended,
        UserInstruction = """
            The currency bar shows icons and numbers with no names, so on its own it can't be
            read reliably.

            Open the full currency window once (the panel that lists each currency by name) and
            press the capture hotkey. Loadstar stores that single image as a reference and pairs
            it with every later reading, so icons resolve to the right names.

            You only need to do this once, and again after a patch adds new currencies.
            """,
    };

    public static readonly PreconditionCheck BorderlessWindowed = new()
    {
        Id = "borderless-windowed",
        Title = "Run the game in borderless windowed mode",
        Severity = PreconditionSeverity.Recommended,
        UserInstruction = """
            In exclusive fullscreen, Windows gives the display to the game and no other window
            can draw over it — so the overlay won't appear.

            Set Display Mode to Borderless Windowed in the game's video settings.

            Capture and advice still work in exclusive fullscreen; only the on-screen overlay
            is unavailable, and suggestions fall back to a second-monitor toast.
            """,
    };

    public static IReadOnlyList<PreconditionCheck> All =>
    [
        CurrencyBarExpanded,
        CurrencyNamesCaptured,
        BorderlessWindowed,
    ];

    /// <summary>
    /// Inspects what the model actually managed to read and returns the checks the user needs
    /// to act on. Runs after every capture, not just at startup — the player can collapse the
    /// bar mid-session, and we should notice rather than quietly getting worse.
    /// </summary>
    public static IReadOnlyList<PreconditionCheck> Evaluate(int currenciesRead, bool hasCurrencyReference)
    {
        var failures = new List<PreconditionCheck>();

        if (currenciesRead < ExpectedMinimumCurrencies)
        {
            failures.Add(CurrencyBarExpanded);
        }

        if (!hasCurrencyReference)
        {
            failures.Add(CurrencyNamesCaptured);
        }

        return failures;
    }
}

public sealed record PreconditionCheck
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required PreconditionSeverity Severity { get; init; }
    public required string UserInstruction { get; init; }
}

public enum PreconditionSeverity
{
    /// <summary>Advice generated in this state would be misleading. Say so prominently.</summary>
    Blocking,

    /// <summary>Advice is usable but degraded.</summary>
    Recommended,
}
