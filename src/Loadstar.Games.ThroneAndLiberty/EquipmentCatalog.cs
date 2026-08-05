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
            AvailableItemLevels = ReadItemLevels(element),
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
public enum AcquisitionEstimate
{
    Unknown = 0,
    Easiest,
    Easy,
    Moderate,
    Hard,
    Hardest,
}
