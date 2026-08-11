using System.Diagnostics;

namespace Loadstar.App;

internal static class Program
{
    /// <summary>
    /// Single-instance guard. Two copies would both register the same hotkey, and the second
    /// registration silently fails — leaving the user with a running app whose shortcut does nothing.
    /// </summary>
    private static Mutex? _instance;

    /// <summary>
    /// Tells a freshly launched copy to wait for the copy that spawned it to exit. See
    /// <see cref="Restart"/> — the argument carries the outgoing process id.
    /// </summary>
    private const string AwaitExitFlag = "--await-exit";

    [STAThread]
    private static void Main(string[] args)
    {
        // FIRST, ahead of every other switch, and that ordering is the point: this runs during an MSI
        // uninstall with no user present, so it must not touch the settings store, must not initialise the
        // log in a directory it is about to delete, and must not construct anything that could show a
        // dialog. Every branch below does at least one of those.
        if (args.Contains(UninstallCleanup.Flag, StringComparer.OrdinalIgnoreCase))
        {
            UninstallCleanup.Run();

            return;
        }

        // --write-icon regenerates installer/Loadstar.ico from AppIcon, which is what keeps the icon Windows
        // shows in Explorer and the Start Menu identical to the one the app draws for its tray and windows.
        // Run it after changing AppIcon; the .ico is committed because the csproj needs it at build time, and
        // a build cannot depend on running the thing it is building.
        if (args.Contains("--write-icon", StringComparer.OrdinalIgnoreCase))
        {
            var target = args.SkipWhile(a => !a.Equals("--write-icon", StringComparison.OrdinalIgnoreCase))
                .Skip(1)
                .FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
                ?? "Loadstar.ico";

            // Every size Windows asks for: 16 in the tray and small lists, 32 on the desktop, 48 in the Start
            // Menu, 256 for Explorer's extra-large view. Omitting one makes Windows scale a neighbour.
            File.WriteAllBytes(target, AppIcon.BuildIcoFile(16, 20, 24, 32, 48, 64, 128, 256));

            Console.WriteLine($"wrote {target} ({new FileInfo(target).Length:n0} bytes)");

            // The installer's own artwork comes from the same mark, written beside the .ico. WixUI needs BMP
            // and fixed dimensions, so these are not something a designer hands over once — they are
            // regenerated whenever the icon changes, which is the point of doing it here.
            var directory = Path.GetDirectoryName(Path.GetFullPath(target)) ?? ".";
            var (banner, dialog) = AppIcon.RenderInstallerArt();

            using (banner)
            using (dialog)
            {
                var bannerPath = Path.Combine(directory, "banner.bmp");
                var dialogPath = Path.Combine(directory, "dialog.bmp");

                banner.Save(bannerPath, System.Drawing.Imaging.ImageFormat.Bmp);
                dialog.Save(dialogPath, System.Drawing.Imaging.ImageFormat.Bmp);

                Console.WriteLine($"wrote {bannerPath} (493x58)");
                Console.WriteLine($"wrote {dialogPath} (493x312)");
            }

            return;
        }

        // --gear identifies equipment on one or more captures using ONLY the embedded index, so the shipped
        // path is exercised: no catalogue fetch, no icon downloads, nothing but bytes inside the assembly.
        // That is the configuration a user on a fresh install actually gets.
        if (args.Contains("--gear", StringComparer.OrdinalIgnoreCase))
        {
            var store2 = new Core.Configuration.SettingsStore();
            Core.Diagnostics.Log.Initialize(store2.Directory);

            Console.WriteLine($"Embedded gear index: {Games.ThroneAndLiberty.TlGearIndex.All.Count} items.");

            foreach (var path in args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)))
            {
                try
                {
                    var image = Capture.Windows.ImageDecoder
                        .DecodeAsync(File.ReadAllBytes(path)).GetAwaiter().GetResult();
                    var verdict = Games.ThroneAndLiberty.TlGearIndex.Identify(image);

                    Console.WriteLine();
                    Console.WriteLine($"{Path.GetFileName(path)} ({image.Width}x{image.Height})");

                    if (verdict is null)
                    {
                        Console.WriteLine("  no set identified (not a character sheet, or genuinely unclear)");

                        continue;
                    }

                    Console.WriteLine($"  SET {verdict.SetName}  mean {verdict.MeanSimilarity:0.000}"
                        + $"  runner-up {verdict.RunnerUpSimilarity:0.000}");

                    foreach (var slot in verdict.Slots.OrderBy(x => x.SlotName, StringComparer.Ordinal))
                    {
                        Console.WriteLine($"    {slot.SlotName,-6} {slot.ItemName ?? "(none in set)",-32}"
                            + $" {slot.Similarity:0.000} {(slot.Confident ? "confirmed" : "assigned")}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  {Path.GetFileName(path)}: {ex.GetType().Name} {ex.Message}");
                }
            }

            return;
        }

