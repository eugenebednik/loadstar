using System.Text.Json;
using Loadstar.Core.Model;

namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// Maps one entry of questlog's <c>builds</c> array onto <see cref="TargetBuild"/>.
///
/// Written against the live payload rather than guessed — the shapes below are what the API
/// actually returns for the reference build, including the parts that are awkward: runes and
/// heroic slots arrive as JSON <em>objects keyed by numeric strings</em> ("0", "1", "2") rather
/// than arrays, and trait values can be negative.
/// </summary>
public static class BuildMapper
{
    public static TargetBuild Map(JsonElement build, string slug)
    {
        var equipment = new Dictionary<string, TargetItem>(StringComparer.OrdinalIgnoreCase);

        if (build.TryGetProperty("equipment", out var eq) && eq.ValueKind == JsonValueKind.Object)
        {
            foreach (var slot in eq.EnumerateObject())
            {
                if (slot.Value.ValueKind == JsonValueKind.Object)
                {
                    equipment[slot.Name] = MapItem(slot.Value);
                }
            }
        }

        return new TargetBuild
        {
            Id = build.TryGetProperty("id", out var id) ? id.ToString() : slug,
            Name = build.TryGetProperty("name", out var n) ? n.GetString() ?? slug : slug,
            Source = "questlog.gg",
            SourceUrl = $"https://questlog.gg/throne-and-liberty/en/character-builder/{slug}",
            WeaponTypes = ReadStringArray(build, "weaponTypes"),
            Equipment = equipment,
            Attributes = MapAttributes(build),
            Tags = ReadStringArray(build, "tags"),
            Notes = build.TryGetProperty("note", out var note) ? note.GetString() : null,
        };
    }

    /// <summary>
    /// Reads the build's target stat spread.
    ///
    /// <para>Carried through with questlog's own keys rather than translated, so nothing is lost in
    /// transit — and two of those keys are actively misleading if read at face value: <c>int</c> is
    /// <b>Wisdom</b> and <c>con</c> is <b>Fortitude</b>. <see cref="TlStats.QuestlogAttributeKeys"/>
    /// owns that mapping.</para>
    ///
    /// <para>The values are <b>allocated</b> points, not stat totals. Base starts at 10 per stat, so
    /// a build reading <c>str: 0</c> is not asking for no Strength — it is allocating none because
    /// the author's gear supplies what they need.</para>
    /// </summary>
    private static IReadOnlyDictionary<string, int> MapAttributes(JsonElement build)
    {
        var attributes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (build.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in attrs.EnumerateObject())
            {
                if (entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetInt32(out var value))
                {
                    attributes[entry.Name] = value;
                }
            }
        }

        return attributes;
    }

    private static TargetItem MapItem(JsonElement item) => new()
    {
        ItemId = item.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
        Perk = item.TryGetProperty("perk", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null,
        Runes = MapRunes(item),
        Traits = MapTraits(item),
        Heroic = MapNumberKeyedStrings(item, "heroic"),
        Potential = item.TryGetProperty("potential", out var pot) && pot.ValueKind == JsonValueKind.String ? pot.GetString() : null,
        Resonance = item.TryGetProperty("resonance", out var res) && res.ValueKind == JsonValueKind.String ? res.GetString() : null,
    };

    /// <summary>
    /// Runes arrive as <c>{"0": {...}, "1": {...}}</c> — an object keyed by numeric strings, not
    /// an array. Slot order is the key, so enumerate and sort rather than trusting document order.
    /// </summary>
    private static IReadOnlyList<TargetRune> MapRunes(JsonElement item)
    {
        if (!item.TryGetProperty("runes", out var runes) || runes.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var mapped = new List<(int Slot, TargetRune Rune)>();

        foreach (var entry in runes.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var slot = int.TryParse(entry.Name, out var s) ? s : int.MaxValue;

            mapped.Add((slot, new TargetRune
            {
                RuneId = entry.Value.TryGetProperty("runeId", out var rid) ? rid.GetString() ?? string.Empty : string.Empty,
                StatId = entry.Value.TryGetProperty("statId", out var sid) ? sid.GetString() ?? string.Empty : string.Empty,
                Level = entry.Value.TryGetProperty("lvl", out var lvl) && lvl.TryGetInt32(out var l) ? l : 0,
            }));
        }

        return mapped.OrderBy(x => x.Slot).Select(x => x.Rune).ToArray();
    }

    /// <summary>
    /// Traits are <c>{"hp_max": 600, "debuff_taken_duration_modifier": -600}</c>. Values are
    /// signed — a negative trait is a real, intentional part of a build, not corrupt data.
    /// </summary>
    private static IReadOnlyDictionary<string, int> MapTraits(JsonElement item)
    {
        var traits = new Dictionary<string, int>(StringComparer.Ordinal);

        if (item.TryGetProperty("traits", out var t) && t.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in t.EnumerateObject())
            {
                if (entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetInt32(out var v))
                {
                    traits[entry.Name] = v;
                }
            }
        }

        return traits;
    }

    private static IReadOnlyList<string> MapNumberKeyedStrings(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var el) || el.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return el.EnumerateObject()
            .Where(e => e.Value.ValueKind == JsonValueKind.String)
            .OrderBy(e => int.TryParse(e.Name, out var i) ? i : int.MaxValue)
            .Select(e => e.Value.GetString() ?? string.Empty)
            .Where(v => v.Length > 0)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return arr.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString() ?? string.Empty)
            .Where(x => x.Length > 0)
            .ToArray();
    }
}
