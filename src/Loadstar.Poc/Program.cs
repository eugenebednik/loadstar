using System.Text.Json;
using Loadstar.Capture.Windows;
using Loadstar.Core.Ai;
using Loadstar.Core.Capture;
using Loadstar.Core.Configuration;
using Loadstar.Core.Model;
using Loadstar.Games.ThroneAndLiberty;
using Loadstar.Poc;

// Proof of concept: import a questlog build, take one screenshot, produce advice with costs quoted.
//
// The shape worth noticing is the division of labour. The model reads numbers off the screen,
// which it is reliable at. StatPlanner prices the moves, which the model is not reliable at — and
// the recorded failure this project is correcting was exactly a correct recommendation with its
// cost left out. So the arithmetic is computed here and printed as authoritative.

var options = PocOptions.Parse(args);

if (options.ShowHelp)
{
    PocOptions.PrintUsage();
    return 0;
}

try
{
    return await RunAsync(options);
}
catch (Exception ex) when (ex is AiProviderException or AdviceParseException or CaptureException)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"  Failed: {ex.Message}");

    if (ex is AdviceParseException parse)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("  The model replied with:");
        Console.Error.WriteLine(Indent(parse.ResponseText, "    "));
    }

    return 1;
}

async Task<int> RunAsync(PocOptions opts)
{
    Console.WriteLine();
    Console.WriteLine("  Loadstar — proof of concept");
    Console.WriteLine("  ===========================");

    // SettingsStore, not a local copy. A PocSettings class here duplicated it against the same
    // file but deserialized with default options — so the camelCase the app writes matched nothing
    // and every setting read back as its default. Harmless while the only field read was a consent
    // flag the PoC also wrote itself; not harmless now that provider, model and build come from it.
    var settingsStore = new SettingsStore();
    var settings = settingsStore.Load();

    // ---- 1. Import the target build -------------------------------------------------
    //
    // First, and it gates everything after it. Advice is always relative to a target build; with
    // none there is nothing to measure the character against, and the only thing left to say would
    // be a guess at what a "good" value for a stat is. So the run stops here rather than capturing
    // a screenshot and spending tokens on a question that cannot be answered properly.
    var buildRef = string.IsNullOrWhiteSpace(opts.Build) ? settings.Game.BuildUrl : opts.Build;

    if (string.IsNullOrWhiteSpace(buildRef))
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("  No target build. Pass --build <questlog-slug-or-url>, or set a Character");
        Console.Error.WriteLine("  Build URL in the app's Settings.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  Advice is always measured against a target build, so there is nothing to");
        Console.Error.WriteLine("  say without one. Nothing was captured and nothing was sent.");
        return 1;
    }

    Console.WriteLine();
    Console.WriteLine($"  Importing build: {buildRef}");

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    var questlog = new QuestlogClient(http);

    var character = await questlog.GetCharacterAsync(buildRef, CancellationToken.None);

    if (character is null)
    {
        Console.Error.WriteLine($"  questlog returned nothing for \"{buildRef}\".");
        Console.Error.WriteLine("  The slug is the LAST path segment of a build URL, not the author's profile id.");
        return 1;
    }

    Console.WriteLine($"    {character.Name}{(character.Level is { } lv ? $" (level {lv})" : string.Empty)}");

    if (character.Tags.Count > 0)
    {
        Console.WriteLine($"    Tags: {string.Join(", ", character.Tags)}");
    }

    var target = ChooseLoadout(character, opts.Loadout);

    if (target is null)
    {
        return 1;
    }

    Console.WriteLine($"    Loadout: {target.Name} ({target.Equipment.Count} slots)");

    var allocated = TlStats.MapAllocated(target.Attributes);

    if (allocated.Count > 0)
    {
        Console.WriteLine($"    Target spread (allocated): " +
            string.Join(", ", allocated.Select(a => $"{a.Key} {a.Value}")));
    }
    else
    {
        Console.WriteLine("    This loadout carries no target stat spread, so no redistribution can be priced.");
    }

    // ---- 2. Consent, then capture ---------------------------------------------------
    if (!settings.CaptureConsentGiven || settings.ConsentVersionAccepted != ConsentPrompt.CurrentVersion)
    {
        if (!ConsentPrompt.Ask(opts.AssumeYes))
        {
            Console.WriteLine("  Capture stays off. Nothing was read and nothing was sent.");
            return 0;
        }

        settings = settings with
        {
            CaptureConsentGiven = true,
            ConsentVersionAccepted = ConsentPrompt.CurrentVersion,
        };

        settingsStore.Save(settings);
    }

    using var captureSource = new WindowsGraphicsCaptureSource();

    var capture = new ConsentGatedCaptureSource(
        captureSource,
        hasConsent: () => settingsStore.Load().CaptureConsentGiven,
        onCaptured: frame => Console.WriteLine(
            $"    [capture] {frame.Width}x{frame.Height} from \"{frame.WindowTitle}\", " +
            $"{frame.PrivacyMasksApplied} privacy mask(s) applied"));

    var windowTarget = ResolveTarget(opts);

    if (windowTarget is null)
    {
        return 1;
    }

    Console.WriteLine();
    Console.WriteLine($"  Capturing {windowTarget}");

    // Full window, not a crop: the character sheet is the highest-value screen and panels are
    // draggable, so locating them is the model's job. Only the currency bar is safe to crop.
    var result = await capture.CaptureAsync(
        new CaptureRequest
        {
            Target = windowTarget,
            Region = ScreenRegions.ForScreen(TlScreen.CharacterSheet),
            PrivacyMasks = ScreenRegions.PrivacyMasks,
            Label = "game window",
            Timeout = TimeSpan.FromSeconds(8),
        },
        CancellationToken.None);

    if (!result.Success)
    {
        Console.Error.WriteLine($"    Capture {result.Status}: {result.Detail}");

        if (result.Status == CaptureStatus.WindowNotFound)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("    Running windows (use --process or --pick to target one):");

            foreach (var window in GameWindowLocator.ListVisibleWindows())
            {
                Console.Error.WriteLine($"      {window}");
            }
        }

        return 1;
    }

    var frame = result.Frame;

    if (opts.SaveCapture is { } path)
    {
        await File.WriteAllBytesAsync(path, frame.Png);
        Console.WriteLine($"    Saved to {path}");
    }

    // ---- 3. Build the prompt --------------------------------------------------------
    //
    // The target's gear contributions are computed from questlog's static per-patch tables. A
    // failure here is not fatal: the prompt simply omits the section and the model reasons as it
    // did before, which is worse advice but not wrong advice.
    DerivedTargets? derived = null;

    try
    {
        var reference = await questlog.GetTraitReferenceAsync(CancellationToken.None);

        derived = new DerivedTargets
        {
            Stats = TargetStatCalculator.Compute(target, reference),
            Reference = reference,
        };

        Console.WriteLine($"    Target gear: {derived.Stats.ByStat.Count} stats, " +
            $"{derived.Stats.Sets.Count} set(s), {derived.Stats.UnresolvedContributions.Count} unresolved");
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
    {
        Console.WriteLine($"    Target gear stats unavailable ({ex.Message}). Continuing without them.");
    }

    var systemPrompt = TlSystemPrompt.Build(target, character.Tags, null, derived);

    Console.WriteLine();
    Console.WriteLine($"  System prompt: {systemPrompt.Length:N0} chars (~{systemPrompt.Length / 4:N0} tokens)");

    if (opts.DryRun)
    {
        Console.WriteLine("  Dry run — stopping before the API call.");
        Console.WriteLine();
        Console.WriteLine(Indent(systemPrompt, "  | "));
        return 0;
    }

    // Same resolution as the tray app — store first, then the provider's environment variable —
    // and now through one shared method rather than each shell spelling the order out with a
    // hardcoded variable name. The two disagreeing was already a latent bug; with three providers
    // it would have been three copies to keep in step.
    var providerKind = settings.Ai.Provider;
    var providerInfo = AiCatalog.For(providerKind);
    var apiKey = new SecretStore(settingsStore.Directory).Resolve(providerKind);

    if (string.IsNullOrWhiteSpace(apiKey))
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(
            $"  No {providerInfo.DisplayName} API key. Add one in the app's Settings, "
            + $"set {providerInfo.EnvironmentVariable}, or use --dry-run.");
        return 1;
    }

    // ---- 4. Ask the model -----------------------------------------------------------
    var model = AiProviderFactory.ResolveModel(providerKind, opts.Model);

    Console.WriteLine($"  Asking {providerInfo.DisplayName} / {model}...");

    using var provider = AiProviderFactory.Create(providerKind, apiKey);

    var response = await provider.AnalyzeAsync(
        new AiRequest
        {
            Model = model,
            Effort = settings.Ai.Effort,
            SystemPrompt = systemPrompt,
            UserPrompt = BuildUserPrompt(allocated, opts.Ask),
            Images = [new CapturedImage { Png = frame.Png, Label = frame.Label }],
        },
        CancellationToken.None);

    var advice = AdviceParser.Parse(response.Text, DateTimeOffset.Now, response.Usage);
    var observed = TlObservationParser.Parse(response.Text);

    // ---- 5. Report ------------------------------------------------------------------
    PrintAdvice(advice, response.Usage);
    PrintStatPlan(observed, allocated);
    PrintPreconditions(advice);

    return 0;
}

