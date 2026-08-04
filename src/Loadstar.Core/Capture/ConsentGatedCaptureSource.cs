namespace Loadstar.Core.Capture;

/// <summary>
/// Wraps a capture source and refuses to run it until the user has consented.
///
/// <para>docs/anti-cheat-posture.md commits to "screen capture is off until the user turns it
/// on". That is the kind of promise that is true on the day it is written and quietly stops being
/// true the first time someone adds a second call site. Making the gate a wrapper rather than an
/// <c>if</c> means there is no capture path that can skip it: the rest of the app is handed one of
/// these and never sees the underlying source.</para>
///
/// <para>The consent flag is read through a callback rather than captured at construction, so
/// revoking consent mid-session takes effect on the next capture instead of the next restart.</para>
/// </summary>
public sealed class ConsentGatedCaptureSource : ICaptureSource
{
    private readonly ICaptureSource _inner;
    private readonly Func<bool> _hasConsent;
    private readonly Action<CapturedFrame>? _onCaptured;

    /// <param name="inner">The real capture source.</param>
    /// <param name="hasConsent">Re-read before every capture.</param>
    /// <param name="onCaptured">
    /// Fired after each successful capture. This is the hook for the visible capture indicator the
    /// posture document requires — the user should never be unsure whether their screen is being
    /// read, so something must announce it every single time.
    /// </param>
    public ConsentGatedCaptureSource(
        ICaptureSource inner,
        Func<bool> hasConsent,
        Action<CapturedFrame>? onCaptured = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(hasConsent);

        _inner = inner;
        _hasConsent = hasConsent;
        _onCaptured = onCaptured;
    }

    public string Name => _inner.Name;

    public bool IsSupported => _inner.IsSupported;

    public async Task<CaptureResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken)
    {
        if (!_hasConsent())
        {
            return CaptureResult.Fail(
                CaptureStatus.ConsentNotGiven,
                "Screen capture is off. Loadstar does not read the screen until you turn it on.");
        }

        var result = await _inner.CaptureAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            _onCaptured?.Invoke(result.Frame);
        }

        return result;
    }

    public void Dispose() => _inner.Dispose();
}
