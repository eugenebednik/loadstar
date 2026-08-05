using System.Diagnostics;
using Loadstar.Core.Capture;

namespace Loadstar.Capture.Windows;

/// <summary>
/// Finds the window Loadstar should read.
///
/// <para>Uses <see cref="Process"/> rather than <c>EnumWindows</c> / <c>GetWindowText</c>, which
/// keeps three P/Invokes off the audit surface for no loss of function — a game's client window is
/// its main window. Nothing here opens a handle to the game process or reads anything out of it;
/// <see cref="Process.MainWindowTitle"/> is window metadata the shell exposes to everyone.</para>
///
/// <para>Resolution order is deliberate, and it exists because title matching alone once selected
/// Firefox: an exact process match wins, then an exact title, and only then a title substring —
/// which additionally refuses to land on a browser unless the user opted in. See
/// <see cref="WindowTarget"/>.</para>
/// </summary>
public static class GameWindowLocator
{
    public static GameWindow? Find(WindowTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!target.IsConfigured)
        {
            return null;
        }

        var candidates = ListVisibleWindows();

        if (!string.IsNullOrWhiteSpace(target.ProcessName))
        {
            var wanted = WindowTargeting.NormalizeProcessName(target.ProcessName);

            var byProcess = candidates
                .Where(w => w.ProcessName.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (byProcess.Length > 0)
            {
                // Narrow by title too when both are configured — some games run several windows
                // under one process (a launcher alongside the client).
                return string.IsNullOrWhiteSpace(target.TitleMatch)
                    ? byProcess[0]
                    : byProcess.FirstOrDefault(w =>
                        w.Title.Contains(target.TitleMatch, StringComparison.OrdinalIgnoreCase))
                      ?? byProcess[0];
            }

            // A configured process that is not running is a different situation from a bad title,
            // and the caller says so. Falling through to a title match here is what produced the
            // browser capture, so we only do it when the user gave no process at all.
            if (string.IsNullOrWhiteSpace(target.TitleMatch))
            {
                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(target.TitleMatch))
        {
            return null;
        }

        var exact = candidates.FirstOrDefault(w =>
            w.Title.Equals(target.TitleMatch, StringComparison.OrdinalIgnoreCase));

        if (exact is not null)
        {
            return exact;
        }

        return candidates.FirstOrDefault(w =>
            w.Title.Contains(target.TitleMatch, StringComparison.OrdinalIgnoreCase)
            && (target.AllowAnyProcess || !WindowTargeting.CommonlyMismatchedProcesses.Contains(w.ProcessName)));
    }

    /// <summary>
    /// Windows that matched the title but were skipped because they belong to a process that
    /// commonly displays a game's name without being it.
    ///
    /// <para>Reported rather than silently dropped: "I ignored your browser" is useful, and it is
    /// how the user discovers they should configure a process name.</para>
    /// </summary>
    public static IReadOnlyList<GameWindow> RejectedTitleMatches(WindowTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target.AllowAnyProcess || string.IsNullOrWhiteSpace(target.TitleMatch))
        {
            return [];
        }

        return ListVisibleWindows()
            .Where(w => w.Title.Contains(target.TitleMatch, StringComparison.OrdinalIgnoreCase)
                && WindowTargeting.CommonlyMismatchedProcesses.Contains(w.ProcessName))
            .ToArray();
    }

    /// <summary>
    /// Every visible top-level window — the list behind the "pick a running window" picker, and
    /// what we show when a configured target matched nothing.
    /// </summary>
    public static IReadOnlyList<GameWindow> ListVisibleWindows()
    {
        var windows = new List<GameWindow>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero &&
                    !string.IsNullOrWhiteSpace(process.MainWindowTitle))
                {
                    windows.Add(new GameWindow(
                        process.MainWindowHandle,
                        process.MainWindowTitle,
                        process.ProcessName));
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                // Exited between enumeration and inspection, or a protected process. Neither is
                // the game; skip rather than failing the whole scan.
            }
            finally
            {
                process.Dispose();
            }
        }

        return windows.OrderBy(w => w.Title, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

public sealed record GameWindow(IntPtr Handle, string Title, string ProcessName)
{
    public override string ToString() => $"[{ProcessName}] {Title}";
}
