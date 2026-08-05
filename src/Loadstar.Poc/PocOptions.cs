namespace Loadstar.Poc;

/// <summary>Command-line options for the proof of concept.</summary>
internal sealed record PocOptions
{
    /// <summary>
    /// questlog build URL or bare slug. Null falls back to the Character Build URL in settings, and
    /// if that is empty too the run stops.
    ///
    /// <para>This used to default to the reference build from CLAUDE.md, which was convenient and
    /// wrong: advice is only meaningful relative to the player's own target, so a silent default
    /// meant a run could be measured against somebody else's build without ever saying so.</para>
    /// </summary>
    public string? Build { get; init; }

    public string WindowTitle { get; init; } = "THRONE AND LIBERTY";

    /// <summary>
    /// Process name of the game client, e.g. <c>TL</c>. Outranks the title, and is the only
    /// reliable key — a title substring once matched a browser showing a build guide.
    /// </summary>
    public string? ProcessName { get; init; }

    /// <summary>Pick the window interactively from what is running.</summary>
    public bool PickWindow { get; init; }

    /// <summary>The player's question, asked alongside the screenshot.</summary>
    public string? Ask { get; init; }

    /// <summary>1-based loadout index. Null prompts, because a character holds several.</summary>
    public int? Loadout { get; init; }

    public string Model { get; init; } = "claude-opus-5";

    /// <summary>Where to write the captured PNG, so the user can see exactly what was sent.</summary>
    public string? SaveCapture { get; init; }

    public bool AssumeYes { get; init; }

    /// <summary>Import and prompt-build only. Useful without an API key, and it costs nothing.</summary>
    public bool DryRun { get; init; }

    public bool ShowHelp { get; init; }

    public static PocOptions Parse(string[] args)
    {
        var options = new PocOptions();

        for (var i = 0; i < args.Length; i++)
        {
            string? Next() => i + 1 < args.Length ? args[++i] : null;

            options = args[i] switch
            {
                "--build" or "-b" => options with { Build = Next() ?? options.Build },
                "--window" or "-w" => options with { WindowTitle = Next() ?? options.WindowTitle },
                "--process" or "-p" => options with { ProcessName = Next() },
                "--pick" => options with { PickWindow = true },
                "--ask" or "-a" => options with { Ask = Next() },
                "--loadout" or "-l" => options with { Loadout = int.TryParse(Next(), out var n) ? n : null },
                "--model" or "-m" => options with { Model = Next() ?? options.Model },
                "--save-capture" => options with { SaveCapture = Next() },
                "--yes" or "-y" => options with { AssumeYes = true },
                "--dry-run" => options with { DryRun = true },
                "--help" or "-h" => options with { ShowHelp = true },
                _ => options,
            };
        }

        return options;
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
            loadstar — proof of concept: import a build, take one screenshot, produce advice.

              --build, -b <url|slug>   questlog.gg build URL or slug
              --process, -p <name>     Game process name, e.g. TL. The reliable way to target it
              --window, -w <title>     Window title substring (fallback; can match a browser)
              --pick                   Choose the window from what is currently running
              --ask, -a "<question>"   Ask something specific about the captured screen
              --loadout, -l <n>        Loadout number (a character holds several)
              --model, -m <id>         Model id
              --save-capture <path>    Write the PNG that was sent, so you can see it
              --dry-run                Import and build the prompt; make no API call
              --yes, -y                Give capture consent non-interactively
              --help, -h               This

            Needs ANTHROPIC_API_KEY in the environment unless --dry-run.
            """);
    }
}
