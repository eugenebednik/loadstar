using System.Text.Json;
using Loadstar.Core.Model;

namespace Loadstar.Core.Ai;

/// <summary>
/// Turns the model's reply into <see cref="Advice"/>.
///
/// <para>Tolerant on the way in, strict on the way out. Models wrap JSON in prose or fence it as
/// markdown often enough that failing the whole capture over a stray "Here's the plan:" would be
/// the single most common failure in the app, so the object is extracted rather than assumed to be
/// the entire response. What is <em>not</em> tolerated is inventing data: a missing field becomes a
/// missing field, never a plausible default, because a fabricated cost is worse than no cost.</para>
/// </summary>
public static class AdviceParser
{
    public static Advice Parse(string responseText, DateTimeOffset generatedAt, TokenUsage? usage = null)
    {
        ArgumentNullException.ThrowIfNull(responseText);

        // "No JSON object" and "an object that was cut off" look the same to the extractor and have
        // OPPOSITE fixes: one means the model ignored the output contract, the other means the reply
        // budget ran out. Reporting them identically sent one investigation at the prompt when the
        // cause was a token ceiling, so they are now told apart before the message is written.
        var json = ExtractJsonObject(responseText)
            ?? throw new AdviceParseException(
                responseText.Contains('{', StringComparison.Ordinal)
                    ? "The model's reply started a JSON object but was cut off before finishing it — "
                      + "the reply was truncated, most likely because the output token budget ran out."
                    : "The model's reply contained no JSON object at all.",
                responseText);

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new AdviceParseException($"The model's reply was not valid JSON: {ex.Message}", responseText);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new AdviceParseException("Expected a JSON object at the root.", responseText);
            }

            return new Advice
            {
                GeneratedAt = generatedAt,
                Headline = ReadString(root, "headline") ?? "(no headline)",
                RecognizedScreen = ReadScreen(root),
            Screens = ReadScreens(root),
            SuggestedBuilds = ReadSuggestedBuilds(root),
                AnsweredFromScreen = !root.TryGetProperty("answeredFromScreen", out var answered)
                    || answered.ValueKind != JsonValueKind.False,
                Steps = ReadSteps(root),
                MissingInformation = ReadStringArray(root, "missingInformation"),
                Usage = usage,
            };
        }
    }

    /// <summary>
    /// Finds the outermost JSON object in a reply that may also contain prose or a code fence.
    ///
    /// <para>Brace matching rather than a regex, and string-aware, so a brace inside an item name or
    /// a rationale does not truncate the object halfway.</para>
    /// </summary>
    public static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');

        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (inString)
            {
                if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;

                    if (depth == 0)
                    {
                        return text[start..(i + 1)];
                    }

                    break;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the model's own identification of the screen. Unrecognised values become
    /// <see cref="ScreenKind.Unknown"/> rather than throwing — a new or unexpected screen name is
    /// information, not a reason to discard otherwise good advice.
    /// </summary>
    private static ScreenKind ReadScreen(JsonElement root)
    {
        var name = ReadString(root, "screen");

        if (name is not null && Enum.TryParse<ScreenKind>(name, ignoreCase: true, out var screen))
        {
            return screen;
        }

        // Falls back to the first entry of the per-screen list, so a model that filled in `screens` and
        // omitted the singular field still gets its reading reported rather than "Unknown".
        return ReadScreens(root).FirstOrDefault()?.Screen ?? ScreenKind.Unknown;
    }

    /// <summary>
    /// Reads the per-screenshot readings, one per image sent.
    ///
    /// <para>Optional throughout. A model that reports only the old single <c>screen</c> field yields an
    /// empty list, which the UI treats as "nothing extra to say" rather than as an error — this arrived
    /// after the single field and must not break a reply that predates it.</para>
    /// </summary>
    private static IReadOnlyList<ScreenReading> ReadScreens(JsonElement root)
    {
        if (!root.TryGetProperty("screens", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var readings = new List<ScreenReading>();

        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = ReadString(entry, "screen");

            readings.Add(new ScreenReading
            {
                Screen = name is not null && Enum.TryParse<ScreenKind>(name, ignoreCase: true, out var kind)
                    ? kind
                    : ScreenKind.Unknown,
                // Defaults to true when absent: a screen the model bothered to list and describe was
                // almost certainly read, and defaulting to false would accuse it of ignoring something.
                Used = !entry.TryGetProperty("used", out var used)
                    || used.ValueKind != JsonValueKind.False,
                Note = ReadString(entry, "note"),
            });
        }

        return readings;
    }

    /// <summary>
    /// Reads the proposed builds. Absent yields an empty list — a model that omits the field must not break
    /// an otherwise good answer, and the UI simply shows no proposals.
    /// </summary>
    private static IReadOnlyList<SuggestedBuild> ReadSuggestedBuilds(JsonElement root)
    {
        if (!root.TryGetProperty("suggestedBuilds", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var builds = new List<SuggestedBuild>();

        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = ReadString(entry, "name");

            // A proposal with no name is nothing a player can act on, so it is dropped rather than rendered
            // as a blank row.
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            builds.Add(new SuggestedBuild
            {
                Name = name,
                Role = ReadString(entry, "role"),
                Axis = ReadString(entry, "axis"),
                Url = ReadString(entry, "url"),
                Why = ReadString(entry, "why"),
            });
        }

        return builds;
    }

    private static IReadOnlyList<AdviceStep> ReadSteps(JsonElement root)
    {
        if (!root.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<AdviceStep>();
        var rank = 0;

        foreach (var step in steps.EnumerateArray())
        {
            if (step.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            rank++;

            result.Add(new AdviceStep
            {
                // Trust position over a self-reported rank: models renumber inconsistently when
                // they revise a list, and the order they emitted is what they actually meant.
                Rank = rank,
                Action = ReadString(step, "action") ?? "(no action given)",
                Rationale = ReadString(step, "rationale") ?? string.Empty,
                Category = ReadString(step, "category"),
                Cost = ReadCost(step),
                Affordable = !step.TryGetProperty("affordable", out var affordable)
                    || affordable.ValueKind != JsonValueKind.False,
            });
        }

        return result;
    }

    private static IReadOnlyDictionary<string, long> ReadCost(JsonElement step)
    {
        var cost = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        if (step.TryGetProperty("cost", out var costs) && costs.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in costs.EnumerateObject())
            {
                if (entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetInt64(out var value))
                {
                    cost[entry.Name] = value;
                }
            }
        }

        return cost;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return array.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString() ?? string.Empty)
            .Where(x => x.Length > 0)
            .ToArray();
    }
}

public sealed class AdviceParseException : Exception
{
    public AdviceParseException(string message, string responseText) : base(message)
    {
        ResponseText = responseText;
    }

    /// <summary>The raw reply, so a failure can be shown rather than merely reported.</summary>
    public string ResponseText { get; }
}
