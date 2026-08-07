using System.Reflection;
using System.Text;

namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// The game-mechanics reference embedded into the system prompt.
///
/// <para><b>Why this is one big static block rather than retrieval.</b> The system prompt is the
/// cacheable prefix (see docs/conversation-model.md), so a large but <em>byte-stable</em> knowledge
/// pack bills at roughly a tenth of input price on every turn after the first. Selecting relevant
/// sections per question would change the prefix each turn and <em>destroy that cache</em>, making
/// every request pay full price — the opposite of the intended saving, and it would risk omitting
/// the section that mattered.</para>
///
/// <para><b>What deliberately stays out.</b> Anything large and queryable: the 1,773-item catalogue,
/// drop tables, auction prices. Those are looked up locally and only the relevant rows are injected
/// per turn. The prompt carries <em>rules</em>; code carries <em>data</em>.</para>
///
/// <para>Stored as markdown files rather than string constants so the knowledge stays readable as
/// documents. They are loaded in filename order, which is why they are numbered.</para>
/// </summary>
public static class TlKnowledgePack
{
    private static readonly Lazy<string> Content = new(Load, isThreadSafe: true);

    /// <summary>The full reference, ordered and concatenated. Byte-stable for a given build.</summary>
    public static string Text => Content.Value;

    /// <summary>Rough token estimate, for keeping an eye on the prompt's size.</summary>
    public static int EstimatedTokens => Text.Length / 4;

    /// <summary>
    /// The pack with the per-class profile section reduced to just <paramref name="className"/>.
    ///
    /// <para><b>Why this exists.</b> There are 45 classes and a player is exactly one of them. Those
    /// sections are 10,200 characters — about 2,500 tokens — of which the relevant 227 belong to the
    /// player's class and the rest describe characters they are not playing. Carrying all of it is not
    /// just a cost: this pack's own header warns that an unbounded prompt dilutes attention, and 44
    /// irrelevant profiles sitting next to the right one is exactly that dilution.</para>
    ///
    /// <para>Caching is unaffected. The prompt prefix already varies per session because it embeds the
    /// player's pinned build, and what caching needs is stability WITHIN a session, which this keeps —
    /// the class does not change mid-conversation.</para>
    ///
    /// <para>An unknown or unrecognised class returns the pack whole. That is the honest fallback: with
    /// no class identified there is no basis for choosing which profile to keep, and dropping all of
    /// them would silently remove knowledge the model might need.</para>
    /// </summary>
    public static string ForClass(string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            return Text;
        }

        var text = Text;
        var start = text.IndexOf(PerClassHeading, StringComparison.Ordinal);
        var end = text.IndexOf(AfterPerClassHeading, StringComparison.Ordinal);

        // If the profile document is restructured these markers stop matching, and returning the pack
        // intact is the right failure — a filter that silently drops the wrong span would be worse.
        if (start < 0 || end < 0 || end <= start)
        {
            return text;
        }

        var block = text[start..end];
        var mine = ExtractSection(block, className);

        if (mine is null)
        {
            return text;
        }

        var kept = new StringBuilder()
            .AppendLine(PerClassHeading)
            .AppendLine()
            .AppendLine($"Only the player's own class is included here. The other 44 profiles were left out "
                + $"deliberately — they describe characters this player is not playing, and 44 of them beside "
                + $"the right one is noise. If you need another class for a comparison, say so rather than "
                + $"guessing at it.")
            .AppendLine()
            .Append(mine)
            .ToString();

        return text[..start] + kept + text[end..];
    }

    private const string PerClassHeading = "## Per class";
    private const string AfterPerClassHeading = "## How to use a profile in an answer";

    /// <summary>One class's `### Name — weapons` section, or null when it is not present.</summary>
    private static string? ExtractSection(string block, string className)
    {
        var marker = $"### {className.Trim()} —";
        var start = block.IndexOf(marker, StringComparison.Ordinal);

        if (start < 0)
        {
            return null;
        }

        var next = block.IndexOf("\n### ", start + marker.Length, StringComparison.Ordinal);

        return next < 0 ? block[start..] : block[start..next] + "\n";
    }

    /// <summary>Section names, for diagnostics and for the settings page.</summary>
    public static IReadOnlyList<string> Sections => ResourceNames()
        .Select(name => name.Split('.').Reverse().Skip(1).First())
        .ToArray();

    private static IReadOnlyList<string> ResourceNames()
    {
        var assembly = Assembly.GetExecutingAssembly();

        return assembly.GetManifestResourceNames()
            .Where(name => name.Contains(".Knowledge.", StringComparison.Ordinal)
                && name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            // Ordinal sort on the numbered filenames keeps the sections in a fixed order. That is
            // not cosmetic: a reordered prompt is a different prefix and a cache miss.
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var builder = new StringBuilder();

        foreach (var name in ResourceNames())
        {
            using var stream = assembly.GetManifestResourceStream(name);

            if (stream is null)
            {
                continue;
            }

            using var reader = new StreamReader(stream);

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine(reader.ReadToEnd().TrimEnd());
        }

        if (builder.Length == 0)
        {
            throw new InvalidOperationException(
                "No knowledge files were embedded. The advice engine would run with no game " +
                "mechanics at all, which is worse than failing loudly.");
        }

        return builder.ToString().TrimEnd();
    }
}
