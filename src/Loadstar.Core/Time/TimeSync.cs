using System.Diagnostics;
using System.Globalization;

using Loadstar.Core.Diagnostics;

namespace Loadstar.Core.Time;

/// <summary>
/// Checks the machine's clock against several independent sources, and corrects countdowns if they agree
/// it is wrong.
///
/// <para><b>Why a countdown cares.</b> Every countdown is <c>spawnInstant - now</c>, so it is only as
/// accurate as the clock. A machine ten minutes fast produces advice ten minutes wrong, and nothing
/// inside the app could notice: the schedule is right, the arithmetic is right, and the answer is wrong.
/// </para>
///
/// <para><b>Why CONSENSUS, and why not a time API — this is the whole story of the class.</b> The first
/// version asked timeapi.io and believed it. It reported this machine as 21 minutes 27 seconds fast, the
/// Windows Time service showed as stopped, and the diagnosis was completely convincing. It was also
/// wrong: challenged to verify, Google, Cloudflare and Microsoft all agreed with the machine to within a
/// second, and Cloudflare's millisecond-precision endpoint put it at 93ms. <b>timeapi.io was the thing
/// that was 21 minutes slow.</b> Shipped, that version would have injected a 21-minute error into a
/// correct clock — the exact failure it existed to prevent.</para>
///
/// <para>So there is no single authority here. Several independent, large, NTP-synced services are asked,
/// the median is taken, and a correction is applied only when enough of them <b>agree</b>. A lone outlier
/// cannot move the clock. That is the same rule the rest of this codebase already runs on: an icon read
/// must be seen twice before it counts, a boss name must appear in the closed vocabulary. One confident
/// source is not evidence.</para>
///
/// <para><b>It never sets the system clock.</b> That needs administrator rights and is a machine-wide
/// change on a game overlay's initiative. The offset is applied internally, so Loadstar is right about
/// time without altering anything outside itself — and if the clock really is wrong, that is worth
/// telling the player, whose machine it is to fix.</para>
///
/// <para><b>Offline is normal, not a failure.</b> No network, too few answers, or sources that disagree,
/// all mean the offset stays zero and everything falls back to system time — the behaviour that existed
/// before this class.</para>
/// </summary>
public static class TimeSync
{
    /// <summary>
    /// Cloudflare's trace endpoint, which reports <c>ts=</c> as a Unix timestamp with milliseconds.
    ///
    /// <para>Preferred as the precise source: the HTTP <c>Date</c> header used for the others has only
    /// one-second resolution, while this measured the local clock to 93 milliseconds.</para>
    /// </summary>
    private const string CloudflareTrace = "https://cloudflare.com/cdn-cgi/trace";

    /// <summary>
    /// Corroborating sources, read from the HTTP <c>Date</c> header.
    ///
    /// <para>Ordinary large services rather than a time API, deliberately. On these a mis-set clock would
    /// be an incident somebody is paged for; a hobby time API is one machine nobody is watching, which is
    /// exactly how this class first acquired a 21-minute error from an authoritative-looking answer. The
    /// <c>Date</c> header is also mandatory on every HTTP response — no key, no quota, one round trip.
    /// </para>
    /// </summary>
    private static readonly string[] DateHeaderSources =
    [
        "https://www.google.com",
        "https://www.microsoft.com",
        "https://www.apple.com",
    ];

    /// <summary>
    /// How many sources must answer before any correction is considered. Two could agree by coincidence
    /// and give no meaningful median; three is the smallest number at which an outlier can be outvoted
    /// rather than averaged in.
    /// </summary>
    private const int MinimumSources = 3;

    /// <summary>
    /// How far apart readings may be and still be believed. All these services are NTP-synced, so real
    /// spread is well under a second — this is a sanity gate, not a precision budget. Wider means the
    /// measurements themselves are suspect, and the safe answer to "I do not know what time it is" is to
    /// change nothing.
    /// </summary>
    private static readonly TimeSpan MaximumSpread = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Below this, the clock counts as correct and no offset is applied. One-second header resolution
    /// plus network jitter makes small readings noise, and correcting noise would only make the countdown
    /// twitch.
    /// </summary>
    private static readonly TimeSpan CorrectionThreshold = TimeSpan.FromSeconds(30);

    /// <summary>Tell the player at this much drift. Below a couple of minutes nobody would notice.</summary>
    public static readonly TimeSpan NoticeableDrift = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Beyond this a reading is a bad measurement, not a very wrong clock. Twenty-five hours exceeds
    /// every real timezone span, so a gap that large means something was misparsed.
    /// </summary>
    private static readonly TimeSpan ImplausibleOffset = TimeSpan.FromHours(25);

    private static readonly object Gate = new();

    // A trusted instant plus a MONOTONIC stopwatch, rather than an offset added to the system clock. If
    // the player corrects their clock mid-session — likely, having just been told it is wrong — a stored
    // offset would then be applied on top of an already-correct clock and make it wrong again by the same
    // amount. Advancing a captured instant with a stopwatch is immune to that.
    private static DateTimeOffset? _syncedAt;
    private static Stopwatch? _elapsed;
    private static TimeSpan _measuredOffset;
    private static int _agreeingSources;

    /// <summary>Whether enough sources agreed that a correction is being applied this launch.</summary>
    public static bool IsSynced
    {
        get { lock (Gate) { return _syncedAt is not null; } }
    }

    /// <summary>
    /// How far the system clock was found to be out: positive means the machine is BEHIND real time,
    /// negative means it is ahead. Zero when unsynced, or when the clock was already accurate.
    /// </summary>
    public static TimeSpan Offset
    {
        get { lock (Gate) { return _measuredOffset; } }
    }

