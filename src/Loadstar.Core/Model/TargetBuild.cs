namespace Loadstar.Core.Model;

/// <summary>
/// Where the player is trying to get to. Unlike <see cref="GameSnapshot"/> this is exact
/// structured data imported from a build site, not something inferred from pixels — which is
/// the whole point of the design. The model only ever has to read one side of the comparison.
/// </summary>
public sealed record TargetBuild
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Source { get; init; }
    public string? SourceUrl { get; init; }
    public string? Author { get; init; }
    public int? Level { get; init; }
    public IReadOnlyList<string> WeaponTypes { get; init; } = [];

    /// <summary>Target gear keyed by slot name.</summary>
    public IReadOnlyDictionary<string, TargetItem> Equipment { get; init; }
        = new Dictionary<string, TargetItem>();

    public IReadOnlyDictionary<string, int> SkillLevels { get; init; }
        = new Dictionary<string, int>();

    /// <summary>Author's notes, stripped to plain text. Often contains the priority order.</summary>
    public string? Notes { get; init; }

    public DateTimeOffset ImportedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record TargetItem
{
    /// <summary>Catalogue id, e.g. <c>sword2h_aa_t2_raid_001</c>.</summary>
    public required string ItemId { get; init; }

    /// <summary>Human-readable name resolved from the catalogue. Null if unresolved.</summary>
    public string? DisplayName { get; init; }

    public string? Perk { get; init; }
    public IReadOnlyList<TargetRune> Runes { get; init; } = [];

    /// <summary>Trait name to target value.</summary>
    public IReadOnlyDictionary<string, int> Traits { get; init; }
        = new Dictionary<string, int>();

    public IReadOnlyList<string> Heroic { get; init; } = [];
    public string? Potential { get; init; }
    public string? Resonance { get; init; }
}

public sealed record TargetRune
{
    public required string RuneId { get; init; }
    public required string StatId { get; init; }
    public int Level { get; init; }
}
