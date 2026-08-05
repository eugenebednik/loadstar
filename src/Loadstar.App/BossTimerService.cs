using Loadstar.Core.Configuration;
using Loadstar.Games.ThroneAndLiberty;

namespace Loadstar.App;

/// <summary>
/// Drives the boss countdown: recomputes spawns, fires pre-spawn alerts, and owns the overlay.
///
/// <para>Alerts fire at the configured offsets and each one fires <b>once per spawn</b>. Without
/// that latch a one-second tick would produce an alert every second for the whole minute, which is
/// how a useful notification becomes one the user disables.</para>
/// </summary>
internal sealed class BossTimerService : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Func<LoadstarSettings> _settings;
    private readonly Action<LoadstarSettings> _save;
    private readonly Action<string, string> _notify;
    private readonly BossSchedule _schedule;

    /// <summary>Alerts already fired, keyed by spawn instant and offset, so each fires once.</summary>
    private readonly HashSet<string> _fired = [];

    private BossOverlay? _overlay;

    public BossTimerService(
        Func<LoadstarSettings> settings,
        Action<LoadstarSettings> save,
        Action<string, string> notify)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _save = save ?? throw new ArgumentNullException(nameof(save));
        _notify = notify ?? throw new ArgumentNullException(nameof(notify));
        _schedule = BossSchedule.LoadBundled();

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        Apply();
    }

    public BossSchedule Schedule => _schedule;

    /// <summary>The next spawns for the configured server, or empty when it is not set up yet.</summary>
    public IReadOnlyList<BossSpawn> NextSpawns()
    {
        var settings = _settings();

        // The countdown stays off until a server is chosen. Showing Americas times to a European
        // player would be worse than showing nothing — they would be wrong by hours and look right.
        if (string.IsNullOrWhiteSpace(settings.Game.ServerName))
        {
            return [];
        }

        var zone = ResolveTimeZone(settings.Game.ServerTimeZone);

        return _schedule.NextSpawns(DateTimeOffset.Now, settings.Game.Region, zone, count: 3);
    }

    /// <summary>Shows or hides the overlay to match settings, and repositions it.</summary>
    public void Apply()
    {
        var settings = _settings();

        if (settings.Overlay.ShowBossCountdown)
        {
            if (_overlay is null or { IsDisposed: true })
            {
                _overlay = new BossOverlay(
                    NextSpawns,
                    new Point((int)settings.Overlay.CountdownLeft, (int)settings.Overlay.CountdownTop),
                    settings.Overlay.Opacity,
                    settings.Overlay.CountdownLocked,
                    SavePosition);
            }
            else
            {
                // Dragging already moved it; only push a position from settings when the window is
                // new, or a drag would be undone the next time anything calls Apply.
                _overlay.Locked = settings.Overlay.CountdownLocked;
            }

            _overlay.Show();
        }
        else
        {
            _overlay?.Hide();
        }
    }

    /// <summary>Persists a dragged position, so it survives a restart.</summary>
    private void SavePosition(Point location)
    {
        var settings = _settings();

        _save(settings with
        {
            Overlay = settings.Overlay with
            {
                CountdownLeft = location.X,
                CountdownTop = location.Y,
            },
        });
    }

    /// <summary>Toggles click-through and remembers the choice.</summary>
    public void SetLocked(bool locked)
    {
        var settings = _settings();
        _save(settings with { Overlay = settings.Overlay with { CountdownLocked = locked } });

        if (_overlay is { IsDisposed: false })
        {
            _overlay.Locked = locked;
        }
    }

    private void Tick()
    {
        var settings = _settings();

        if (!settings.Game.BossAlertsEnabled || settings.Game.BossAlertMinutes.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.Now;

        foreach (var spawn in NextSpawns())
        {
            var remaining = spawn.SpawnsAt - now;

            foreach (var offset in settings.Game.BossAlertMinutes)
            {
                var window = TimeSpan.FromMinutes(offset);

                // A one-second band around the offset. Tighter would let a dropped tick miss the
                // alert entirely; wider would fire twice.
                if (remaining <= window && remaining > window - TimeSpan.FromSeconds(1.5))
                {
                    var key = $"{spawn.SpawnsAt:O}|{offset}";

                    if (_fired.Add(key))
                    {
                        _notify(
                            $"{spawn.DisplayName} in {offset} minute{(offset == 1 ? string.Empty : "s")}",
                            $"Spawns at {spawn.SpawnsAt.ToLocalTime():HH:mm} your time.");
                    }
                }
            }
        }

        // Keep the latch from growing without bound over a long session.
        if (_fired.Count > 256)
        {
            _fired.Clear();
        }
    }

    /// <summary>
    /// Resolves an IANA id, falling back to the local zone. .NET 8 accepts IANA ids on Windows, but
    /// a hand-edited settings file can still carry something unresolvable.
    /// </summary>
    public static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        _overlay?.Dispose();
    }
}
