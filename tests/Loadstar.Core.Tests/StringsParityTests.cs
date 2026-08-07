using System.Text.RegularExpressions;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Guards the localisation table itself, because its failures are per-language and therefore invisible
/// to whoever wrote them.
///
/// <para>Reflection rather than a reference: Strings lives in the WinForms app assembly, which this test
/// project cannot reference directly. The table is a private static dictionary keyed by language, and
/// reading it here is worth the awkwardness — the alternative is finding a mismatched placeholder when a
/// Russian user's window throws a FormatException.</para>
/// </summary>
public sealed class StringsParityTests
{
    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.Compiled);

    /// <summary>
    /// One `["key"] = "value",` entry. The value class excludes double quotes, which is safe because a
    /// raw quote inside a value would not compile as C# in the first place — so it terminates
    /// unambiguously without needing to anchor on a line ending.
    /// </summary>
    private const string KeyValue = @"\[""([^""]+)""\] = ""([^""]*)"",";

    /// <summary>
    /// Parses the table out of Strings.cs.
    ///
    /// <para>Source-level rather than reflective, because the app targets net8.0-windows and this test
    /// project targets net8.0 — it cannot load that assembly at all. That is not a workaround so much as
    /// the right level: what is being checked is the literal table a human types into, and every failure
    /// this test catches is a typo in that file.</para>
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Table()
    {
        var source = FindStringsSource();
        var text = File.ReadAllText(source);

        var blocks = Regex.Matches(text, @"\[AppLanguage\.(\w+)\] = new\(\)");
        Assert.True(blocks.Count >= 9, $"only {blocks.Count} language blocks found in {source}");

        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>();

        for (var i = 0; i < blocks.Count; i++)
        {
            var start = blocks[i].Index;
            var end = i + 1 < blocks.Count ? blocks[i + 1].Index : text.Length;

            var entries = new Dictionary<string, string>();

            // Non-greedy up to the closing quote before `,` at end of line: the values contain commas,
            // colons and brackets, but never an unescaped double quote (a separate test asserts that).
            foreach (Match entry in Regex.Matches(text[start..end], KeyValue))
            {
                entries[entry.Groups[1].Value] = entry.Groups[2].Value;
            }

            result[blocks[i].Groups[1].Value] = entries;
        }

        return result;
    }

    /// <summary>Walks up from the test assembly to the repo, so this works from any working directory.</summary>
    private static string FindStringsSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Loadstar.App", "Strings.cs");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("could not locate src/Loadstar.App/Strings.cs from " + AppContext.BaseDirectory);
    }

    /// <summary>Every language carries every key, or a player silently falls back to English mid-window.</summary>
    [Fact]
    public void EveryLanguageHasEveryKey()
    {
        var table = Table();
        var english = table["English"];

        Assert.True(english.Count > 100, $"only {english.Count} English keys — the table did not load");

        foreach (var (language, entries) in table.Where(t => t.Key != "English"))
        {
            var missing = english.Keys.Except(entries.Keys).OrderBy(k => k).ToArray();
            var extra = entries.Keys.Except(english.Keys).OrderBy(k => k).ToArray();

            Assert.True(missing.Length == 0, $"{language} is missing: {string.Join(", ", missing)}");
            Assert.True(extra.Length == 0, $"{language} has keys English does not: {string.Join(", ", extra)}");
        }
    }

    /// <summary>
    /// THE FAILURE THAT ONLY ONE LANGUAGE SEES. string.Format throws when a placeholder index is absent
    /// from the format string, so a Russian translation that drops {7} crashes the result window for
    /// Russian players and nobody else. Several of these strings carry eight placeholders.
    /// </summary>
    [Fact]
    public void EveryTranslationUsesTheSamePlaceholdersAsEnglish()
    {
        var table = Table();
        var english = table["English"];

        foreach (var (language, entries) in table.Where(t => t.Key != "English"))
        {
            foreach (var (key, value) in entries)
            {
                if (!english.TryGetValue(key, out var reference))
                {
                    continue;
                }

                var expected = Indices(reference);
                var actual = Indices(value);

                Assert.True(
                    expected.SetEquals(actual),
                    $"{language}/{key}: English uses {{{string.Join("}, {", expected.Order())}}} "
                    + $"but this uses {{{string.Join("}, {", actual.Order())}}} — string.Format would throw");
            }
        }
    }

    private static HashSet<int> Indices(string format) =>
        Placeholder.Matches(format).Select(m => int.Parse(m.Groups[1].Value)).ToHashSet();

    /// <summary>
    /// A translation that is byte-identical to English across many keys usually means a block was copied
    /// and not translated. English itself and deliberately-shared tokens are exempt.
    /// </summary>
    [Fact]
    public void NoLanguageIsSecretlyStillEnglish()
    {
        var table = Table();
        var english = table["English"];

        foreach (var (language, entries) in table.Where(t => t.Key != "English"))
        {
            var identical = entries.Count(e =>
                english.TryGetValue(e.Key, out var value) && value == e.Value);

            // Some overlap is legitimate: proper nouns, "Loadstar", short shared tokens.
            Assert.True(
                identical < entries.Count / 3,
                $"{language} matches English on {identical} of {entries.Count} keys — likely untranslated");
        }
    }
}