    /// <summary>How many sources answered and agreed. Zero when nothing was reachable.</summary>
    public static int AgreeingSources
    {
        get { lock (Gate) { return _agreeingSources; } }
    }

    /// <summary>True when the clock is out by enough that a countdown would visibly mislead.</summary>
    public static bool IsClockNoticeablyWrong => IsSynced && Offset.Duration() >= NoticeableDrift;

    /// <summary>
    /// The current instant — corrected if the sources agreed the clock is wrong, plain system time
    /// otherwise.
    ///
    /// <para><b>Use this rather than <see cref="DateTimeOffset.Now"/> wherever a countdown or an alert is
    /// computed.</b> It shifts the instant, not the timezone, so rendering a local time is unaffected.
    /// </para>
    /// </summary>
    public static DateTimeOffset Now => UtcNow.ToLocalTime();

    /// <inheritdoc cref="Now"/>
    public static DateTimeOffset UtcNow
    {
        get
        {
            lock (Gate)
            {
                return _syncedAt is { } synced && _elapsed is { } running
                    ? synced + running.Elapsed
                    : DateTimeOffset.UtcNow;
            }
        }
    }

    /// <summary>One source's view of how far the local clock is off.</summary>
    private readonly record struct Reading(string Source, TimeSpan Offset);

    /// <summary>
    /// Measures the clock against the sources, once per launch. Never throws; failure leaves the app on
    /// system time.
    /// </summary>
    public static async Task SynchroniseAsync(HttpClient http, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);

        var readings = new List<Reading>();

        if (await ReadCloudflareAsync(http, cancellationToken).ConfigureAwait(false) is { } precise)
        {
            readings.Add(precise);
        }

        foreach (var source in DateHeaderSources)
        {
            if (await ReadDateHeaderAsync(source, http, cancellationToken).ConfigureAwait(false) is { } reading)
            {
                readings.Add(reading);
            }
        }

        Apply(readings);
    }

    private static void Apply(List<Reading> readings)
    {
        readings.RemoveAll(r => r.Offset.Duration() > ImplausibleOffset);

        if (readings.Count < MinimumSources)
        {
            Log.Info(
                $"Time sync: {readings.Count} source(s) answered, {MinimumSources} needed to agree. "
                + "Using system time.");
            return;
        }

        var offsets = readings.Select(r => r.Offset).OrderBy(o => o).ToArray();
        var spread = offsets[^1] - offsets[0];
        var detail = string.Join(", ", readings.Select(r => $"{r.Source} {r.Offset.TotalSeconds:+0.0;-0.0}s"));

        if (spread > MaximumSpread)
        {
            Log.Warn(
                $"Time sync: sources disagree by {spread.TotalSeconds:0.0}s ({detail}). "
                + "Not correcting anything. Using system time.");
            return;
        }

        // Median, not mean: one bad reading shifts a mean, and cannot shift a median past its neighbours.
        var median = offsets[offsets.Length / 2];

        lock (Gate)
        {
            _agreeingSources = readings.Count;
        }

        if (median.Duration() < CorrectionThreshold)
        {
            Log.Info(
                $"Time sync: clock agrees with {readings.Count} sources to within "
                + $"{median.Duration().TotalSeconds:0.000}s. No correction needed. ({detail})");
            return;
        }

        lock (Gate)
        {
            _syncedAt = DateTimeOffset.UtcNow + median;
            _elapsed = Stopwatch.StartNew();
            _measuredOffset = median;
        }

        Log.Warn(
            $"Time sync: this machine's clock is {median.Duration():hh\\:mm\\:ss} "
            + $"{(median < TimeSpan.Zero ? "AHEAD OF" : "BEHIND")} real time, agreed by {readings.Count} "
            + $"independent sources ({detail}). Countdowns are corrected internally; the system clock is "
            + "left alone.");
    }

    /// <summary>
    /// Cloudflare's trace output is <c>key=value</c> lines; <c>ts</c> is a Unix timestamp with a
    /// fractional part, which is where the millisecond precision comes from.
    /// </summary>
    private static async Task<Reading?> ReadCloudflareAsync(HttpClient http, CancellationToken cancellationToken)
    {
        try
        {
            var before = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            var body = await http.GetStringAsync(CloudflareTrace, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            var line = body.Split('\n').FirstOrDefault(l => l.StartsWith("ts=", StringComparison.Ordinal));

            if (line is null
                || !double.TryParse(line[3..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var unix))
            {
                return null;
            }

            var served = DateTimeOffset.FromUnixTimeMilliseconds((long)(unix * 1000));

            return new Reading("cloudflare", served - (before + (stopwatch.Elapsed / 2)));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or OperationCanceledException or FormatException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Asks one host the time via its <c>Date</c> header.
    ///
    /// <para>The request is bracketed and the midpoint used, so the round trip cancels out instead of
    /// being charged to the clock — otherwise a slow connection reads as drift. HEAD keeps it to headers.
    /// </para>
    /// </summary>
    private static async Task<Reading?> ReadDateHeaderAsync(
        string url,
        HttpClient http,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);

            var before = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            if (!response.Headers.TryGetValues("Date", out var values)
                || values.FirstOrDefault() is not { } raw
                || !DateTimeOffset.TryParse(
                    raw, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var served))
            {
                return null;
            }

            return new Reading(new Uri(url).Host, served - (before + (stopwatch.Elapsed / 2)));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or OperationCanceledException or UriFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Discards any correction and returns to system time. Public so a session can be reset — used by
    /// tests, and the natural thing to call if the player fixes their clock and wants a fresh check.
    /// </summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _syncedAt = null;
            _elapsed = null;
            _measuredOffset = TimeSpan.Zero;
            _agreeingSources = 0;
        }
    }
}
