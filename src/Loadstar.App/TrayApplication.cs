using System.Text.Json;
using Loadstar.Capture.Windows;
using Loadstar.Core.Ai;
using Loadstar.Core.Capture;
using Loadstar.Core.Configuration;
using Loadstar.Core.Model;
using Loadstar.Games.ThroneAndLiberty;

namespace Loadstar.App;

/// <summary>
/// The tray icon and the capture-ask-advise flow behind the hotkey.
///
/// <para>Loadstar lives in the tray rather than owning a window because the product is "ask it when
/// you want to know", not a dashboard to alt-tab to. The hotkey is the primary entry point; the tray
/// menu exists for configuration and for the times someone wants to trigger it without the keyboard.</para>
/// </summary>
internal sealed class TrayApplication : IDisposable
{
    private readonly NotifyIcon _tray;
    private readonly HotkeyHost _hotkeys;
    private readonly SettingsStore _store;
    private readonly SecretStore _secrets;
    private readonly WindowsGraphicsCaptureSource _capture;
    private readonly ConsentGatedCaptureSource _gated;
    private readonly List<string> _recentQuestions = [];

    /// <summary>
    /// questlog's per-patch reference tables, fetched once and kept for the process lifetime.
    ///
    /// <para>They are static for a patch and total roughly a megabyte across three calls, so
    /// re-fetching them on every capture would add latency to the one action the user is waiting on
    /// for no benefit. Null means the fetch has not succeeded yet — a later capture retries.</para>
    /// </summary>
    private TraitReference? _traitReference;
    private readonly GameLaunchWatcher _launchWatcher;
    private readonly BossTimerService _bossTimer;

    private static NotifyIcon? _errorSink;
    private bool _busy;

    public TrayApplication()
    {
        _store = new SettingsStore();
        _secrets = new SecretStore(_store.Directory);
        _capture = new WindowsGraphicsCaptureSource();

        _gated = new ConsentGatedCaptureSource(
            _capture,
            hasConsent: () => _store.Load().CaptureConsentGiven,
            onCaptured: frame => ShowBalloon(
                "Screen captured",
                $"{frame.Width}x{frame.Height} from \"{frame.WindowTitle}\", " +
                $"{frame.PrivacyMasksApplied} privacy mask(s) applied."));

        _tray = new NotifyIcon
        {
            Icon = AppIcon.Shared,
            Visible = true,
            Text = "Loadstar",
            ContextMenuStrip = BuildMenu(),
        };

        _errorSink = _tray;

        // Double-clicking the tray icon is the same as the hotkey — a discoverable fallback for
        // anyone who has not learned the shortcut yet.
        _tray.DoubleClick += async (_, _) => await CaptureAndAskAsync();

        _hotkeys = new HotkeyHost();
        RegisterHotkeys();

        _bossTimer = new BossTimerService(_store.Load, _store.Save, (title, body) => ShowBalloon(title, body));

        // Reminds the user of the hotkey when the game starts, the way an overlay does. Process
        // presence is a read-only shell query — no handle to the game is opened.
        _launchWatcher = new GameLaunchWatcher(
            () => _store.Load().Capture.WindowProcessName,
            OnGameLaunched);

        if (!_capture.IsSupported)
        {
            ShowBalloon(
                "Capture unavailable",
                "Windows Graphics Capture needs Windows 10 version 2004 (build 19041) or newer.",
                ToolTipIcon.Warning);
        }
    }

    private void OnGameLaunched(string processName)
    {
        var settings = _store.Load();
        var hotkey = Hotkey.TryParse(settings.Overlay.CaptureHotkey);

        ShowBalloon(
            "Loadstar is ready",
            hotkey is null
                ? $"{processName} is running, but no capture hotkey is set. Open Settings to add one."
                : $"Press {hotkey.Display} any time to capture the screen and ask a question.");

        // A game launch is the moment the countdown becomes relevant, so re-apply rather than
        // waiting for the next settings change.
        _bossTimer.Apply();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var capture = new ToolStripMenuItem("Capture and ask…");
        capture.Click += async (_, _) => await CaptureAndAskAsync();
        capture.Font = new Font(menu.Font, FontStyle.Bold);

        var countdown = new ToolStripMenuItem("Show boss countdown") { CheckOnClick = true };
        countdown.Click += (_, _) =>
        {
            var current = _store.Load();
            _store.Save(current with { Overlay = current.Overlay with { ShowBossCountdown = countdown.Checked } });
            _bossTimer.Apply();
        };

        var lockOverlay = new ToolStripMenuItem("Lock countdown position") { CheckOnClick = true };
        lockOverlay.Click += (_, _) => _bossTimer.SetLocked(lockOverlay.Checked);

        // Reflect stored state each time the menu opens, so it stays in step with the settings page.
        menu.Opening += (_, _) =>
        {
            var overlay = _store.Load().Overlay;
            countdown.Checked = overlay.ShowBossCountdown;
            lockOverlay.Checked = overlay.CountdownLocked;
            lockOverlay.Enabled = overlay.ShowBossCountdown;
        };

        var settings = new ToolStripMenuItem("Settings…");
        settings.Click += (_, _) => ShowSettings();

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => Application.Exit();

        menu.Items.Add(capture);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(countdown);
        menu.Items.Add(lockOverlay);
        menu.Items.Add(settings);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);

