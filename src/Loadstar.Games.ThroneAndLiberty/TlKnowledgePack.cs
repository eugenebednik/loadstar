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
