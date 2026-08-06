using System.Text.Json;
using Loadstar.Core.Ai;

namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// Reads the <c>observedStats</c> the model reported off the screen.
///
/// <para>This is the division of labour the whole design rests on: the model reads numbers, which
/// it is reliable at, and <see cref="StatPlanner"/> does the arithmetic, which it is not. Parsing
/// the observations back out lets the cost of a redistribution be computed locally and printed as
/// authoritative, rather than trusting prose that has already been observed to omit the expensive
/// half of a trade.</para>
///
/// <para><c>base</c> is optional and stays optional. The character sheet does not show the
/// Base/Equipment split — that needs a stat tooltip — so a model that omits it is being correct,
/// and filling in a plausible value would silently corrupt every cost downstream.</para>
/// </summary>
public static class TlObservationParser
{
    /// <summary>
    /// The two weapon ids the model reported, with where it read them, or null when it reported
    /// nothing usable.
    ///
    /// <para>Two weapons name a class, so this is what lets the app identify the player and look up
    /// what the community plays for them — the mechanism that replaces demanding a build URL.</para>
    ///
    /// <para><b>Only a recognised pair is accepted.</b> Anything else returns null: a pair that is not
    /// one of the 45 means a misread weapon or a weapon newer than the table, and either way storing it
    /// would have the app recommending builds for a class the player is not playing. A single weapon is
    /// also rejected, since one weapon identifies nothing.</para>
    ///
    /// <para><b>The source is kept because the two kinds of read are not equally good.</b> A weapon
    /// tooltip states the type in text, which this model is reliable at. Identifying the slot artwork
    /// is naming an icon, which it is not — and this project has already shipped one confidently wrong
    /// icon read. So the source travels with the value and the caller decides how much to trust it,
    /// rather than the distinction being flattened away here.</para>
    /// </summary>
    public static WeaponReading? ParseWeapons(string responseText)
    {
        ArgumentNullException.ThrowIfNull(responseText);

        var json = AdviceParser.ExtractJsonObject(responseText);

        if (json is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("weapons", out var weapons)
                || weapons.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var read = weapons.EnumerateArray()
                .Where(w => w.ValueKind == JsonValueKind.String)
                .Select(w => w.GetString()?.Trim().ToLowerInvariant() ?? string.Empty)
                .Where(w => w.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            // Validated against the class table rather than merely counted, so a hallucinated weapon
            // name is rejected here instead of becoming a stored setting.
            if (read.Length != 2 || TlClasses.Name(read[0], read[1]) is null)
            {
                return null;
            }

            var source = document.RootElement.TryGetProperty("weaponsSource", out var s)
                && s.ValueKind == JsonValueKind.String
                    ? s.GetString()?.Trim().ToLowerInvariant()
                    : null;

            return new WeaponReading(read, source);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static IReadOnlyList<StatObservation> Parse(string responseText)
    {
        ArgumentNullException.ThrowIfNull(responseText);

        var json = AdviceParser.ExtractJsonObject(responseText);

        if (json is null)
        {
            return [];
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return [];
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("observedStats", out var stats) ||
                stats.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var observations = new List<StatObservation>();

            foreach (var entry in stats.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!entry.TryGetProperty("stat", out var name) || name.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (!Enum.TryParse<TlStat>(name.GetString(), ignoreCase: true, out var stat))
                {
                    continue;
                }

                if (!entry.TryGetProperty("total", out var total) || !total.TryGetInt32(out var totalValue))
                {
                    continue;
                }

                int? baseValue = entry.TryGetProperty("base", out var b) && b.ValueKind == JsonValueKind.Number
                    && b.TryGetInt32(out var parsedBase)
                    ? parsedBase
                    : null;

                observations.Add(new StatObservation
                {
                    Stat = stat,
                    Total = totalValue,
                    Base = baseValue,
                });
            }

            return observations;
        }
    }
}

/// <summary>
/// A weapon pair the model reported, and how it came by it.
///
/// <para><b>The source is the whole point of this type existing.</b> Weapon identification is the read
/// where being wrong does the most damage and is least likely to be caught: a wrong pair names a
/// different class, and every recommendation afterwards is confidently aimed at a character the player
/// is not playing. Nothing downstream contradicts it, and the player has no way to tell.</para>
///
/// <para>So a text read and an icon guess are kept distinct all the way to the decision about whether
/// to store them. Flattening them into "the weapons" would throw away the only information that says
/// which one this is.</para>
/// </summary>
/// <param name="Weapons">Exactly two ids, already validated as one of the 45 real pairs.</param>
/// <param name="Source">
/// <c>tooltip</c>, <c>mastery</c> or <c>skills</c> — a text read. <c>icon</c> — the artwork in the
/// character sheet's weapon slots. Null when the model did not say, which is treated as an icon read:
/// the cautious assumption is the correct one when the claim is unlabelled.
/// </param>
public sealed record WeaponReading(IReadOnlyList<string> Weapons, string? Source)
{
    /// <summary>
    /// Whether this came from text the model read rather than artwork it recognised.
    ///
    /// <para>The model is reliable at text and unreliable at naming icons — this codebase has the
    /// receipts, in the boss-schedule capture where a badge that a person could see plainly turned out
    /// to be three pixels after downsampling. An unlabelled source counts as an icon read, so a model
    /// that omits the field gets the careful path rather than the trusting one.</para>
    /// </summary>
    public bool IsTextRead =>
        Source is not null
        && (Source.Equals("tooltip", StringComparison.OrdinalIgnoreCase)
            || Source.Equals("mastery", StringComparison.OrdinalIgnoreCase)
            || Source.Equals("skills", StringComparison.OrdinalIgnoreCase));

    /// <summary>The class these weapons name. Never null — the pair was validated on construction.</summary>
    public string ClassName => TlClasses.Name(Weapons[0], Weapons[1])!;

    public bool SamePairAs(IReadOnlyList<string>? other) =>
        other is { Count: 2 } && TlClasses.Name(other[0], other[1]) == ClassName;
}
