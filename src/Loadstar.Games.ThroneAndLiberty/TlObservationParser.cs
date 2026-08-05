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
