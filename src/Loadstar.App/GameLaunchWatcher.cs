using System.Diagnostics;

namespace Loadstar.App;

/// <summary>
/// Watches for the configured game process starting, so Loadstar can remind the user of its hotkey
/// the way an overlay does when a game launches.
///
/// <para>Polls <see cref="Process.GetProcessesByName(string)"/>. That is a read-only shell query —
/// no handle to the game is opened, nothing is injected, and nothing is read out of it — so it sits
/// inside the anti-cheat contract. docs/boss-timer.md already relies on the same check for the
/// opposite purpose: not firing alerts when the game is not running.</para>
///
/// <para>Fires only on the <b>transition</b> from absent to present. Polling state and notifying on
/// every tick would produce a reminder every few seconds for as long as the game is open, which is
/// how a helpful notification becomes one the user permanently mutes.</para>
/// </summary>
internal sealed class GameLaunchWatcher : IDisposable
{
    /// <summary>
    /// Five seconds. Fast enough that the reminder arrives while the game is still loading, slow
    /// enough to be invisible — this runs for the whole session.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly System.Windows.Forms.Timer _timer;
    private readonly Func<string?> _processName;
    private readonly Action<string> _onLaunched;

    private bool _wasRunning;
    private string? _watching;

    public GameLaunchWatcher(Func<string?> processName, Action<string> onLaunched)
    {
        _processName = processName ?? throw new ArgumentNullException(nameof(processName));
        _onLaunched = onLaunched ?? throw new ArgumentNullException(nameof(onLaunched));

        _timer = new System.Windows.Forms.Timer { Interval = (int)PollInterval.TotalMilliseconds };
        _timer.Tick += (_, _) => Poll();
        _timer.Start();

        // Establish the baseline immediately. Without this, starting Loadstar while the game is
        // already open would read as a launch and fire a reminder the user did not trigger.
        _wasRunning = IsRunning(_processName());
        _watching = _processName();
    }

    private void Poll()
    {
        var name = _processName();

        if (string.IsNullOrWhiteSpace(name))
        {
            _wasRunning = false;
            return;
        }

        // Changing the configured target resets the baseline rather than reporting a launch for a
        // process that may have been running all along.
        if (!string.Equals(name, _watching, StringComparison.OrdinalIgnoreCase))
        {
            _watching = name;
            _wasRunning = IsRunning(name);
            return;
        }

        var running = IsRunning(name);

        if (running && !_wasRunning)
        {
            _onLaunched(name);
        }

        _wasRunning = running;
    }

    private static bool IsRunning(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        try
        {
            var processes = Process.GetProcessesByName(processName);

            foreach (var process in processes)
            {
                process.Dispose();
            }

            return processes.Length > 0;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
