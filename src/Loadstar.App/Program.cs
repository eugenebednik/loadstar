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
}
