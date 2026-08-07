namespace Loadstar.Core.Model;

/// <summary>
/// The player's current state as read off the screen. Every field is optional because a
/// snapshot only ever sees whatever screens the player happened to have open — an inventory
/// capture tells us nothing about skills, and vice versa. Consumers must treat null as
/// "unknown", never as "empty".
/// </summary>
public sealed record GameSnapshot
{
    public required DateTimeOffset CapturedAt { get; init; }

    /// <summary>Which in-game screen the model believed it was looking at.</summary>
    public required ScreenKind Screen { get; init; }

    public int? CharacterLevel { get; init; }

    /// <summary>Equipped items keyed by slot. Absent slot means "not visible in this capture".</summary>
    public IReadOnlyDictionary<string, ObservedItem> Equipment { get; init; }
        = new Dictionary<string, ObservedItem>();

    /// <summary>Gold, and every token/currency type the model could read, keyed by display name.</summary>
    public IReadOnlyDictionary<string, long> Currencies { get; init; }
        = new Dictionary<string, long>();

    public IReadOnlyList<ObservedItem> Inventory { get; init; } = [];

    public IReadOnlyDictionary<string, int> SkillLevels { get; init; }
        = new Dictionary<string, int>();

    /// <summary>
    /// How much of this the model was willing to stand behind, 0..1. Low confidence
    /// snapshots are still stored but are not used to drive advice on their own.
    /// </summary>
    public double Confidence { get; init; } = 1.0;

    /// <summary>Anything the model flagged as unreadable — occluded, blurred, cut off.</summary>
    public IReadOnlyList<string> Unreadable { get; init; } = [];
}

public enum ScreenKind
{
    Unknown = 0,
    Inventory,
    Character,
    Skills,
    Currency,
    Merchant,
    World,

    /// <summary>
    /// The tabbed expanded panel, which is a DIFFERENT screen from <see cref="Character"/> and the only
    /// place Evasion, Endurance, Hit, Critical, Heavy Attack and Damage Reduction appear at all.
    /// </summary>
    CharacterExpanded,

    /// <summary>
    /// The Rune Book.
    ///
    /// <para><b>This value's absence was a real bug.</b> The prompt tells the model to ask for the Rune
    /// Book proactively — runes were the single largest Combat Power gap on the reference character — and
    /// then handed it an enum with no way to name the screen when the player supplied it. A rune screen
    /// arrived among four and produced no rune advice at all.</para>
    /// </summary>
    Runes,

    /// <summary>The Artifact page. Absent for the same reason as <see cref="Runes"/>, and paired with it
    /// in the prompt as the other system most often left empty.</summary>
    Artifacts,

    /// <summary>The Weapon Mastery screen, which names the character's weapons in text.</summary>
    Mastery,

    /// <summary>Exploration or Adventure Codex — a materials source, so worth telling apart.</summary>
    Codex,
}

/// <summary>An item as the model read it from the screen, not as the game defines it.</summary>
public sealed record ObservedItem
{
    public required string Name { get; init; }

    /// <summary>Resolved against the item catalogue when we can match the name. Null when we can't.</summary>
    public string? CatalogItemId { get; init; }

    public string? Rarity { get; init; }
    public int? EnhancementLevel { get; init; }
    public int? Quantity { get; init; }
    public IReadOnlyList<string> Traits { get; init; } = [];
}
