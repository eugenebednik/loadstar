using System.Diagnostics.CodeAnalysis;
using Loadstar.Core.Configuration;

namespace Loadstar.Core.Capture;

/// <summary>
/// Something that can produce a PNG of the game window.
///
/// <para>The interface exists so Core never references a capture technology. Everything above it
/// — the console app, the advice loop — depends on this and stays platform-neutral and testable;
/// the one implementation that talks to Windows Graphics Capture lives in its own assembly, which
/// is also what keeps the anti-cheat audit surface confined to a single project.</para>
/// </summary>
public interface ICaptureSource : IDisposable
{
    /// <summary>Shown to the user, so they know what is reading their screen.</summary>
    string Name { get; }

    /// <summary>
    /// False when this machine cannot do the capture at all — too old a Windows build, or a
    /// session with no desktop. Callers should say so rather than reporting a failed capture.
    /// </summary>
    bool IsSupported { get; }

    Task<CaptureResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken);
}

public sealed record CaptureRequest
{
    /// <summary>Substring matched against window titles to find the game.</summary>
    public required string WindowTitleMatch { get; init; }

    /// <summary>Crop, as fractions of the window. Null captures the whole client area.</summary>
    public CaptureRegion? Region { get; init; }

    /// <summary>
    /// Regions blanked before encoding, as fractions of the <em>window</em> — not of the crop.
    /// Expressed that way because they describe fixed UI furniture (party list, chat) whose
    /// position has nothing to do with whatever the current capture happens to be cropped to.
    /// </summary>
    public IReadOnlyList<CaptureRegion> PrivacyMasks { get; init; } = [];

    /// <summary>What this capture is of, carried through to the model so it can tell frames apart.</summary>
    public string? Label { get; init; }

    /// <summary>
    /// How long to wait for a frame. A window that is minimised or on another desktop simply
    /// never produces one, so this is a normal outcome rather than an error path.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
}

public sealed record CaptureResult
{
    public required CaptureStatus Status { get; init; }

    public CapturedFrame? Frame { get; init; }

    /// <summary>Human-readable explanation. Always populated when <see cref="Status"/> is not Ok.</summary>
    public string? Detail { get; init; }

    [MemberNotNullWhen(true, nameof(Frame))]
    public bool Success => Status == CaptureStatus.Ok && Frame is not null;

    public static CaptureResult Ok(CapturedFrame frame) =>
        new() { Status = CaptureStatus.Ok, Frame = frame };

    public static CaptureResult Fail(CaptureStatus status, string detail) =>
        new() { Status = status, Detail = detail };
}

public enum CaptureStatus
{
    Ok = 0,

    /// <summary>The user has not turned capture on. Not an error — the documented default.</summary>
    ConsentNotGiven,

    /// <summary>This machine or session cannot capture at all.</summary>
    Unsupported,

    /// <summary>No visible window matched the configured title.</summary>
    WindowNotFound,

    /// <summary>The window exists but produced no frame in time — minimised, or exclusive fullscreen.</summary>
    TimedOut,

    Failed,
}

public sealed record CapturedFrame
{
    public required byte[] Png { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }

    /// <summary>Title of the window this came from, so the user can confirm we read the right one.</summary>
    public required string WindowTitle { get; init; }

    public string? Label { get; init; }

    /// <summary>How many privacy masks were actually painted. Zero when none overlapped the crop.</summary>
    public int PrivacyMasksApplied { get; init; }
}