/// <summary>
/// Resolves which window to read, preferring an explicit process name.
/// </summary>
static WindowTarget? ResolveTarget(PocOptions opts)
{
    if (opts.PickWindow)
    {
        var windows = GameWindowLocator.ListVisibleWindows();

        Console.WriteLine();
        Console.WriteLine("  Running windows:");

        for (var i = 0; i < windows.Count; i++)
        {
            Console.WriteLine($"    {i + 1}. {windows[i]}");
        }

        Console.Write($"  Which window? [1-{windows.Count}] ");

        if (!int.TryParse(Console.ReadLine(), out var chosen) || chosen < 1 || chosen > windows.Count)
        {
            Console.Error.WriteLine("  No window chosen.");
            return null;
        }

        // Store the process name, not the title. Titles change as the player moves through the
        // game; the process does not.
        return WindowTarget.ForProcess(windows[chosen - 1].ProcessName);
    }

    return opts.ProcessName is { } process
        ? WindowTarget.ForProcess(process)
        : WindowTarget.ForTitle(opts.WindowTitle);
}

static string BuildUserPrompt(IReadOnlyDictionary<TlStat, int> allocated, string? question)
{
    var spread = allocated.Count > 0
        ? string.Join(", ", allocated.Select(a => $"{a.Key} {a.Value}"))
        : "(this loadout specifies none)";

    var asked = string.IsNullOrWhiteSpace(question)
        ? "The player asked nothing specific, so rank the highest-value next actions."
        : $"The player asks: \"{question.Trim()}\"\n\nAnswer THAT question specifically.";

    return $"""
        Here is the player's current screen.

        {asked}

        Identify which screen this is; nobody has told you.

        The target build's allocated attribute points are: {spread}

        Report every base stat you can see in `observedStats`, with `base` included only where a
        stat tooltip actually shows the Base/Equipment/Stellar Journey breakdown. Do not compute
        the cost of any redistribution yourself — that is calculated separately from your readings
        and shown to the player alongside your advice.
        """;
}

