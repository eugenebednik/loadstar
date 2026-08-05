namespace Loadstar.App;

internal static class Program
{
    /// <summary>
    /// Single-instance guard. Two copies would both register the same hotkey, and the second
    /// registration silently fails — leaving the user with a running app whose shortcut does nothing.
    /// </summary>
    private static Mutex? _instance;

    [STAThread]
    private static void Main(string[] args)
    {
        // --settings opens the dialog on its own, without the tray. Purely so the window can be
        // opened, inspected and screenshotted without hunting through a tray menu each time.
        if (args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();

            var store = new Core.Configuration.SettingsStore();
            Strings.Use(store.Load().Language);

            // Application.Run, not ShowDialog: a modal dialog shown without a message loop never
            // registers as the process main window, so nothing can find it to capture or automate.
            using var standalone = new SettingsWindow(store, new Core.Configuration.SecretStore(store.Directory));
            Application.Run(standalone);
            return;
        }

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

        // A tray app has no console and no window to print to, so an unhandled exception otherwise
        // surfaces as the bare .NET dialog with no clue where it came from. Log it somewhere the
        // user can find and quote.
        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception);

        // Language before any window is constructed, so nothing is built with the wrong strings.
        Strings.Use(new Core.Configuration.SettingsStore().Load().Language);

        using var tray = new TrayApplication();

        // No main form: the app's lifetime is the message loop, ended by the tray's Exit item.
        Application.Run();
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