        // --check-update runs the update check and prints what it decided, without any UI. A feature that
        // talks to the network and then executes an installer needs a way to be exercised repeatedly; the
        // alternative is waiting on a balloon and a dialog to find out whether a URL is right.
        if (args.Contains("--check-update", StringComparer.OrdinalIgnoreCase))
        {
            var store3 = new Core.Configuration.SettingsStore();
            Core.Diagnostics.Log.Initialize(store3.Directory);

            var language = Core.Configuration.AppLanguages.InstallerCode(store3.Load().Language);

            Console.WriteLine($"Running {UpdateService.CurrentVersion}, asking for '{language}'.");

            var found = new UpdateService()
                .CheckAsync(language, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            if (found is null)
            {
                Console.WriteLine("No update offered. Either up to date, or the check failed — see the log.");
            }
            else
            {
                Console.WriteLine($"Update {found.Version} available: {found.Installer.File} "
                    + $"({found.Installer.Bytes / 1048576:n0} MB, sha256 {found.Installer.Sha256?[..16]}…)");

                // Downloads only when asked twice, because this pulls 60 MB.
                if (args.Contains("--download", StringComparer.OrdinalIgnoreCase))
                {
                    var path = new UpdateService()
                        .DownloadAsync(found, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();

                    Console.WriteLine(path is null
                        ? "Download or verification FAILED; nothing was kept."
                        : $"Downloaded and digest-verified: {path}");
                }
            }

            return;
        }

        // --settings opens the dialog on its own, without the tray. Purely so the window can be
        // opened, inspected and screenshotted without hunting through a tray menu each time.
        if (args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();

            var store = new Core.Configuration.SettingsStore();
            Core.Diagnostics.Log.Initialize(store.Directory);
            Core.Diagnostics.Log.Info("Started in --settings mode.");
            Strings.Use(store.Load().Language);

            // Application.Run, not ShowDialog: a modal dialog shown without a message loop never
            // registers as the process main window, so nothing can find it to capture or automate.
            using var standalone = new SettingsWindow(store, new Core.Configuration.SecretStore(store.Directory));
            Application.Run(standalone);
            return;
        }

        // --slots runs ONLY equipment-slot localisation over one or more images and prints the geometry.
        // Separate from --icon-probe so the resolution question can be answered without rebuilding the
        // whole icon index each time.
        if (args.Contains("--slots", StringComparer.OrdinalIgnoreCase))
        {
            var store0 = new Core.Configuration.SettingsStore();
            Core.Diagnostics.Log.Initialize(store0.Directory);

            foreach (var path in args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)))
            {
                try
                {
                    var image = Capture.Windows.ImageDecoder
                        .DecodeAsync(File.ReadAllBytes(path)).GetAwaiter().GetResult();
                    var found = Core.Capture.EquipmentSlotLocator.Locate(image);
                    var ring = found.Count > 0 ? found[0].Ring.Width : 0;
                    var columns = found.Select(s => s.Ring.X).Distinct().Count();

                    Console.WriteLine(
                        $"{image.Width,5}x{image.Height,-5} slots={found.Count,3}  ring={ring,4}px"
                        + $"  columns={columns}  {Path.GetFileName(path)}");

                    // The rectangles themselves, next to the image, when asked. Descriptor experiments run
                    // outside this codebase and must use the SHIPPING geometry rather than a reimplementation
                    // of it — a second copy of the detector would be measuring the wrong thing.
                    if (args.Contains("--json", StringComparer.OrdinalIgnoreCase))
                    {
                        var rows = found.Select((slot, i) => new
                        {
                            index = i,
                            row = slot.Row,
                            column = slot.Column,
                            name = found.Count == Games.ThroneAndLiberty.TlEquipmentLayout.Order.Count
                                ? Games.ThroneAndLiberty.TlEquipmentLayout.SlotNameForIndex(i)
                                : null,
                            categories = found.Count == Games.ThroneAndLiberty.TlEquipmentLayout.Order.Count
                                ? Games.ThroneAndLiberty.TlEquipmentLayout.CategoriesForIndex(i)
                                : Games.ThroneAndLiberty.TlEquipmentLayout.CategoriesForRow(slot.Row),
                            ring = new { slot.Ring.X, slot.Ring.Y, slot.Ring.Width, slot.Ring.Height },
                            disc = new { slot.Disc.X, slot.Disc.Y, slot.Disc.Width, slot.Disc.Height },
                            artwork = new { slot.Artwork.X, slot.Artwork.Y, slot.Artwork.Width, slot.Artwork.Height },
                        });

                        var json = System.Text.Json.JsonSerializer.Serialize(
                            new { image = new { image.Width, image.Height }, slots = rows },
                            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                        var target = Path.ChangeExtension(path, ".slots.json");

                        File.WriteAllText(target, json);
                        Console.WriteLine($"  wrote {target}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  {Path.GetFileName(path)}: {ex.GetType().Name} {ex.Message}");
                }
            }

            return;
        }

        // --result opens the answer window with a synthetic answer, for the same reason --settings and --ask
        // exist: the real one needs a capture, an API key and a paid round trip, and "ask the user to check
        // it" is a slow way to discover that a link rendered unreadably.
        if (args.Contains("--result", StringComparer.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();

            var store1 = new Core.Configuration.SettingsStore();
            Core.Diagnostics.Log.Initialize(store1.Directory);
            Strings.Use(store1.Load().Language);

            using var result = new ResultWindow(SyntheticAdvice(), plan: null, question: "What should I build?");
            Application.Run(result);

            return;
        }

        // --icon-probe measures whether local icon identification works, against the real catalogue and a
        // real capture. See IconProbe: the thresholds it tests were calibrated on synthetic icons and the
        // code that owns them asks in writing to be re-measured on real ones.
        if (args.Contains("--icon-probe", StringComparer.OrdinalIgnoreCase))
        {
            var store = new Core.Configuration.SettingsStore();
            Core.Diagnostics.Log.Initialize(store.Directory);

            var capture = args.SkipWhile(a => !a.Equals("--icon-probe", StringComparison.OrdinalIgnoreCase))
                .Skip(1)
                .FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

            Environment.ExitCode = IconProbe.RunAsync(capture).GetAwaiter().GetResult();

            return;
        }

        // --ask opens the ask dialog with synthetic screens, for the same reason --settings exists: the
        // multi-screen layout cannot be checked without four captures and a running game, and "ask the
        // user to test it" is a slow way to find out a panel did not render. Takes a count so one, two,
        // three and four screens can each be looked at.
        if (args.FirstOrDefault(a => a.StartsWith("--ask", StringComparison.OrdinalIgnoreCase)) is { } askArg)
        {
            ApplicationConfiguration.Initialize();

            var store = new Core.Configuration.SettingsStore();
            Core.Diagnostics.Log.Initialize(store.Directory);
            Strings.Use(store.Load().Language);

            var count = int.TryParse(askArg.Split('=').ElementAtOrDefault(1), out var n)
                ? Math.Clamp(n, 1, Core.Capture.PendingCaptures.Maximum)
                : Core.Capture.PendingCaptures.Maximum;

            // --offline forces the probe to fail, so the greyed-out Ask button can be seen without
            // unplugging anything.
            var offline = args.Contains("--offline", StringComparer.OrdinalIgnoreCase);

            // --nokey does the same for the missing-API-key gate, and for the same stated reason: this state
            // shipped broken because it could only be reached by genuinely having no key, so nobody ever
            // looked at it. The fake provider name is obviously fake so a screenshot of this cannot be
            // mistaken for the real dialog.
            var noKey = args.Contains("--nokey", StringComparer.OrdinalIgnoreCase)
                ? "Example AI (synthetic)"
                : null;

            using var ask = new AskWindow(
                [.. Enumerable.Range(1, count).Select(SyntheticScreen)],
                "THRONE AND LIBERTY (synthetic)",
                "Ctrl+Alt+S",
                // No fake recents. Recent questions are the player's OWN typed words, so the real app is
                // right to leave them untranslated — but seeding an English one here made the dev harness
                // look like a localisation bug, and it was reported as one.
                [],
                probe: offline ? _ => Task.FromResult(false) : null,
                missingKeyFor: noKey,
                // Reports that the key is now present, so the recovery path — blocked, open Settings, Ask
                // becomes usable — can be walked through without touching a real credential.
                fixKey: noKey is null ? null : () => true);

            Application.Run(ask);
            return;
        }

        // A restart launches the replacement before the outgoing copy has finished shutting down, so
        // the replacement waits here. Both things it needs are still held by the old process: the
        // mutex below, and the global hotkey — and losing the hotkey race is the quiet failure, since
        // the app would start looking perfectly healthy with a shortcut that does nothing.
        WaitForPredecessor(args);

        _instance = new Mutex(initiallyOwned: true, "Loadstar.SingleInstance", out var isFirst);

        if (!isFirst)
        {
            MessageBox.Show(
                "Loadstar is already running — look for it in the system tray.",
                "Loadstar",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        ApplicationConfiguration.Initialize();

        var settings = new Core.Configuration.SettingsStore();

        // Before anything else that could fail. A log started after the first risky call is a log
        // guaranteed to be missing the one entry someone needs.
        Core.Diagnostics.Log.Initialize(settings.Directory);
        Core.Diagnostics.Log.Info(
            $"Loadstar {typeof(Program).Assembly.GetName().Version} starting on {Environment.OSVersion}.");

        // A tray app has no console and no window to print to, so an unhandled exception otherwise
        // surfaces as the bare .NET dialog with no clue where it came from. Log it somewhere the
        // user can find and quote.
        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception);

        // Language before any window is constructed, so nothing is built with the wrong strings.
        Strings.Use(settings.Load().Language);

        using var tray = new TrayApplication();

        // No main form: the app's lifetime is the message loop, ended by the tray's Exit item.
        Application.Run();
    }

    /// <summary>
    /// Blocks until the process named by <see cref="AwaitExitFlag"/> has exited, so the replacement
    /// does not race the copy it is replacing for the mutex or the hotkey.
    ///
    /// <para>Bounded rather than indefinite: if the outgoing process wedges on shutdown, starting
    /// anyway is a better outcome than a replacement that never appears and leaves the user thinking
    /// the app is gone.</para>
    /// </summary>
    private static void WaitForPredecessor(string[] args)
    {
        var index = Array.FindIndex(args, a => a.Equals(AwaitExitFlag, StringComparison.OrdinalIgnoreCase));

        if (index < 0 || index + 1 >= args.Length || !int.TryParse(args[index + 1], out var pid))
        {
            return;
        }

        try
        {
            using var predecessor = Process.GetProcessById(pid);
            predecessor.WaitForExit(milliseconds: 10_000);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Already gone, which is exactly what we were waiting for.
        }
    }

    /// <summary>
    /// Relaunches Loadstar and ends this copy, so a new interface language applies immediately.
    ///
    /// <para>WinForms builds a control's text once, when it is constructed, so changing the culture
    /// afterwards leaves every open window in the old language. Rebuilding them all would mean every
    /// window growing a re-localise path and every future window remembering to have one — a
    /// restart is a fraction of the machinery and cannot be half-done.</para>
    ///
    /// <para>The replacement is told to wait for this process id before it does anything, because it
    /// otherwise races the copy it is replacing; see <see cref="WaitForPredecessor"/>.</para>
    /// </summary>
    /// <returns><c>false</c> if the replacement could not be started, leaving this copy running.</returns>
    public static bool Restart()
    {
        // Null under single-file publish in some hosts, and there is nothing to relaunch without it.
        var executable = Environment.ProcessPath;

        if (string.IsNullOrEmpty(executable))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(executable)
            {
                Arguments = $"{AwaitExitFlag} {Environment.ProcessId}",
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? string.Empty,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }

        Application.Exit();
        return true;
    }

    /// <summary>
    /// Writes the failure next to the settings file and shows the path, so a crash can be reported
    /// with a stack trace rather than "it said unhandled exception".
    /// </summary>
    private static void ReportCrash(Exception? ex)
    {
        if (ex is null)
        {
            return;
        }

        Core.Diagnostics.Log.Error("Unhandled exception", ex);

        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Loadstar",
            "crash.log");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"=== {DateTimeOffset.Now:O} ==={Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception writeFailure) when (writeFailure is IOException or UnauthorizedAccessException)
        {
            // Reporting must not itself crash the reporter.
            path = "(could not be written)";
        }

        MessageBox.Show(
            $"{ex.GetType().Name}: {ex.Message}{Environment.NewLine}{Environment.NewLine}Details written to:{Environment.NewLine}{path}",
            "Loadstar — something went wrong",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    /// <summary>
    /// A stand-in answer for <c>--result</c>: two questlog links that should render as clickable accents, and
    /// one off-site link that must NOT, so both halves of the host rule are visible at a glance.
    /// </summary>
    private static Core.Model.Advice SyntheticAdvice() => new()
    {
        GeneratedAt = DateTimeOffset.UtcNow,
        Headline = "Adopt a recent Seeker build, then fix your stat spread",
        RecognizedScreen = Core.Model.ScreenKind.Character,
        Steps =
        [
            new Core.Model.AdviceStep
            {
                Rank = 1,
                Action = "Pick a target build — bow+wand is played as healer or dps",
                Category = "Build",
                Rationale =
                    "Most-liked and updated this month:\n"
                    + "  PvE healer — https://questlog.gg/throne-and-liberty/en/character-builder/GoldenConquestAndWriter\n"
                    + "  PvP dps    — https://questlog.gg/throne-and-liberty/en/character-builder/InfernalRavenousUnderTheSalvation\n"
                    + "This one is NOT ours and must not be clickable: https://example.com/not-questlog",
            },
            new Core.Model.AdviceStep
            {
                Rank = 2,
                Action = "Move 9 allocated points into Fortitude for the 80 threshold",
                Category = "Stat Points",
                Rationale = "Costs nothing but a reallocation; gains Endurance 60 and Heavy Attack Evasion 60.",
            },
        ],
        MissingInformation = ["Hover a stat for its Base / Equipment / Stellar Journey split."],
        // Exercises the no-target path: several proposals, roles and axes, real slugs.
        SuggestedBuilds =
        [
            new Core.Model.SuggestedBuild
            {
                Name = "T4 Seeker Magic HAE/END (Aelon Bow)",
                Role = "dps",
                Axis = "PvP",
                Url = "https://questlog.gg/throne-and-liberty/en/character-builder/GoldenConquestAndWriter",
                Why = "Most-liked dps Seeker updated this month.",
            },
            new Core.Model.SuggestedBuild
            {
                Name = "Seeker PVE Healer",
                Role = "healer",
                Axis = "PvE",
                Url = "https://questlog.gg/throne-and-liberty/en/character-builder/InfernalRavenousUnderTheSalvation",
                Why = "The alternative role this weapon pair plays.",
            },
        ],
    };

    /// <summary>
    /// A stand-in game screen for <c>--ask</c>: numbered, 16:9, and obviously not a real capture, so a
    /// screenshot of the dialog cannot be mistaken for one taken in game.
    /// </summary>
    private static Core.Capture.CapturedFrame SyntheticScreen(int number)
    {
        using var bitmap = new Bitmap(1920, 1080);
        using var graphics = Graphics.FromImage(bitmap);

        Color[] tints = [Color.FromArgb(40, 60, 90), Color.FromArgb(80, 50, 40), Color.FromArgb(40, 80, 55), Color.FromArgb(70, 45, 80)];

        graphics.Clear(tints[(number - 1) % tints.Length]);

        using var font = new Font("Segoe UI", 220, FontStyle.Bold);
        using var brush = new SolidBrush(Color.FromArgb(210, 235, 245, 255));

        graphics.DrawString(
            number.ToString(),
            font,
            brush,
            new RectangleF(0, 0, 1920, 1080),
            new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });

        using var stream = new MemoryStream();

        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);

        return new Core.Capture.CapturedFrame
        {
            Png = stream.ToArray(),
            Width = 1920,
            Height = 1080,
            CapturedAt = DateTimeOffset.UtcNow,
            WindowTitle = "THRONE AND LIBERTY (synthetic)",
            Label = $"synthetic screen {number}",
        };
    }

}