static TargetBuild? ChooseLoadout(CharacterBuilds character, int? requested)
{
    if (character.Builds.Count == 0)
    {
        Console.Error.WriteLine("  That character has no loadouts.");
        return null;
    }

    if (requested is { } index)
    {
        if (index < 1 || index > character.Builds.Count)
        {
            Console.Error.WriteLine($"  Loadout {index} is out of range (1..{character.Builds.Count}).");
            return null;
        }

        return character.Builds[index - 1];
    }

    if (!character.RequiresSelection)
    {
        return character.Builds[0];
    }

    // A questlog build URL does not resolve to one build. Collapsing them would silently advise
    // against a loadout the player is not running.
    Console.WriteLine();
    Console.WriteLine($"  This character has {character.Builds.Count} loadouts:");

    for (var i = 0; i < character.Builds.Count; i++)
    {
        var build = character.Builds[i];
        var weapons = build.WeaponTypes.Count > 0 ? $" — {string.Join(" + ", build.WeaponTypes)}" : string.Empty;
        Console.WriteLine($"    {i + 1}. {build.Name}{weapons}");
    }

    Console.Write($"  Which one? [1-{character.Builds.Count}, default 1] ");
    var answer = Console.ReadLine();

    return int.TryParse(answer, out var chosen) && chosen >= 1 && chosen <= character.Builds.Count
        ? character.Builds[chosen - 1]
        : character.Builds[0];
}

