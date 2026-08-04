using Loadstar.Core.Model;

namespace Loadstar.Core.Ai;

/// <summary>
/// The running conversation for one play session.
///
/// The assistant is meant to remember the whole session, so turns are never deleted. What does
/// get dropped is the <em>image payload</em> of older turns: once the model has read a
/// screenshot and told us what was in it, keeping the pixels around costs ~4,800 tokens a turn
/// to re-send information we already extracted as text. See docs/conversation-model.md.
/// </summary>
public sealed class ConversationHistory
{
    /// <summary>
    /// How many recent turns keep their screenshots. Two is enough for the model to compare
    /// "now" against "just before" visually; everything older is served by the text summary.
    /// </summary>
    private const int TurnsRetainingImages = 2;

    private readonly List<ConversationTurn> _turns = [];

    /// <summary>
    /// The system prompt. Byte-stable for the session's whole life and cached as the prompt
    /// prefix, which is why it is set once at construction and has no setter — a mutable
    /// system prompt would silently invalidate the cache on every request.
    /// </summary>
    public string SystemPrompt { get; }

    public IReadOnlyList<ConversationTurn> Turns => _turns;

    public int SnapshotCount { get; private set; }

    public ConversationHistory(string systemPrompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        SystemPrompt = systemPrompt;
    }

    /// <summary>
    /// Records a new snapshot as a user turn, and demotes any image that has now fallen out of
    /// the retention window to its text summary.
    /// </summary>
    public void AppendSnapshot(CapturedImage image, string observationText)
    {
        SnapshotCount++;

        _turns.Add(new ConversationTurn
        {
            Role = ConversationRole.User,
            Text = observationText,
            Image = image,
            SnapshotIndex = SnapshotCount,
        });

        PruneOldImages();
    }

    /// <summary>
    /// Records the assistant's reply.
    ///
    /// <paramref name="rawContent"/> is the provider's content blocks, kept verbatim. That
    /// matters for Anthropic's server-side compaction: it emits compaction blocks that have to
    /// be echoed back on the next request, and storing only the text would drop them and
    /// silently disable compaction on long sessions.
    /// </summary>
    public void AppendAssistantTurn(string text, IReadOnlyList<object>? rawContent = null)
    {
        _turns.Add(new ConversationTurn
        {
            Role = ConversationRole.Assistant,
            Text = text,
            RawContent = rawContent,
            SnapshotIndex = SnapshotCount,
        });
    }

    /// <summary>
    /// Replaces the image on any user turn older than the retention window with nothing —
    /// the turn's text stays, so the conversation still reads continuously.
    /// </summary>
    private void PruneOldImages()
    {
        var kept = 0;

        for (var i = _turns.Count - 1; i >= 0; i--)
        {
            if (_turns[i] is not { Role: ConversationRole.User, Image: not null })
            {
                continue;
            }

            kept++;

            if (kept > TurnsRetainingImages)
            {
                _turns[i] = _turns[i] with { Image = null, ImageWasPruned = true };
            }
        }
    }

    /// <summary>Rough token estimate, for showing the user where a session stands.</summary>
    public int EstimateTokens()
    {
        var total = SystemPrompt.Length / 4;

        foreach (var turn in _turns)
        {
            total += (turn.Text?.Length ?? 0) / 4;

            if (turn.Image is not null)
            {
                // A full-resolution capture runs to roughly this on current vision models.
                total += 4800;
            }
        }

        return total;
    }
}

public sealed record ConversationTurn
{
    public required ConversationRole Role { get; init; }
    public string? Text { get; init; }

    /// <summary>Null once pruned, or on assistant turns.</summary>
    public CapturedImage? Image { get; init; }

    /// <summary>True when this turn used to carry an image. Useful for the UI, and for tests.</summary>
    public bool ImageWasPruned { get; init; }

    /// <summary>Provider content blocks, preserved verbatim so compaction blocks survive.</summary>
    public IReadOnlyList<object>? RawContent { get; init; }

    public int SnapshotIndex { get; init; }
}

public enum ConversationRole
{
    User,
    Assistant,
}
