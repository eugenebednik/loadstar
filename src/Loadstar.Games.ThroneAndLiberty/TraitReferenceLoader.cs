using System.Text.Json;

namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// Builds a <see cref="TraitReference"/> from questlog's reference catalogues.
///
/// <para>Three calls, all static per patch and therefore cached hard: the rune tables, the item
/// catalogue (only for item→set membership) and the set bonuses, plus the synergy table. The item
/// catalogue is ~10 MB, which is why only the one field needed is kept — holding the whole thing in
/// memory to answer "which set is this in" would be a poor trade.</para>
///
/// <para>Every parse is defensive. These are undocumented, unversioned endpoints, so a shape change
/// should degrade the calculator's coverage rather than throw: a table that fails to parse leaves
/// its contributions unresolved, and unresolved contributions are reported.</para>
/// </summary>
public sealed partial class QuestlogClient
{
    public async Task<TraitReference> GetTraitReferenceAsync(CancellationToken cancellationToken)
    {
        var runes = await GetJsonAsync("characterBuilder.getEquipmentRunes", cancellationToken)
            .ConfigureAwait(false);
        var sets = await GetJsonAsync("characterBuilder.getEquipmentItemSets", cancellationToken)
            .ConfigureAwait(false);
        var synergies = await GetJsonAsync("characterBuilder.getRuneSynergies", cancellationToken)
            .ConfigureAwait(false);
        var format = await GetJsonAsync("statFormat.getStatFormat", cancellationToken)
            .ConfigureAwait(false);

        using (runes)
        using (sets)
        using (synergies)
        using (format)
        {
            var (itemToSet, gearSets) = ParseSets(sets);

            return new TraitReference
            {
                RuneLevels = ParseRuneLevels(runes),
                ItemToSet = itemToSet,
                Sets = gearSets,
                Synergies = ParseSynergies(synergies),
                Display = ParseDisplay(format),
            };
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string procedure, CancellationToken cancellationToken)
    {
        var url = $"{Base}{procedure}?input=%7B%22language%22%3A%22en%22%7D";
        var payload = await _http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

        return JsonDocument.Parse(payload);
    }

    /// <summary>Unwraps tRPC's <c>{"result":{"data":…}}</c> envelope.</summary>
    private static JsonElement Data(JsonDocument document) =>
        document.RootElement.TryGetProperty("result", out var result)
        && result.TryGetProperty("data", out var data)
            ? data
            : document.RootElement;

    private static Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>> ParseRuneLevels(
        JsonDocument document)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>>(
            StringComparer.OrdinalIgnoreCase);

        var data = Data(document);

        if (data.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var rune in data.EnumerateObject())
        {
            // The stat pool is a weighted roll table; each entry carries the value at every level.
            if (!rune.Value.TryGetProperty("itemStats", out var stats)
                || !stats.TryGetProperty("random_stat_group_1", out var group)
                || group.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var byStat = new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in group.EnumerateArray())
            {
                if (!entry.TryGetProperty("stat_id", out var statId)
                    || statId.GetString() is not { } stat
                    || !entry.TryGetProperty("levels", out var levels)
                    || levels.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                byStat[stat] = [.. levels.EnumerateArray()
                    .Select(l => l.TryGetInt32(out var v) ? v : 0)];
            }

            if (byStat.Count > 0)
            {
                result[rune.Name] = byStat;
            }
        }

        return result;
    }

    private static (Dictionary<string, string> ItemToSet, Dictionary<string, GearSet> Sets) ParseSets(
        JsonDocument document)
    {
        var itemToSet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var gearSets = new Dictionary<string, GearSet>(StringComparer.OrdinalIgnoreCase);

        var data = Data(document);

        if (data.ValueKind != JsonValueKind.Object)
        {
            return (itemToSet, gearSets);
        }

        foreach (var set in data.EnumerateObject())
        {
            var id = set.Name;

            // Item → set only exists in reverse, via the set's own member list.
            if (set.Value.TryGetProperty("itemSetMadeOfItems", out var members)
                && members.ValueKind == JsonValueKind.Array)
            {
                foreach (var member in members.EnumerateArray())
                {
                    if (member.TryGetProperty("id", out var itemId)
                        && itemId.GetString() is { } item)
                    {
                        itemToSet[item] = id;
                    }
                }
            }

            var bonuses = new List<GearSetBonus>();

            if (set.Value.TryGetProperty("itemSetBonus", out var tiers)
                && tiers.ValueKind == JsonValueKind.Array)
            {
                foreach (var tier in tiers.EnumerateArray())
                {
                    if (!tier.TryGetProperty("set_count", out var count)
                        || !count.TryGetInt32(out var pieces))
                    {
                        continue;
                    }

                    var stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                    if (tier.TryGetProperty("bonus_stat", out var bonusStats)
                        && bonusStats.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var stat in bonusStats.EnumerateArray())
                        {
                            if (stat.TryGetProperty("type", out var type)
                                && type.GetString() is { } statId
                                && stat.TryGetProperty("value", out var value)
                                && value.TryGetInt32(out var amount))
                            {
                                stats[statId] = amount;
                            }
                        }
                    }

                    var passives = new List<string>();

                    if (tier.TryGetProperty("bonus_passive", out var bonusPassives)
                        && bonusPassives.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var passive in bonusPassives.EnumerateArray())
                        {
                            if (passive.TryGetProperty("text", out var text)
                                && text.GetString() is { } description)
                            {
                                passives.Add(description);
                            }
                        }
                    }

                    bonuses.Add(new GearSetBonus
                    {
                        PieceCount = pieces,
                        Stats = stats,
                        Passives = passives,
                    });
                }
            }

            gearSets[id] = new GearSet
            {
                Id = id,
                Name = set.Value.TryGetProperty("name", out var name) ? name.GetString() ?? id : id,
                Bonuses = [.. bonuses.OrderBy(b => b.PieceCount)],
            };
        }

