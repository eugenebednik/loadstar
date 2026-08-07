namespace Loadstar.Core.Capture;

/// <summary>
/// The screenshots queued for the next question, oldest first.
///
/// <para><b>Why more than one.</b> The advice routinely needs two or three screens that cannot be open
/// at the same time — runes, then artifacts, then the character sheet. With a single slot the model had
/// to ask the player to go and look somewhere else and try again, which meant it never saw two of them
/// together and could not compare them. It could only ever answer about the last screen sent.</para>
///
/// <para><b>Why four.</b> A screenshot costs roughly 4,800 tokens, so four is about 19,000 on top of a
/// ~23,000-token system prompt. That is affordable and it is close to the point where it stops being so.
/// Four also covers the real cases: the longest chain the advice actually asks for is character sheet →
/// runes → artifacts → gear tooltip.</para>
///
/// <para><b>Full means the oldest goes.</b> Not "refuse the fifth": the hotkey is the only way to add a
/// screenshot, and a hotkey that silently stops working is the bug this replaced. Dropping the oldest
/// keeps the most recent four, which is what someone pressing it a fifth time is asking for.</para>
///
/// <para>Holds PNG bytes only, no decoded bitmaps. That keeps this testable and keeps image lifetime the
/// business of whatever draws them — a UI that decodes on demand cannot leak a bitmap it never owned.</para>
/// </summary>
public sealed class PendingCaptures
{
    /// <summary>The ceiling. See the type remarks for why it is this number and not a larger one.</summary>
    public const int Maximum = 4;

    private readonly List<CapturedFrame> _frames = [];

    /// <summary>Oldest first, which is also the order they are sent in and shown in.</summary>
    public IReadOnlyList<CapturedFrame> Frames => _frames;

    public int Count => _frames.Count;

    public bool IsEmpty => _frames.Count == 0;

    /// <summary>True when the next <see cref="Add"/> will evict, so the UI can say so before it happens.</summary>
    public bool IsFull => _frames.Count >= Maximum;

    /// <summary>
    /// Appends a capture, dropping the oldest if that would exceed <see cref="Maximum"/>.
    /// </summary>
    /// <returns>The frame that was evicted to make room, or null when nothing was.</returns>
    public CapturedFrame? Add(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        CapturedFrame? evicted = null;

        if (_frames.Count >= Maximum)
        {
            evicted = _frames[0];
            _frames.RemoveAt(0);
        }

        _frames.Add(frame);

        return evicted;
    }

    /// <summary>
    /// Replaces everything with a single capture — the retake case, where the screenshot was of the wrong
    /// screen and keeping it would send the wrong screen along with the right one.
    /// </summary>
    public void Replace(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        _frames.Clear();
        _frames.Add(frame);
    }

    /// <summary>
    /// Drops one capture the player rejected. Out-of-range indexes are ignored rather than thrown: this
    /// is driven by UI that was built from a snapshot of the list, and a stale click is not an error.
    /// </summary>
    public void RemoveAt(int index)
    {
        if (index >= 0 && index < _frames.Count)
        {
            _frames.RemoveAt(index);
        }
    }

    /// <summary>Keeps only these, in this order. How the ask window reports what survived its delete buttons.</summary>
    public void Keep(IEnumerable<CapturedFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);

        var kept = frames.Take(Maximum).ToArray();

        _frames.Clear();
        _frames.AddRange(kept);
    }

    public void Clear() => _frames.Clear();
}
