using Loadstar.Core.Capture;

using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// The queue of screenshots a question is asked about. The rules that matter are the eviction order and
/// the fact that nothing here ever refuses input — a hotkey that stops responding is the failure this
/// whole feature was built to remove.
/// </summary>
public class PendingCapturesTests
{
    private static CapturedFrame Frame(string label) => new()
    {
        Png = [0x89, 0x50, 0x4E, 0x47],
        Width = 1920,
        Height = 1080,
        CapturedAt = DateTimeOffset.UnixEpoch,
        WindowTitle = "THRONE AND LIBERTY",
        Label = label,
    };

    private static string[] Labels(PendingCaptures captures) =>
        captures.Frames.Select(f => f.Label!).ToArray();

    [Fact]
    public void CapturesArriveOldestFirst()
    {
        var captures = new PendingCaptures();

        Assert.True(captures.IsEmpty);

        captures.Add(Frame("sheet"));
        captures.Add(Frame("runes"));

        Assert.Equal(["sheet", "runes"], Labels(captures));
        Assert.False(captures.IsFull);
    }

    /// <summary>
    /// The fifth press. It must not be refused and it must not land in the middle — the player pressing
    /// the hotkey a fifth time wants the four most recent screens, in the order they took them.
    /// </summary>
    [Fact]
    public void AFifthCaptureEvictsTheOldest()
    {
        var captures = new PendingCaptures();

        foreach (var label in (string[])["sheet", "runes", "artifacts", "tooltip"])
        {
            Assert.Null(captures.Add(Frame(label)));
        }

        Assert.True(captures.IsFull);

        var evicted = captures.Add(Frame("skills"));

        Assert.Equal("sheet", evicted?.Label);
        Assert.Equal(["runes", "artifacts", "tooltip", "skills"], Labels(captures));
        Assert.Equal(PendingCaptures.Maximum, captures.Count);
    }

    /// <summary>Pressing it many more times must keep working and must never grow past the ceiling.</summary>
    [Fact]
    public void TheCeilingHoldsUnderRepeatedAdds()
    {
        var captures = new PendingCaptures();

        for (var i = 0; i < 20; i++)
        {
            captures.Add(Frame($"shot{i}"));

            Assert.True(captures.Count <= PendingCaptures.Maximum);
        }

        Assert.Equal(["shot16", "shot17", "shot18", "shot19"], Labels(captures));
    }

    /// <summary>
    /// Retake means the screenshot was of the wrong screen. Keeping it would send the wrong screen
    /// alongside the right one, which is worse than either alone — so retake clears rather than appends.
    /// </summary>
    [Fact]
    public void RetakeReplacesEverythingRatherThanAppending()
    {
        var captures = new PendingCaptures();

        captures.Add(Frame("world"));
        captures.Add(Frame("map"));
        captures.Replace(Frame("sheet"));

        Assert.Equal(["sheet"], Labels(captures));
    }

    [Fact]
    public void RemovingDropsOnlyThatCapture()
    {
        var captures = new PendingCaptures();

        captures.Add(Frame("sheet"));
        captures.Add(Frame("runes"));
        captures.Add(Frame("artifacts"));

        captures.RemoveAt(1);

        Assert.Equal(["sheet", "artifacts"], Labels(captures));
    }

    /// <summary>
    /// A click on a thumbnail that is already gone. The UI is built from a snapshot, so this is a race
    /// the user can genuinely lose rather than a programming error, and throwing would take down the
    /// dialog over a double-click.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(99)]
    public void RemovingSomethingThatIsNotThereIsIgnored(int index)
    {
        var captures = new PendingCaptures();

        captures.Add(Frame("sheet"));
        captures.Add(Frame("runes"));

        captures.RemoveAt(index);

        Assert.Equal(["sheet", "runes"], Labels(captures));
    }

    [Fact]
    public void KeepAdoptsWhatSurvivedTheDialog()
    {
        var captures = new PendingCaptures();

        var sheet = Frame("sheet");
        var artifacts = Frame("artifacts");

        captures.Add(sheet);
        captures.Add(Frame("runes"));
        captures.Add(artifacts);

        captures.Keep([sheet, artifacts]);

        Assert.Equal(["sheet", "artifacts"], Labels(captures));
    }

    /// <summary>Keep cannot be used to smuggle in more than the ceiling allows.</summary>
    [Fact]
    public void KeepIsStillBoundedByTheMaximum()
    {
        var captures = new PendingCaptures();

        captures.Keep(Enumerable.Range(0, 10).Select(i => Frame($"shot{i}")));

        Assert.Equal(PendingCaptures.Maximum, captures.Count);
    }

    [Fact]
    public void ClearEmptiesTheQueue()
    {
        var captures = new PendingCaptures();

        captures.Add(Frame("sheet"));
        captures.Clear();

        Assert.True(captures.IsEmpty);
        Assert.False(captures.IsFull);
    }
}
