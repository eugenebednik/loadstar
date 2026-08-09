using System.Text.Json;

namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// questlog's equipment catalogue, indexed for lookup.
///
/// <para>This is the type that makes "never invent an item name" enforceable rather than
/// aspirational. 1,773 items arrive with display names, slots, rarity grades and per-item-level
/// stats, so identifying and comparing gear becomes a local, deterministic lookup instead of a
/// vision model guessing at a 40px icon. Identification is ours; the model reads numbers.</para>
///
/// <para>Static per patch, and 10.4 MB on the wire, so it is cached to disk and refreshed only on
/// explicit user action — hammering someone else's site to re-fetch data that cannot have changed
/// is how third-party tools get blocked.</para>
/// </summary>
public sealed class EquipmentCatalog
{
    private readonly IReadOnlyDictionary<string, CatalogItem> _byId;
    private readonly ILookup<string, CatalogItem> _bySlot;

    private EquipmentCatalog(IReadOnlyDictionary<string, CatalogItem> byId)
    {
        _byId = byId;
        _bySlot = byId.Values.ToLookup(i => i.EquipmentType, StringComparer.OrdinalIgnoreCase);
    }

    public int Count => _byId.Count;

    public IEnumerable<CatalogItem> Items => _byId.Values;

    /// <summary>Resolves an item id to its catalogue entry, or null when it is not known.</summary>
    public CatalogItem? Find(string itemId) =>
        _byId.TryGetValue(itemId, out var item) ? item : null;

    /// <summary>
    /// Resolves an id to a display name, or a plainly-marked placeholder.
    ///
    /// <para>Never fabricates. An unresolved id becomes "unidentified (&lt;id&gt;)" because a
    /// plausible wrong name produces confidently wrong spending advice, which is worse than an
    /// admitted gap.</para>
    /// </summary>
    public string DisplayName(string itemId) =>
        Find(itemId)?.Name ?? $"unidentified ({itemId})";

    public IEnumerable<CatalogItem> ForSlot(string equipmentType) => _bySlot[equipmentType];

    /// <summary>
    /// Parses the raw tRPC response. The payload is an object keyed by item id, not an array —
    /// enumerating it as a list silently yields nothing.
    /// </summary>
    public static EquipmentCatalog Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "Equipment catalogue payload had no result.data object. The questlog API is " +
                "undocumented and unversioned, so this can change without notice.");
        }

        var items = new Dictionary<string, CatalogItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in data.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var item = CatalogItem.Parse(entry.Name, entry.Value);

            if (item is not null)
            {
                items[item.Id] = item;
            }
        }

        return new EquipmentCatalog(items);
    }
}

