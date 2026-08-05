namespace Loadstar.Core.Model;

/// <summary>What the assistant thinks the player should do next, in priority order.</summary>
public sealed record Advice
{
    public required DateTimeOffset GeneratedAt { get; init; }
    public required IReadOnlyList<AdviceStep> Steps { get; init; }

    /// <summary>One line the overlay can show when there's no room for the full list.</summary>
    public required string Headline { get; init; }

    /// <summary>
    /// Which in-game screen the model believed it was looking at.
    ///
    /// <para>Recognised rather than declared: the user presses a hotkey whenever they want an
    /// answer, so nothing upstream knows whether that moment is the character sheet, the inventory
    /// or open world. Surfacing it lets the app say "this looks like the inventory, and the
    /// question you asked needs the character sheet" instead of answering from the wrong screen.</para>
    /// </summary>
    public ScreenKind RecognizedScreen { get; init; } = ScreenKind.Unknown;

    /// <summary>The model's own reading of whether the screen could answer the question asked.</summary>
    public bool AnsweredFromScreen { get; init; } = true;

    /// <summary>
    /// Things the model could not determine and that would change the advice if known.
    /// Surfaced to the player so they know which screen to open next.
    /// </summary>
    public IReadOnlyList<string> MissingInformation { get; init; } = [];

    public TokenUsage? Usage { get; init; }
}

public sealed record AdviceStep
{
    public required int Rank { get; init; }
    public required string Action { get; init; }

    /// <summary>Why this beats the alternatives. The part that makes the advice trustworthy.</summary>
    public required string Rationale { get; init; }

    /// <summary>Resource cost, keyed by currency name. Empty when the action is free.</summary>
    public IReadOnlyDictionary<string, long> Cost { get; init; }
        = new Dictionary<string, long>();

    public bool Affordable { get; init; } = true;

    /// <summary>Which slot or system this touches — for grouping in the UI.</summary>
    public string? Category { get; init; }
}

public sealed record TokenUsage
{
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }

    /// <summary>Estimated cost in USD. Approximate — providers change prices.</summary>
    public decimal EstimatedCostUsd { get; init; }
}
