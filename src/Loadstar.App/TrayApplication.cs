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

    /// <summary>
    /// The equipment catalogue, fetched once and kept. Same reasoning as the trait reference above: 10.4MB
    /// of per-patch data that would add nothing to re-read on every capture.
    /// </summary>
    private EquipmentCatalog? _catalog;
    private readonly GameLaunchWatcher _launchWatcher;
    private readonly BossTimerService _bossTimer;

    private static NotifyIcon? _errorSink;
    private bool _busy;

    /// <summary>
    /// The answer window while it is open, so the hotkey can say something useful instead of nothing.
    ///
    /// <para>Pressing the hotkey with this up used to do exactly nothing — silently, because the guard
    /// was a bare <c>if (_busy) return;</c>. The player has no way to tell that from a broken hotkey.</para>
    /// </summary>
    private ResultWindow? _openResult;

    /// <summary>
    /// The question window while it is open. Separate from <see cref="_openResult"/> because the right
    /// thing to say differs: this one already HAS a Retake button, so the answer is to point at it rather
    /// than to queue anything.
    /// </summary>
    private AskWindow? _openAsk;

    /// <summary>
    /// Set when the hotkey fires while the answer window is open: the capture is owed, and runs once the
    /// window closes.
    ///
    /// <para>It cannot run immediately for the same reason the retake loop exists — the answer window is
    /// ON SCREEN and the capture is a region grab of the game's bounds, so it would photograph our own
    /// window. Hence the countdown afterwards rather than an instant shot: the player still needs a moment
    /// to bring the game forward, and the countdown is cancellable if the press was an accident.</para>
    /// </summary>
    private bool _captureQueued;

    /// <summary>
    /// The games this build can advise on. One today; the player will choose here once there are
    /// several. Registered rather than discovered so what ships is readable in one place.
    /// </summary>
    private readonly Core.Games.GameCatalog _games = new(new ThroneAndLibertyModule());

    /// <summary>
    /// The module for the configured game.
    ///
    /// <para>Falls back to the default when the stored id is unknown, and SAYS SO — a settings file
    /// naming a game this build does not have is worth reporting, because silently advising on a
    /// different game is exactly the confidently-wrong outcome to avoid.</para>
    /// </summary>
    private Core.Games.IGameModule Game
    {
        get
        {
            var configured = _store.Load().GameId;
            var module = _games.Find(configured);

            if (module is not null)
            {
                return module;
            }

            Core.Diagnostics.Log.Warn(
                $"Game: settings name '{configured}', which this build does not have. "
                + $"Using {_games.Default.DisplayName}.");

            return _games.Default;
        }
    }

    public TrayApplication()
    {
        _store = new SettingsStore();
        _secrets = new SecretStore(_store.Directory);
        _capture = new WindowsGraphicsCaptureSource();

        // Every launch, as requested, and fire-and-forget: a countdown must never wait on a time
        // server. Until it lands, TimeSync falls through to the system clock, so the worst case is the
        // behaviour that existed before this — the first second or two of a session is uncorrected.
        _ = SynchroniseTimeAsync();

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

        // Repairs an autostart entry left pointing at a previous install location. Only rewrites when one
        // already exists, so it can never turn the feature on by itself — see StartupRegistration for why
        // the alternative fails so quietly: the checkbox keeps reading "on" and the app simply stops
        // starting.
        if (new Core.Startup.StartupRegistration(new RunKeyStartupKey(), Environment.ProcessPath).Synchronise())
        {
            Core.Diagnostics.Log.Info(
                $"Autostart: entry pointed elsewhere, repointed at {Environment.ProcessPath}.");
        }

        _bossTimer = new BossTimerService(_store.Load, _store.Save, (title, body) => ShowBalloon(title, body), _store.Directory);

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

    /// <summary>
    /// Checks the machine's clock against several independent sources, once per launch.
    ///
    /// <para>Worth doing because every countdown is <c>spawn - now</c> and nothing inside the app could
    /// notice a wrong clock — the schedule would be right, the arithmetic right, and the answer wrong.
    /// </para>
    ///
    /// <para>Balloons only when several sources AGREE the drift is large, and only to report it — the
    /// system clock is never altered. See <see cref="Core.Time.TimeSync"/> for why consensus rather than
    /// a single time service: the first version of this trusted one, was told this machine was 21 minutes
    /// fast, and would have injected that error into a clock that was in fact accurate to 93ms.</para>
    /// </summary>
    private async Task SynchroniseTimeAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            await Core.Time.TimeSync.SynchroniseAsync(http, CancellationToken.None);

            if (!Core.Time.TimeSync.IsClockNoticeablyWrong)
            {
                return;
            }

            var offset = Core.Time.TimeSync.Offset;
            var ahead = offset < TimeSpan.Zero;

            ShowBalloon(
                Strings.Get("time.driftTitle"),
                string.Format(
                    Strings.Get(ahead ? "time.driftAhead" : "time.driftBehind"),
                    offset.Duration().ToString(@"hh\:mm\:ss")),
                ToolTipIcon.Warning);
        }
        catch (Exception ex)
        {
            // A clock check must never be able to stop the app starting.
            Core.Diagnostics.Log.Warn($"Time sync: skipped ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    /// <summary>
    /// Shows the retake countdown and reports whether the player let it run.
    ///
    /// <para>Modal so the flow genuinely waits, but the window never takes focus — the player is about
    /// to alt-tab into the game, and stealing focus at that exact moment would fight them.</para>
    /// </summary>
    /// <param name="whatToOpen">The screen to name in the prompt, or null for a generic one.</param>
    /// <returns>False if the player cancelled, in which case nothing should be captured.</returns>
    private static bool WaitForRetake(string? whatToOpen)
    {
        using var countdown = new RetakeCountdown(whatToOpen);

        return countdown.ShowDialog() == DialogResult.OK;
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

        // menu.capture, menu.countdown, menu.settings and menu.exit were translated into all nine
        // languages and the menu asked for none of them, so the tray stayed English whatever the
        // player picked. Same omission the ask dialog had.
        var capture = new ToolStripMenuItem(Strings.Get("menu.capture"));
        capture.Click += async (_, _) => await CaptureAndAskAsync();
        capture.Font = new Font(menu.Font, FontStyle.Bold);

        var countdown = new ToolStripMenuItem(Strings.Get("menu.countdown")) { CheckOnClick = true };
        countdown.Click += (_, _) =>
        {
            var current = _store.Load();
            _store.Save(current with { Overlay = current.Overlay with { ShowBossCountdown = countdown.Checked } });
            _bossTimer.Apply();
        };

        var lockOverlay = new ToolStripMenuItem(Strings.Get("menu.lockCountdown")) { CheckOnClick = true };
        lockOverlay.Click += (_, _) => _bossTimer.SetLocked(lockOverlay.Checked);

        // Reflect stored state each time the menu opens, so it stays in step with the settings page.
        menu.Opening += (_, _) =>
        {
            var overlay = _store.Load().Overlay;
            countdown.Checked = overlay.ShowBossCountdown;
            lockOverlay.Checked = overlay.CountdownLocked;
            lockOverlay.Enabled = overlay.ShowBossCountdown;
        };

        var settings = new ToolStripMenuItem(Strings.Get("menu.settings"));
        settings.Click += (_, _) => ShowSettings();

        var exit = new ToolStripMenuItem(Strings.Get("menu.exit"));
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
            if (_openResult is { } answer)
            {
                _captureQueued = true;

                // Focused as well as announced. The balloon says to close a window, so that window had
                // better be the one in front. It is already TopMost from its constructor, which is what
                // gets it above the game; this is what puts the keyboard in it.
                answer.Activate();

                ShowBalloon(Strings.Get("busy.answer.title"), Strings.Get("busy.answer"));
            }
            else if (_openAsk is { } asking)
            {
                // ADD A SCREEN. The hotkey is the whole point of the multi-screen queue: the screens the
                // advice needs cannot be open at once, so the player alt-tabs to the game, opens the next
                // one, and presses the same key again.
                //
                // It has to close the dialog rather than capture in place, for the same reason the first
                // capture happens before the dialog opens: the capture is a region grab of the game's
                // bounds, so an open dialog ends up in the picture. Setting DialogResult closes a modal
                // form, and the capture loop reopens it with the new screen appended.
                //
                // Same result the Add button produces, so there is one code path and not two.
                Core.Diagnostics.Log.Info(
                    "Hotkey: pressed with the ask window open — adding a screen. "
                    + "(If this line never appears while that window is up, WM_HOTKEY is not reaching "
                    + "HotkeyHost during the modal loop and the Add button is the only way in.)");

                asking.DialogResult = AskWindow.AddAnother;
            }
            else
            {
                // Mid-request. Telling them to close something would be wrong advice: nothing is in the
                // way, the answer simply has not arrived.
                ShowBalloon(Strings.Get("busy.working.title"), Strings.Get("busy.working"));
            }

            return;
        }

        _busy = true;

        try
        {
            // OUTER LOOP: one pass per question. A second pass happens only when the hotkey was pressed while
            // the answer window was open, which is the player asking for a fresh capture and a fresh question.
            while (true)
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

                // NO BUILD GATE HERE ANY MORE, deliberately. This used to refuse the capture outright
                // when no questlog URL was configured, on the reasoning that advice is measured against a
                // target and without one there is nothing to compare to.
                //
                // That reasoning was wrong about its own product. Most of what makes the advice good is
                // visible on the screen and needs no target at all: empty artifact slots, unfilled rune
                // sockets, a set one piece from a threshold, a negative boss stat, and a stat
                // redistribution that costs nothing. The gate withheld all of it behind a chore, and the
                // player most likely to hit it is the one who has not yet found a build to copy — exactly
                // the player with the most to gain.
                //
                // What genuinely needs a build is the PvE/PvP axis, and the prompt now handles its absence
                // by asking rather than assuming. See DescribeNoTarget in TlSystemPrompt.
                // RETAKE LOOP. The capture has to happen with no Loadstar window in the way, so a retake
                // cannot be done from inside the dialog — the dialog closes, the player is given a moment to
                // bring the game forward and open the right screen, and the capture runs again.
                //
                // Worth the loop because the hotkey fires on whatever happens to be on screen, and a capture
                // of the open world answers almost nothing. Before this, that cost the player the entire
                // interaction: cancel, navigate, find the hotkey, retype the question.
                // THE QUEUE, not a frame. Up to four screens travel with one question; see
                // PendingCaptures for why four, and why the oldest goes rather than the newest bouncing.
                var shots = new PendingCaptures();
                string question;
                string? carried = null;
                var appending = false;

                while (true)
                {
                    var result = await _gated.CaptureAsync(
                        new CaptureRequest
                        {
                            Target = settings.Capture.ToWindowTarget(Game.DefaultProcessName, Game.DefaultWindowTitleMatch),
                            Region = Game.FullWindow,
                            PrivacyMasks = Game.PrivacyMasks,
                            Label = "game window",
                            Timeout = TimeSpan.FromSeconds(8),
                        },
                        CancellationToken.None);

                    if (!result.Success)
                    {
                        ShowBalloon($"Capture {result.Status}", result.Detail ?? "Unknown failure.", ToolTipIcon.Warning);
                        return;
                    }

                    // Append or replace. Retake means the screenshot was of the wrong screen, and keeping
                    // it would send the wrong screen alongside the right one.
                    if (appending)
                    {
                        shots.Add(result.Frame);
                    }
                    else
                    {
                        shots.Replace(result.Frame);
                    }

                    appending = false;

                    Core.Diagnostics.Log.Info(
                        $"Ask: {shots.Count} screen(s) queued"
                        + (shots.IsFull ? ", at the maximum — the next capture replaces the oldest." : "."));

                    using var ask = new AskWindow(
                        shots.Frames,
                        result.Frame.WindowTitle,
                        Hotkey.TryParse(settings.Overlay.CaptureHotkey)?.Display ?? settings.Overlay.CaptureHotkey,
                        _recentQuestions,
                        carried);

                    _openAsk = ask;

                    DialogResult choice;

                    try
                    {
                        choice = ask.ShowDialog();
                    }
                    finally
                    {
                        _openAsk = null;
                    }

                    // Whatever the player deleted in the strip is gone before anything else happens, so a
                    // rejected screen cannot survive a retake or an add.
                    shots.Keep(ask.Kept);

                    if (choice is DialogResult.Retry or AskWindow.AddAnother)
                    {
                        // Carry the typed question across, so fixing the screenshot does not cost the words.
                        carried = ask.Question;
                        appending = choice == AskWindow.AddAnother;

                        // Cancelling the countdown abandons the whole interaction rather than firing a
                        // capture nobody is ready for.
                        if (!WaitForRetake(whatToOpen: null))
                        {
                            return;
                        }

                        continue;
                    }

                    if (choice != DialogResult.OK)
                    {
                        return;
                    }

                    question = ask.Question;
                    break;
                }

                if (!string.IsNullOrWhiteSpace(question))
                {
                    _recentQuestions.Remove(question);
                    _recentQuestions.Insert(0, question);
                }

                // A retake from the RESULT window captures again and re-asks with the SAME question.
                //
                // It APPENDS rather than replaces, which is the point of the whole feature: the model says
                // "open the runes screen", and what it gets back should be the runes screen AND the
                // character sheet it was already looking at, not the runes screen alone. Replacing was why
                // a chain of "now open X" questions could never be answered as one.
                while (await AnalyseAsync(shots.Frames, question, settings))
                {
                    if (!WaitForRetake(whatToOpen: null))
                    {
                        return;
                    }

                    var again = await _gated.CaptureAsync(
                        new CaptureRequest
                        {
                            Target = settings.Capture.ToWindowTarget(Game.DefaultProcessName, Game.DefaultWindowTitleMatch),
                            Region = Game.FullWindow,
                            PrivacyMasks = Game.PrivacyMasks,
                            Label = "game window",
                            Timeout = TimeSpan.FromSeconds(8),
                        },
                        CancellationToken.None);

                    if (!again.Success)
                    {
                        ShowBalloon($"Capture {again.Status}", again.Detail ?? "Unknown failure.", ToolTipIcon.Warning);
                        return;
                    }

                    shots.Add(again.Frame);
                }

                // A hotkey press landed while the answer was open. Restart from the capture rather than
                // reusing the question: the retake loop above exists for "same question, better screenshot",
                // and reaching for the hotkey means the player wants to ask something else.
                if (_captureQueued)
                {
                    _captureQueued = false;

                    if (!WaitForRetake(whatToOpen: null))
                    {
                        return;
                    }

                    continue;
                }

                return;
            }
        }
        catch (Exception ex)
        {
            ReportError("Capture failed", ex);
        }
        finally
        {
            _busy = false;

            // Dropped rather than honoured if the flow ended some other way — an error, a cancelled
            // countdown. A capture nobody is expecting is worse than one that did not happen.
            _captureQueued = false;
        }
    }

    /// <returns>True when the player asked to retake the screenshot and try the same question again.</returns>
    private async Task<bool> AnalyseAsync(
        IReadOnlyList<CapturedFrame> frames, string question, LoadstarSettings settings)
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
            return false;
        }

        // Name the provider: this is the moment a screenshot leaves the machine, and which third
        // party receives it is exactly what the user should be told.
        ShowBalloon("Analysing…", $"Sending one screenshot to {providerInfo.DisplayName}.");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // A BUILD IS OPTIONAL. It used to be required, and the requirement was in the wrong place:
        // most of what makes the advice good — empty artifact slots, unfilled rune sockets, a set one
        // piece from a threshold, a free stat reallocation — is visible on screen and needs no target
        // at all. Demanding a questlog URL first put a chore in front of the first useful answer.
        //
        // When one is configured it still wins, because it states the player's intended axis and role,
        // which nothing on the screen reveals.
        TargetBuild? target = null;
        IReadOnlyList<string> characterTags = [];

        if (!string.IsNullOrWhiteSpace(settings.Game.BuildUrl))
        {
            // Stage-by-stage, because "no answer appeared" spans five things that can fail and the log
            // is only useful if it says which one did.
            Core.Diagnostics.Log.Info($"Analyse: fetching build {settings.Game.BuildUrl}");

            var character = await new QuestlogClient(http)
                .GetCharacterAsync(settings.Game.BuildUrl, CancellationToken.None);

            if (character is null || character.Builds.Count == 0)
            {
                // Warn, then continue without it. A typo in the URL should cost the player the build's
                // contribution, not the whole answer.
                Core.Diagnostics.Log.Warn($"Analyse: questlog returned no builds for {settings.Game.BuildUrl}");
                ShowBalloon(
                    "Build not found",
                    $"questlog returned nothing for \"{settings.Game.BuildUrl}\". Continuing without it.",
                    ToolTipIcon.Warning);
            }
            else
            {
                target = character.Builds[0];
                characterTags = character.Tags;
            }
        }

        var allocated = target is null
            ? new Dictionary<TlStat, int>()
            : TlStats.MapAllocated(target.Attributes);

        var derived = target is null ? null : await ComputeTargetsAsync(target, http);

        // Cached on disk for a month, so this is a file read on all but the first launch after a patch.
        // Only worth fetching when there is a build whose items it would resolve.
        var catalog = target is null ? null : await LoadCatalogAsync(http);

        // Candidates for the class the player is playing, so the model can offer a target instead of
        // demanding one. Only worth fetching when nothing is pinned, and never worth failing over:
        // no candidates means the offer is skipped, not that the answer is lost.
        var candidates = target is null
            ? await FindCandidateBuildsAsync(settings, http)
            : [];

        Core.Diagnostics.Log.Info(
            $"Analyse: build \"{target?.Name ?? "(none pinned)"}\", {allocated.Count} allocated stats, "
            + $"targets {(derived is null ? "unavailable" : "computed")}, "
            + $"{candidates.Count} candidate build(s). "
            + $"Sending {frames.Count} screen(s), {frames.Sum(f => f.Png.Length) / 1024}KB, "
            + $"to {providerInfo.DisplayName} "
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
                    characterTags,
                    AppLanguages.EnglishName(settings.Language),
                    derived,
                    candidates,
                    catalog),
                UserPrompt = BuildUserPrompt(question, allocated, frames.Count),
                // Every queued screen, oldest first, each labelled with its position so the model can refer
                // to one of them. Without the labels a multi-image request is four anonymous pictures and
                // "the second screenshot" means nothing.
                Images = [.. frames.Select((f, i) => new CapturedImage
                {
                    Png = f.Png,
                    Label = frames.Count == 1 ? f.Label : $"screen {i + 1} of {frames.Count}",
                })],
                // 3000 was too tight and it failed in the field. Reasoning tokens are billed and
                // budgeted as OUTPUT on every current model, so this ceiling is shared between the
                // thinking and the answer — and a reply that thinks hard then gets cut off mid-JSON is
                // indistinguishable, downstream, from a model that cannot follow the output contract.
                //
                // The answer itself is small (a headline, a few steps, some costs). What varies is the
                // reasoning, and non-Latin replies make it worse: Cyrillic runs roughly two to three
                // tokens per character, so the same answer in Russian costs several times an English
                // one. Sized for the worst of those rather than the average.
                MaxOutputTokens = 8000,
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

            return false;
        }

        var advice = AdviceParser.Parse(response.Text, DateTimeOffset.Now, response.Usage);
        var observed = TlObservationParser.Parse(response.Text);

        RememberWeapons(response.Text);

        // The arithmetic stays ours. The model reads the numbers off the screen; StatPlanner prices
        // the move, because a correct recommendation with its cost omitted is the failure this
        // project exists to correct.
        var plan = allocated.Count > 0 && observed.Count > 0
            ? StatPlanner.Plan(observed, allocated)
            : null;

        using var window = new ResultWindow(advice, plan, question);

        // Visible to the hotkey handler for exactly as long as it is on screen. Cleared in a finally so a
        // throw from the dialog cannot leave a stale reference behind — the next hotkey press would then
        // tell the player to close a window that is not there.
        _openResult = window;

        try
        {
            // Retry means the player took the "open that screen and try again" advice. Reported up so the
            // capture loop can run once more with the question already typed, instead of making them start
            // from the hotkey.
            return window.ShowDialog() == DialogResult.Retry;
        }
        finally
        {
            _openResult = null;
        }
    }

    /// <param name="screens">
    /// How many screenshots are attached. Stated per turn rather than left to the system prompt, because
    /// it varies per request and the system prompt is the cached, byte-stable part — putting a number
    /// that changes into it would invalidate the cache on every question.
    /// </param>
    private static string BuildUserPrompt(
        string question, IReadOnlyDictionary<TlStat, int> allocated, int screens)
    {
        var spread = allocated.Count > 0
            ? string.Join(", ", allocated.Select(a => $"{a.Key} {a.Value}"))
            : "(this loadout specifies none)";

        var asked = string.IsNullOrWhiteSpace(question)
            ? "The player asked nothing specific, so rank the highest-value next actions."
            : $"The player asks: \"{question}\"\n\nAnswer THAT question specifically.";

        // Saying how many there are, and that they are probably different panels, stops the common
        // failure of reading only the first image and answering from it.
        var attached = screens == 1
            ? "Here is the player's current screen."
            : $"Here are {screens} screens the player captured, labelled in capture order. They are "
              + "probably DIFFERENT panels, not the same one twice. Read all of them.";

        return $"""
            {attached}

            {asked}

            Identify which screen each one is; nobody has told you.

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
    /// <summary>
    /// Builds the community is actively liking for the class the player is playing, so a target can be
    /// offered rather than demanded.
    ///
    /// <para>Needs the weapons, which come from a previous reply — so the very first capture has none
    /// and returns empty. That is correct rather than a gap: nothing can be recommended for a class
    /// nobody has identified yet, and the alternative is guessing a class from nothing.</para>
    ///
    /// <para>Never throws and never blocks the answer. No candidates means the offer is skipped; the
    /// advice the player actually asked for does not depend on it.</para>
    /// </summary>
    private static async Task<IReadOnlyList<BuildCandidate>> FindCandidateBuildsAsync(
        LoadstarSettings settings,
        HttpClient http)
    {
        var weapons = settings.Game.LastWeapons;

        // FETCHED WHENEVER NO BUILD IS PINNED, which is a deliberate change. It used to stop once the
        // one-time offer had been shown, on the reasoning that a tool which asks the same setup question
        // every capture is worse than one that never asks.
        //
        // That reasoning was about NAGGING and it is still right — the prompt still says to offer once and
        // then drop it. But it was suppressing the DATA as well as the offer, so a player who had dismissed
        // the prompt once could never afterwards ask "what should I be building towards" and get an answer.
        // Suggesting builds on request is the whole point of having no target be acceptable.
        if (weapons.Count != 2)
        {
            return [];
        }

        try
        {
            var candidates = await new QuestlogClient(http)
                .FindPopularBuildsAsync(weapons[0], weapons[1], CancellationToken.None);

            Core.Diagnostics.Log.Info(
                $"Candidates: {candidates.Count} for {TlClasses.Describe(weapons[0], weapons[1])}.");

            return candidates;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Core.Diagnostics.Log.Warn($"Candidates: lookup failed ({ex.GetType().Name}). Skipping the offer.");
            return [];
        }
    }

    /// <summary>
    /// Stores the weapons the model reported, weighing how it came by them.
    ///
    /// <para>This is where "detection must be rock solid" is actually enforced, because it is the last
    /// point before a guess becomes a stored fact that shapes every later recommendation. The rules:</para>
    ///
    /// <list type="bullet">
    /// <item><b>A player's own confirmation is never overwritten by a model read.</b> They know what
    /// they equipped; we are looking at a screenshot.</item>
    /// <item><b>A text read is trusted immediately</b> — a weapon tooltip or the mastery screen names
    /// the type in words, and text is what this model is good at.</item>
    /// <item><b>An icon read has to happen twice</b> and agree. One is a guess; two independent
    /// captures landing on the same pair is evidence. Until then it is stored unconfirmed, which is
    /// enough to ask the player and not enough to drive advice silently.</item>
    /// </list>
    /// </summary>
    private void RememberWeapons(string responseText)
    {
        var reading = TlObservationParser.ParseWeapons(responseText);

        if (reading is null)
        {
            // Not a character sheet, or the model was not confident enough to report. Keeping the
            // previous pair is right: weapons rarely change, and a blank would lose the class.
            return;
        }

        var settings = _store.Load();
        var game = settings.Game;

        // The player's own answer outranks anything read off a screenshot. Only a genuine change of
        // weapons should move it, and that arrives as a text read rather than an icon guess.
        if (game.WeaponsConfirmed && !reading.SamePairAs(game.LastWeapons) && !reading.IsTextRead)
        {
            Core.Diagnostics.Log.Info(
                $"Weapons: ignoring an icon read of {reading.ClassName} — "
                + $"{TlClasses.Name(game.LastWeapons)} is confirmed by the player.");
            return;
        }

        // A text read is good on its own. An icon read needs a second, agreeing sighting.
        var confirmed = reading.IsTextRead
            || (reading.SamePairAs(game.LastWeapons) && !game.WeaponsConfirmed);

        if (reading.SamePairAs(game.LastWeapons) && game.WeaponsConfirmed == confirmed)
        {
            return;
        }

        Core.Diagnostics.Log.Info(
            $"Weapons: {reading.ClassName} from {reading.Source ?? "an unstated source"} "
            + $"({(confirmed ? "confirmed" : "unconfirmed, awaiting corroboration")}).");

        _store.Save(settings with
        {
            Game = game with
            {
                LastWeapons = reading.Weapons,
                WeaponsConfirmed = confirmed,
            },
        });
    }

    /// <summary>
    /// The equipment catalogue, so the build's item ids become names.
    ///
    /// <para>Held for the process lifetime once loaded. It is 10.4MB of static per-patch data, and the
    /// alternative to resolving it is a prompt that shows the player thirteen lines of
    /// <c>belt_aa_S1_003</c> and a model instructed not to guess what they mean.</para>
    ///
    /// <para>Never blocks advice: a failure returns null and the ids stay unresolved, which is exactly
    /// the behaviour that existed before this.</para>
    /// </summary>
    private async Task<EquipmentCatalog?> LoadCatalogAsync(HttpClient http)
    {
        try
        {
            _catalog ??= await new QuestlogClient(http)
                .GetEquipmentCatalogAsync(_store.Directory, CancellationToken.None);

            return _catalog;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
            or InvalidOperationException or ArgumentException)
        {
            // Belt and braces: GetEquipmentCatalogAsync already swallows these, but it is the one call in
            // the advice path whose failure must never surface, and a catch list is cheaper than trusting
            // that a method two projects away keeps its contract.
            Loadstar.Core.Diagnostics.Log.Warn($"Equipment catalogue not loaded: {ex.GetType().Name}.");
            return null;
        }
    }

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

        // THE REPLY ITSELF, when the failure was in reading it. AdviceParseException has carried the
        // raw text since it was written, and nothing ever logged it — so a parse failure recorded a
        // stack trace saying the reply "contained no JSON object" and left no way to see what the reply
        // actually was. That is the one piece of evidence needed to tell a truncated answer from a
        // model ignoring the output contract, and they have opposite fixes.
        //
        // Truncated to keep a 1MB rotating log useful; the head is where the shape is visible anyway.
        if (ex is Core.Ai.AdviceParseException { ResponseText: { Length: > 0 } reply })
        {
            const int limit = 2000;

            Core.Diagnostics.Log.Warn(
                $"Unparseable reply, {reply.Length} chars:{Environment.NewLine}"
                + reply[..Math.Min(limit, reply.Length)]
                + (reply.Length > limit ? $"{Environment.NewLine}[...{reply.Length - limit} more]" : string.Empty));
        }

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