static void PrintAdvice(Advice advice, TokenUsage? usage)
{
    Console.WriteLine();
    Console.WriteLine("  Advice");
    Console.WriteLine("  ------");
    Console.WriteLine();
    Console.WriteLine($"  Screen recognised as: {advice.RecognizedScreen}");

    if (!advice.AnsweredFromScreen)
    {
        Console.WriteLine("  NOTE: the model says this screen cannot answer what was asked.");
    }

    Console.WriteLine();
    Console.WriteLine($"  {advice.Headline}");

    foreach (var step in advice.Steps)
    {
        Console.WriteLine();
        Console.WriteLine($"  {step.Rank}. {step.Action}");

        if (!string.IsNullOrWhiteSpace(step.Category))
        {
            Console.WriteLine($"     [{step.Category}]");
        }

        if (step.Cost.Count > 0)
        {
            var costs = string.Join(", ", step.Cost.Select(c => $"{c.Value:N0} {c.Key}"));
            Console.WriteLine($"     Cost: {costs}{(step.Affordable ? string.Empty : "  — NOT AFFORDABLE")}");
        }
        else
        {
            Console.WriteLine("     Cost: free");
        }

        if (!string.IsNullOrWhiteSpace(step.Rationale))
        {
            Console.WriteLine(Indent(step.Rationale, "     "));
        }
    }

    if (usage is not null)
    {
        Console.WriteLine();
        Console.WriteLine($"  Tokens: {usage.InputTokens:N0} in, {usage.OutputTokens:N0} out");
    }
}

/// <summary>
/// The locally computed half. Printed separately from the model's advice, and labelled as
/// computed, so it is obvious which numbers are arithmetic and which are a reading.
/// </summary>
static void PrintStatPlan(IReadOnlyList<StatObservation> observed, IReadOnlyDictionary<TlStat, int> allocated)
{
    if (allocated.Count == 0)
    {
        return;
    }

    Console.WriteLine();
    Console.WriteLine("  Stat redistribution — computed locally, not by the model");
    Console.WriteLine("  --------------------------------------------------------");
    Console.WriteLine();

    if (observed.Count == 0)
    {
        Console.WriteLine("  The model reported no stat readings, so there is nothing to compare.");
        Console.WriteLine("  Open the character sheet and capture again.");
        return;
    }

    Console.WriteLine("  Read from the screen: " +
        string.Join(", ", observed.Select(o => $"{o.Stat} {o.Total}{(o.Base is { } b ? $" (base {b})" : string.Empty)}")));
    Console.WriteLine();

    var plan = StatPlanner.Plan(observed, allocated);

    Console.WriteLine(Indent(plan.Describe(), "  "));

    Console.WriteLine();
    Console.WriteLine("  Assumptions behind these numbers:");

    foreach (var caveat in RedistributionPlan.Caveats)
    {
        Console.WriteLine(Indent("- " + caveat, "  "));
    }
}

static void PrintPreconditions(Advice advice)
{
    if (advice.MissingInformation.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("  The model could not see:");

        foreach (var missing in advice.MissingInformation)
        {
            Console.WriteLine($"    - {missing}");
        }
    }

    // Re-checked after every capture, not just at startup: the player can collapse the currency
    // bar mid-session, and advice about a wallet we cannot see is advice not to act on.
    var failures = CapturePreconditions.Evaluate(currenciesRead: 0, hasCurrencyReference: false);

    if (failures.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("  Setup that would improve the next run:");

        foreach (var check in failures)
        {
            Console.WriteLine($"    [{check.Severity}] {check.Title}");
        }
    }
}

static string Indent(string text, string prefix) =>
    string.Join(
        Environment.NewLine,
        text.Replace("\r\n", "\n").Split('\n').Select(line => prefix + line));

