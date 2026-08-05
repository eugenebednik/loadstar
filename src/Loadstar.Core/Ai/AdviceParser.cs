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

        var json = ExtractJsonObject(responseText)
            ?? throw new AdviceParseException("The model's reply contained no JSON object.", responseText);

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

        return name is not null && Enum.TryParse<ScreenKind>(name, ignoreCase: true, out var screen)
            ? screen
            : ScreenKind.Unknown;
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