        return menu;
    }

    private void RegisterHotkeys()
    {
        var configured = _store.Load().Overlay.CaptureHotkey;
        var hotkey = Hotkey.TryParse(configured);

        if (hotkey is null)
        {
            ShowBalloon("Hotkey not set", $"Could not parse \"{configured}\". Set one in Settings.", ToolTipIcon.Warning);
            return;
        }

        var failure = _hotkeys.TryRegister(hotkey, () => _ = CaptureAndAskAsync());

        if (failure is not null)
        {
            ShowBalloon("Hotkey unavailable", failure + " Pick another in Settings.", ToolTipIcon.Warning);
        }
        else
        {
            _tray.Text = $"Loadstar — press {hotkey.Display} to capture";
        }
    }

    /// <summary>
    /// The main flow: capture, show the image and ask for a question, then analyse.
    ///
    /// <para>Capture happens <em>before</em> any Loadstar window opens. Opening the prompt first
    /// would put our own window over the game and capture that instead.</para>
    /// </summary>
    private async Task CaptureAndAskAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;

        try
        {
            var settings = _store.Load();

            if (!settings.CaptureConsentGiven)
            {
                if (!ConsentPrompt.Ask(null))
                {
                    ShowBalloon("Capture stays off", "Nothing was read and nothing was sent.");
                    return;
                }

                settings = settings with
                {
                    CaptureConsentGiven = true,
                    ConsentVersionAccepted = ConsentPrompt.CurrentVersion,
                };

                _store.Save(settings);
            }

            // Before the capture, not after. Every piece of advice this app gives is relative to an
            // imported build — without one there is nothing to compare against, and the honest
            // output would be a guess at what "good" means for a stat, which is precisely what the
            // advice engine must never do. Gating here means no screenshot is taken and no question
            // is asked for a request that could not have been answered.
            if (!EnsureBuildConfigured(settings))
            {
                return;
            }

            var result = await _gated.CaptureAsync(
                new CaptureRequest
                {
                    Target = settings.Capture.ToWindowTarget(),
                    Region = ScreenRegions.FullWindow,
                    PrivacyMasks = ScreenRegions.PrivacyMasks,
                    Label = "game window",
                    Timeout = TimeSpan.FromSeconds(8),
                },
                CancellationToken.None);

            if (!result.Success)
            {
                ShowBalloon($"Capture {result.Status}", result.Detail ?? "Unknown failure.", ToolTipIcon.Warning);
                return;
            }

            var frame = result.Frame;

            using var stream = new MemoryStream(frame.Png);
            using var preview = Image.FromStream(stream);

            using var ask = new AskWindow((Image)preview.Clone(), frame.WindowTitle, _recentQuestions);

            if (ask.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            var question = ask.Question;

            if (!string.IsNullOrWhiteSpace(question))
            {
                _recentQuestions.Remove(question);
                _recentQuestions.Insert(0, question);
            }

            await AnalyseAsync(frame, question, settings);
        }
        catch (Exception ex)
        {
            ReportError("Capture failed", ex);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task AnalyseAsync(CapturedFrame frame, string question, LoadstarSettings settings)
    {
        var provider = settings.Ai.Provider;
        var providerInfo = AiCatalog.For(provider);
        var apiKey = _secrets.Resolve(provider);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ShowBalloon(
                "No API key",
                $"Add your {providerInfo.DisplayName} API key in Settings.",
                ToolTipIcon.Warning);
            return;
        }

        // Belt and braces. EnsureBuildConfigured already stopped this before the capture; this
        // second check guards any future caller that reaches AnalyseAsync by another path, because
        // the invariant being protected is "nothing is sent to a provider without a build".
        if (string.IsNullOrWhiteSpace(settings.Game.BuildUrl))
        {
            ShowBalloon("No target build", "Paste a questlog.gg build URL in Settings.", ToolTipIcon.Warning);
            return;
        }

        // Name the provider: this is the moment a screenshot leaves the machine, and which third
        // party receives it is exactly what the user should be told.
        ShowBalloon("Analysing…", $"Sending one screenshot to {providerInfo.DisplayName}.");

        // Stage-by-stage, because "no answer appeared" spans five things that can fail and the log is
        // only useful if it says which one did.
        Core.Diagnostics.Log.Info($"Analyse: fetching build {settings.Game.BuildUrl}");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var character = await new QuestlogClient(http).GetCharacterAsync(settings.Game.BuildUrl, CancellationToken.None);

        if (character is null || character.Builds.Count == 0)
        {
            Core.Diagnostics.Log.Warn($"Analyse: questlog returned no builds for {settings.Game.BuildUrl}");
            ShowBalloon("Build not found", $"questlog returned nothing for \"{settings.Game.BuildUrl}\".", ToolTipIcon.Warning);
            return;
        }

        var target = character.Builds[0];
        var allocated = TlStats.MapAllocated(target.Attributes);
        var derived = await ComputeTargetsAsync(target, http);

        Core.Diagnostics.Log.Info(
            $"Analyse: build \"{target.Name}\", {allocated.Count} allocated stats, "
            + $"targets {(derived is null ? "unavailable" : "computed")}. "
            + $"Sending {frame.Png.Length / 1024}KB to {providerInfo.DisplayName} "
            + $"as {AiProviderFactory.ResolveModel(provider, settings.Ai.Model)}.");

        using var client = AiProviderFactory.Create(provider, apiKey);

        var response = await client.AnalyzeAsync(
            new AiRequest
            {
                // Guards against a model left over from a different provider — sending
                // claude-opus-5 to Gemini returns a 404 naming a model the user never picked.
                Model = AiProviderFactory.ResolveModel(provider, settings.Ai.Model),
                Effort = settings.Ai.Effort,
                // Tell the model which language to answer in when the user has picked one
                // explicitly; on "System" it follows the language of the question instead.
                SystemPrompt = TlSystemPrompt.Build(
                    target,
                    character.Tags,
                    AppLanguages.EnglishName(settings.Language),
                    derived),
                UserPrompt = BuildUserPrompt(question, allocated),
                Images = [new CapturedImage { Png = frame.Png, Label = frame.Label }],
                MaxOutputTokens = 3000,
            },
            CancellationToken.None);

        Core.Diagnostics.Log.Info(
            $"Analyse: {providerInfo.DisplayName} replied with {response.Text?.Length ?? 0} chars.");

        // A provider that returns an empty body is a real outcome — a safety refusal, a truncation, a
        // model that answered with nothing. Left alone it renders as a blank window, which is
        // indistinguishable from the app having done nothing at all.
        if (string.IsNullOrWhiteSpace(response.Text))
        {
            Core.Diagnostics.Log.Warn("Analyse: provider returned an empty response.");

            MessageBox.Show(
                $"{providerInfo.DisplayName} accepted the screenshot but returned no text. This is "
                + "usually a rate limit, an exhausted quota, or a response cut short."
                + Environment.NewLine + Environment.NewLine
                + $"Nothing is wrong with your settings — try again, and if it repeats, check your "
                + $"{providerInfo.DisplayName} usage.",
                "Loadstar — empty response",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        var advice = AdviceParser.Parse(response.Text, DateTimeOffset.Now, response.Usage);
        var observed = TlObservationParser.Parse(response.Text);

        // The arithmetic stays ours. The model reads the numbers off the screen; StatPlanner prices
        // the move, because a correct recommendation with its cost omitted is the failure this
        // project exists to correct.
        var plan = allocated.Count > 0 && observed.Count > 0
            ? StatPlanner.Plan(observed, allocated)
            : null;

        using var window = new ResultWindow(advice, plan, question);
        window.ShowDialog();
    }

    private static string BuildUserPrompt(string question, IReadOnlyDictionary<TlStat, int> allocated)
    {
        var spread = allocated.Count > 0
            ? string.Join(", ", allocated.Select(a => $"{a.Key} {a.Value}"))
            : "(this loadout specifies none)";

        var asked = string.IsNullOrWhiteSpace(question)
            ? "The player asked nothing specific, so rank the highest-value next actions."
            : $"The player asks: \"{question}\"\n\nAnswer THAT question specifically.";

        return $"""
            Here is the player's current screen.

            {asked}

            Identify which screen this is; nobody has told you.

            The target build's allocated attribute points are: {spread}

            Report every base stat you can see in `observedStats`, with `base` included only where a
            stat tooltip actually shows the Base/Equipment/Stellar Journey breakdown. Do not compute
            the cost of any redistribution yourself — that is calculated separately from your
            readings and shown to the player alongside your advice.
            """;
    }

    private void ShowSettings()
    {
        using var settings = new SettingsWindow(_store, _secrets);

        if (settings.ShowDialog() == DialogResult.OK)
        {
            // Release the old registration and re-register so a changed hotkey takes effect now
            // rather than on next launch. Deliberately NOT Dispose() — the host window owns the
            // message queue that hotkeys are delivered to, and disposing it here crashed on every
            // Save with ObjectDisposedException.
            _hotkeys.UnregisterAll();
            RegisterHotkeys();
            _bossTimer.Apply();
        }
    }

    /// <summary>
    /// Computes what the target build's gear is worth, caching the reference tables.
    ///
    /// <para>Returns null on any failure, which omits the section from the prompt rather than
    /// blocking the capture. Losing the computed targets makes the advice weaker; failing the whole
    /// request because a reference table could not be fetched would make it useless.</para>
    /// </summary>
    private async Task<DerivedTargets?> ComputeTargetsAsync(TargetBuild target, HttpClient http)
    {
        try
        {
            _traitReference ??= await new QuestlogClient(http)
                .GetTraitReferenceAsync(CancellationToken.None);

            return new DerivedTargets
            {
                Stats = TargetStatCalculator.Compute(target, _traitReference),
                Reference = _traitReference,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Blocks the run when no build is configured, and offers to fix it.
    ///
    /// <para>A balloon would be wrong here: this is a hard precondition rather than a notification,
    /// and a balloon is dismissable, easy to miss, and leaves the user pressing the hotkey again
    /// wondering why nothing happens. Offering Settings turns a dead end into the one action that
    /// resolves it.</para>
    /// </summary>
    private bool EnsureBuildConfigured(LoadstarSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Game.BuildUrl))
        {
            return true;
        }

        var open = MessageBox.Show(
            "Loadstar needs a Character Build URL before it can give advice.\n\n"
            + "Every recommendation is measured against your target build — which stats to aim "
            + "for, which slots are behind. Without one there is nothing to compare your character "
            + "to, so no screenshot has been taken and nothing has been sent.\n\n"
            + "Open Settings and paste a questlog.gg build URL now?",
            "Loadstar — no target build",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);

        if (open == DialogResult.Yes)
        {
            ShowSettings();
        }

        return false;
    }

    private void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _tray.BalloonTipTitle = title;
        _tray.BalloonTipText = text;
        _tray.BalloonTipIcon = icon;
        _tray.ShowBalloonTip(4000);
    }

    /// <summary>
    /// Reports a failure the user was waiting on.
    ///
    /// <para>This used to show only a six-second balloon, and that produced the worst bug report this
    /// project has had: a question asked, a screenshot taken, and no answer — because the failure went
    /// to a notification Windows may suppress entirely, and nowhere else. The app knew what went wrong
    /// and there was no way to find out.</para>
    ///
    /// <para>So now it logs first, then shows a dialog that stays until dismissed. A modal for a
    /// background event would be wrong, but this only fires on an action the user explicitly started
    /// and is standing there waiting for, and silence is the worse failure.</para>
    /// </summary>
    public static void ReportError(string title, Exception ex)
    {
        Core.Diagnostics.Log.Error(title, ex);

        var detail = ex.Message;

        // Inner exceptions carry the real cause often enough to be worth surfacing: "One or more
        // errors occurred" on its own tells the user nothing they can act on or report.
        if (ex.InnerException is { } inner && !string.IsNullOrWhiteSpace(inner.Message))
        {
            detail += Environment.NewLine + Environment.NewLine + inner.Message;
        }

        var where = Core.Diagnostics.Log.Path is { } path
            ? Environment.NewLine + Environment.NewLine + $"Details were written to:{Environment.NewLine}{path}"
            : string.Empty;

        MessageBox.Show(
            $"{detail}{where}",
            $"Loadstar — {title}",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    public void Dispose()
    {
        _tray.Visible = false;
        _tray.Dispose();
        _hotkeys.Dispose();
        _launchWatcher.Dispose();
        _bossTimer.Dispose();
        _gated.Dispose();
    }
}
