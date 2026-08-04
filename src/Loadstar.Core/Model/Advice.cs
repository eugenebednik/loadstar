namespace Loadstar.Core.Model;

/// <summary>What the assistant thinks the player should do next, in priority order.</summary>
public sealed record Advice
{
    public required DateTimeOffset GeneratedAt { get; init; }
    public required IReadOnlyList<AdviceStep> Steps { get; init; }

    /// <summary>One line the overlay can show when there's no room for the full list.</summary>
    public required string Headline { get; init; }

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
