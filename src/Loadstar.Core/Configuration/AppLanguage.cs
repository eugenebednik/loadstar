using System.Globalization;

namespace Loadstar.Core.Configuration;

/// <summary>
/// The languages Loadstar's interface is available in.
///
/// <para>Separate from the language the <em>game</em> is running in and from the language the player
/// types their question in — all three can differ, and the system prompt handles the other two. This
/// one only decides what the buttons say.</para>
///
/// <para><b>This list intentionally differs from the game's.</b> Throne and Liberty ships text in
/// seven languages — English, French, German, Korean, Japanese, Spanish (LATAM) and Chinese
/// (Traditional) — and notably <b>not</b> Russian or Ukrainian. Those players run an English client
/// and still want the tool in their own language, so the app supports languages the game does not.
/// Do not "correct" this list to match the game's.</para>
/// </summary>
public enum AppLanguage
{
    /// <summary>Follow Windows. The default, so most users never touch this setting.</summary>
    System = 0,

    English,

    /// <summary>
    /// A Russian client exists but launched separately in Russia and is far behind the global
    /// build — still the T1 gear era, before the 4.0.0 item rewrite. Loadstar's game knowledge does
    /// not apply to it, and the system prompt makes the model say so when it sees one.
    /// </summary>
    Russian,

    /// <summary>No Ukrainian game client exists; these players run English clients.</summary>
    Ukrainian,

    Spanish,
    German,
    French,
    Japanese,
    Korean,

    /// <summary>Traditional Chinese — a language the game itself ships in.</summary>
    ChineseTraditional,
}

public static class AppLanguages
{
    /// <summary>Two-letter ISO code per language, used to match against the OS culture.</summary>
    public static string IsoCode(AppLanguage language) => language switch
    {
        AppLanguage.Russian => "ru",
        AppLanguage.Ukrainian => "uk",
        AppLanguage.Spanish => "es",
        AppLanguage.German => "de",
        AppLanguage.French => "fr",
        AppLanguage.Japanese => "ja",
        AppLanguage.Korean => "ko",
        AppLanguage.ChineseTraditional => "zh",
        _ => "en",
    };

    /// <summary>
    /// The code in an installer's filename — <c>Loadstar-&lt;version&gt;-x64-&lt;code&gt;.msi</c>.
    ///
    /// <para><b>Resolves <see cref="AppLanguage.System"/> first</b>, which is the whole reason this exists
    /// rather than callers using <see cref="IsoCode"/> directly. That maps System to English, because its job
    /// is matching an explicit choice against a culture. Here the question is which installer to download, and
    /// somebody on System with a Russian OS should get the Russian one.</para>
    /// </summary>
    public static string InstallerCode(AppLanguage language) => IsoCode(Resolve(language));

    /// <summary>The language's own name, so the picker is readable to someone who needs it.</summary>
    public static string NativeName(AppLanguage language) => language switch
    {
        AppLanguage.System => "System default",
        AppLanguage.English => "English",
        AppLanguage.Russian => "Русский",
        AppLanguage.Ukrainian => "Українська",
        AppLanguage.Spanish => "Español",
        AppLanguage.German => "Deutsch",
        AppLanguage.French => "Français",
        AppLanguage.Japanese => "日本語",
        AppLanguage.Korean => "한국어",
        AppLanguage.ChineseTraditional => "繁體中文",
        _ => language.ToString(),
    };

    /// <summary>
    /// The English name, which is what gets handed to the model when telling it which language to
    /// reply in — an instruction it follows more reliably than a bare ISO code.
    /// </summary>
    public static string? EnglishName(AppLanguage language) => language switch
    {
        AppLanguage.System => null,
        AppLanguage.ChineseTraditional => "Traditional Chinese",
        _ => language.ToString(),
    };

    /// <summary>
    /// Resolves <see cref="AppLanguage.System"/> against the OS UI culture, falling back to English
    /// for any culture that is not translated.
    /// </summary>
    public static AppLanguage Resolve(AppLanguage configured)
    {
        if (configured != AppLanguage.System)
        {
            return configured;
        }

        // TwoLetterISOLanguageName so regional variants — pt-BR, es-MX, en-GB — land on their base
        // language rather than falling through to English.
        var code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        return code switch
        {
            "ru" => AppLanguage.Russian,
            "uk" => AppLanguage.Ukrainian,
            "es" => AppLanguage.Spanish,
            "de" => AppLanguage.German,
            "fr" => AppLanguage.French,
            "ja" => AppLanguage.Japanese,
            "ko" => AppLanguage.Korean,
            "zh" => AppLanguage.ChineseTraditional,
            _ => AppLanguage.English,
        };
    }
}