        return (itemToSet, gearSets);
    }

    /// <summary>
    /// Display names and scale factors, so a computed total can be stated in the units the player
    /// sees. Without this the numbers are internal and comparing them to a screenshot is wrong by
    /// an order of magnitude.
    /// </summary>
    private static Dictionary<string, StatDisplay> ParseDisplay(JsonDocument document)
    {
        var result = new Dictionary<string, StatDisplay>(StringComparer.OrdinalIgnoreCase);

        var data = Data(document);

        if (data.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var entry in data.EnumerateObject())
        {
            if (!entry.Value.TryGetProperty("name", out var name) || name.GetString() is not { } display)
            {
                continue;
            }

            var multiplier = entry.Value.TryGetProperty("multiplier", out var m)
                && m.TryGetDouble(out var value)
                    ? value
                    : 1;

            // The percent sign lives in the format string, e.g. "{0}%".
            var isPercent = entry.Value.TryGetProperty("valueFormat", out var format)
                && (format.GetString() ?? string.Empty).Contains('%', StringComparison.Ordinal);

            result[entry.Name] = new StatDisplay
            {
                Name = display,
                Multiplier = multiplier == 0 ? 1 : multiplier,
                IsPercent = isPercent,
            };
        }

        return result;
    }

    private static Dictionary<string, RuneSynergy> ParseSynergies(JsonDocument document)
    {
        var result = new Dictionary<string, RuneSynergy>(StringComparer.OrdinalIgnoreCase);

        var data = Data(document);

        if (data.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var entry in data.EnumerateObject())
        {
            var value = entry.Value;

            if (!value.TryGetProperty("equipmentCategory", out var categoryElement)
                || categoryElement.GetString() is not { } category
                // questlog ships a "test" category of six entries. Filtering it out here rather than
                // at the call site keeps the artifact out of every consumer.
                || category.Equals("test", StringComparison.OrdinalIgnoreCase)
                || !value.TryGetProperty("combination", out var combination)
                || combination.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var order = combination.EnumerateArray()
                .Select(c => c.GetString())
                .Where(c => c is not null)
                .Select(c => c!)
                .ToArray();

            if (order.Length != 3)
            {
                continue;
            }

            var stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (value.TryGetProperty("stats", out var statsElement)
                && statsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var stat in statsElement.EnumerateObject())
                {
                    if (stat.Value.TryGetInt32(out var amount))
                    {
                        stats[stat.Name] = amount;
                    }
                }
            }

            result[TraitReference.SynergyKey(category, order)] = new RuneSynergy
            {
                Name = value.TryGetProperty("name", out var name) ? name.GetString() ?? entry.Name : entry.Name,
                Combination = order,
                Stats = stats,
            };
        }

        return result;
    }
}