public sealed record CatalogItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string EquipmentType { get; init; }

    /// <summary>Rarity ladder. Observed values are 11, 21, 31, 41 and 51, with 51 the rarest.</summary>
    public int? Grade { get; init; }

    public int RequiredLevel { get; init; }

    /// <summary>Set membership, for the set-completion cliff. Null when the item belongs to none.</summary>
    public string? SetId { get; init; }

    public string? Icon { get; init; }

    /// <summary>Item levels this item has stats defined for. Observed range is 0 to 85.</summary>
    public IReadOnlyList<int> AvailableItemLevels { get; init; } = [];

    /// <summary>
    /// The item's own stats at its LOWEST defined level, flattened from the nested armour/weapon groups.
    ///
    /// <para>Floor and ceiling only, not all thirty levels. Two numbers answer the question that matters —
    /// what this piece gives now against what it would give fully raised — while the intermediate levels are
    /// interpolation nobody asks about and thirty times the memory.</para>
    /// </summary>
    public IReadOnlyDictionary<string, int> StatsAtFloor { get; init; } = new Dictionary<string, int>();

    /// <summary>The same stats at its HIGHEST defined level, so the headroom is a subtraction.</summary>
    public IReadOnlyDictionary<string, int> StatsAtCeiling { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// Every trait this item CAN carry, and the value at the last pip.
    ///
    /// <para><b>This is the "possible stats" of a piece and it is not visible in game without hovering.</b>
    /// Gear drops with no traits at all since 4.0.0 — they are unlocked with stones — so the catalogue's list
    /// is the menu, and what a build has chosen is a selection from it. The difference between the two is a
    /// concrete, priceable action: an unlocked trait slot on a piece the player already wears.</para>
    ///
    /// <para>Values are the fourth and final pip, because that is the ceiling a trait can be levelled to and
    /// therefore what a comparison should use.</para>
    /// </summary>
    public IReadOnlyDictionary<string, int> TraitOptions { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// Resonance options, each with the value at its top tier and the percentage chance of rolling it.
    ///
    /// <para>Probabilities matter here in a way they do not for traits: resonance is rolled rather than
    /// chosen, and opening one slot costs 1,500,000 Sollant and three stones. Advice that names a resonance
    /// without its odds is advice about a lottery presented as a purchase.</para>
    /// </summary>
    public IReadOnlyDictionary<string, ResonanceOption> ResonanceOptions { get; init; }
        = new Dictionary<string, ResonanceOption>();

    public int? MaxItemLevel => AvailableItemLevels.Count > 0 ? AvailableItemLevels[^1] : null;

    /// <summary>
    /// The source token embedded in the id — <c>boss</c>, <c>Arch</c>, <c>nomal</c>, <c>upgrade</c>
    /// and so on. <b>Inferred from naming, not a field questlog publishes.</b>
    /// </summary>
    public string? SourceToken { get; init; }

    /// <summary>
    /// A rough "how hard is this to get" estimate.
    ///
    /// <para><b>This is an inference and must be presented as one.</b> The catalogue has no
    /// acquisition-source column; this combines the rarity grade with the source token in the id.
    /// It is good enough to sort candidates by, and not good enough to state as fact.</para>
    /// </summary>
    public AcquisitionEstimate Acquisition
    {
        get
        {
            if (SourceToken is not null)
            {
                if (SourceToken.Contains("Arch", StringComparison.OrdinalIgnoreCase))
                {
                    return AcquisitionEstimate.Hardest;
                }

                if (SourceToken.Contains("boss", StringComparison.OrdinalIgnoreCase))
                {
                    return AcquisitionEstimate.Hard;
                }
            }

            return Grade switch
            {
                >= 51 => AcquisitionEstimate.Hardest,
                >= 41 => AcquisitionEstimate.Moderate,
                >= 31 => AcquisitionEstimate.Easy,
                >= 11 => AcquisitionEstimate.Easiest,
                _ => AcquisitionEstimate.Unknown,
            };
        }
    }

    internal static CatalogItem? Parse(string key, JsonElement element)
    {
        var id = element.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
            ? idElement.GetString() ?? key
            : key;

        var name = element.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
            ? nameElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var levels = ReadItemLevels(element);

        return new CatalogItem
        {
            Id = id,
            Name = name,
            EquipmentType = element.TryGetProperty("equipmentType", out var type) && type.ValueKind == JsonValueKind.String
                ? type.GetString() ?? "unknown"
                : "unknown",
            Grade = element.TryGetProperty("grade", out var grade) && grade.TryGetInt32(out var g) ? g : null,
            RequiredLevel = element.TryGetProperty("requiredLevel", out var rl) && rl.TryGetInt32(out var r) ? r : 0,
            SetId = element.TryGetProperty("setId", out var set) && set.ValueKind == JsonValueKind.String
                ? set.GetString()
                : null,
            Icon = element.TryGetProperty("icon", out var icon) && icon.ValueKind == JsonValueKind.String
                ? icon.GetString()
                : null,
            AvailableItemLevels = levels,
            StatsAtFloor = ReadStatsAt(element, levels.Count > 0 ? levels[0] : null),
            StatsAtCeiling = ReadStatsAt(element, levels.Count > 0 ? levels[^1] : null),
            TraitOptions = ReadTraitOptions(element),
            ResonanceOptions = ReadResonanceOptions(element),
            SourceToken = ExtractSourceToken(id),
        };
    }

    /// <summary>
    /// Reads the item levels <c>itemStats.main</c> defines, sorted. These are the levels at which
    /// the item's stats are known, which is what makes a level-N-to-level-M comparison possible.
    /// </summary>
    private static IReadOnlyList<int> ReadItemLevels(JsonElement element)
    {
        if (!element.TryGetProperty("itemStats", out var stats) ||
            stats.ValueKind != JsonValueKind.Object ||
            !stats.TryGetProperty("main", out var main) ||
            main.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return main.EnumerateObject()
            .Select(p => int.TryParse(p.Name, out var level) ? level : -1)
            .Where(level => level >= 0)
            .OrderBy(level => level)
            .ToArray();
    }

    /// <summary>
    /// The item's stats at one level, flattened.
    ///
    /// <para><c>main</c> nests its values under <c>armor</c>, <c>extra</c>, <c>shield</c>, <c>offhand</c> and
    /// <c>mainhand</c>, most of which are null on any given item, while a sibling <c>extra</c> block is keyed
    /// by level directly. Both are folded into one flat map, because a caller wants the piece's stats and not
    /// a tour of the payload's shape.</para>
    /// </summary>
    private static IReadOnlyDictionary<string, int> ReadStatsAt(JsonElement element, int? level)
    {
        var stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (level is null
            || !element.TryGetProperty("itemStats", out var itemStats)
            || itemStats.ValueKind != JsonValueKind.Object)
        {
            return stats;
        }

        var key = level.Value.ToString();

        if (itemStats.TryGetProperty("main", out var main)
            && main.ValueKind == JsonValueKind.Object
            && main.TryGetProperty(key, out var atLevel)
            && atLevel.ValueKind == JsonValueKind.Object)
        {
            foreach (var group in atLevel.EnumerateObject())
            {
                if (group.Value.ValueKind == JsonValueKind.Object)
                {
                    Absorb(stats, group.Value);
                }
            }
        }

        if (itemStats.TryGetProperty("extra", out var extra)
            && extra.ValueKind == JsonValueKind.Object
            && extra.TryGetProperty(key, out var extraAtLevel)
            && extraAtLevel.ValueKind == JsonValueKind.Object)
        {
            Absorb(stats, extraAtLevel);
        }

        return stats;
    }

    private static void Absorb(Dictionary<string, int> into, JsonElement source)
    {
        foreach (var stat in source.EnumerateObject())
        {
            if (stat.Value.ValueKind == JsonValueKind.Number && stat.Value.TryGetInt32(out var value))
            {
                into[stat.Name] = value;
            }
        }
    }

    /// <summary>
    /// Every trait the item can carry, valued at its final pip.
    ///
    /// <para><c>itemStats.traits</c> is a stat id to an ascending array of four pip values. The last one is
    /// the ceiling, which is what a comparison against a target build should use — a trait at one pip is the
    /// same trait, part-levelled.</para>
    /// </summary>
    private static IReadOnlyDictionary<string, int> ReadTraitOptions(JsonElement element)
    {
        var options = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (!element.TryGetProperty("itemStats", out var itemStats)
            || itemStats.ValueKind != JsonValueKind.Object
            || !itemStats.TryGetProperty("traits", out var traits)
            || traits.ValueKind != JsonValueKind.Object)
        {
            return options;
        }

        foreach (var trait in traits.EnumerateObject())
        {
            if (trait.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var pips = trait.Value.EnumerateArray()
                .Where(v => v.ValueKind == JsonValueKind.Number)
                .Select(v => v.TryGetInt32(out var pip) ? pip : 0)
                .ToArray();

            if (pips.Length > 0)
            {
                options[trait.Name] = pips[^1];
            }
        }

        return options;
    }

    /// <summary>Resonance options with their top tier and roll chance.</summary>
    private static IReadOnlyDictionary<string, ResonanceOption> ReadResonanceOptions(JsonElement element)
    {
        var options = new Dictionary<string, ResonanceOption>(StringComparer.OrdinalIgnoreCase);

        if (!element.TryGetProperty("itemStats", out var itemStats)
            || itemStats.ValueKind != JsonValueKind.Object
            || !itemStats.TryGetProperty("resonance", out var resonance)
            || resonance.ValueKind != JsonValueKind.Object)
        {
            return options;
        }

        foreach (var entry in resonance.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var top = 0;

            if (entry.Value.TryGetProperty("tiers", out var tiers) && tiers.ValueKind == JsonValueKind.Array)
            {
                foreach (var tier in tiers.EnumerateArray())
                {
                    if (tier.ValueKind == JsonValueKind.Number && tier.TryGetInt32(out var value))
                    {
                        top = value;
                    }
                }
            }

            var probability = entry.Value.TryGetProperty("probability", out var chance)
                && chance.TryGetInt32(out var percent)
                ? percent
                : 0;

            options[entry.Name] = new ResonanceOption(top, probability);
        }

        return options;
    }

    /// <summary>
    /// Pulls the source token out of an id like <c>bow_aa_t5_boss_001</c>.
    /// Structure is roughly <c>{slot}_{rarity}_{tier}_{source}_{index}</c>, but it is not perfectly
    /// regular, so this returns null rather than guessing when the shape does not fit.
    /// </summary>
    private static string? ExtractSourceToken(string id)
    {
        var parts = id.Split('_');

        if (parts.Length < 4)
        {
            return null;
        }

        var candidate = parts[3];

        // Trailing numeric segments are indexes, not sources.
        return int.TryParse(candidate, out _) ? null : candidate;
    }
}

/// <summary>
/// How hard an item is estimated to be to obtain. Inferred, never stated as fact — see
/// <see cref="CatalogItem.Acquisition"/>.
/// </summary>
/// <summary>One resonance choice: the value at its highest tier, and the chance of rolling it.</summary>
/// <param name="TopTier">Value at the fourth and final tier.</param>
/// <param name="ProbabilityPercent">Chance of this stat appearing, as a whole percentage.</param>
public readonly record struct ResonanceOption(int TopTier, int ProbabilityPercent);

public enum AcquisitionEstimate
{
    Unknown = 0,
    Easiest,
    Easy,
    Moderate,
    Hard,
    Hardest,
}
