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

    /// <summary>
    /// One entry per screenshot sent, in the order they were sent.
    ///
    /// <para><b>Why a list and not just <see cref="RecognizedScreen"/>.</b> Up to four screens travel with
    /// a question, and a single field forced the model to pick one — so an answer that had genuinely read
    /// the character sheet AND the artifact page still reported "Screen recognised as: Character", and
    /// looked to the player like three of their four screenshots had been thrown away.</para>
    ///
    /// <para><b>And why <see cref="ScreenReading.Used"/> exists.</b> Making the model write a line per
    /// screen turns ignoring one into something it has to state rather than something that happens
    /// silently. A rune screen was supplied and produced no rune advice, and nothing in the answer
    /// admitted it — which is the failure this field is here to make impossible to hide.</para>
    ///
    /// <para>Empty when the model did not report it, in which case <see cref="RecognizedScreen"/> is all
    /// there is. Nothing depends on this being populated.</para>
    /// </summary>
    public IReadOnlyList<ScreenReading> Screens { get; init; } = [];

    /// <summary>The model's own reading of whether the screen could answer the question asked.</summary>
    public bool AnsweredFromScreen { get; init; } = true;

    /// <summary>
    /// Things the model could not determine and that would change the advice if known.
    /// Surfaced to the player so they know which screen to open next.
    /// </summary>
    public IReadOnlyList<string> MissingInformation { get; init; } = [];

    public TokenUsage? Usage { get; init; }
}

/// <summary>
/// What the model made of one of the screenshots it was sent.
/// </summary>
public sealed record ScreenReading
{
    public required ScreenKind Screen { get; init; }

    /// <summary>
    /// Whether this screen actually informed the advice.
    ///
    /// <para>False is a legitimate and useful answer — a capture of the open world among four genuinely
    /// contributes nothing. What is not acceptable is silence, which is what the single-screen field
    /// allowed.</para>
    /// </summary>
    public bool Used { get; init; }

    /// <summary>
    /// What it contributed, or why it did not. In the player's language, so it can be shown as-is.
    /// </summary>
    public string? Note { get; init; }
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
