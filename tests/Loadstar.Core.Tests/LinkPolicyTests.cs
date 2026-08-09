using Loadstar.Core.Net;

using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Which links the app will hand to the browser.
///
/// <para>Worth real tests rather than a glance: the text these links come from is model output, shaped by
/// screenshots and by build names other players wrote. An allowlist nobody has checked is not an
/// allowlist.</para>
/// </summary>
public class LinkPolicyTests
{
    [Theory]
    [InlineData("https://questlog.gg/throne-and-liberty/en/character-builder/GoldenConquestAndWriter")]
    [InlineData("https://questlog.gg/")]
    [InlineData("http://questlog.gg/build")]
    [InlineData("https://QUESTLOG.GG/build")]
    [InlineData("https://cdn.questlog.gg/asset.webp")]
    [InlineData("   https://questlog.gg/build   ")]
    public void QuestlogLinksOpen(string link)
    {
        Assert.True(LinkPolicy.IsAllowed(link, out var uri));
        Assert.NotNull(uri);
    }

    /// <summary>
    /// The two ways an allowlist normally gets walked past. <c>questlog.gg.evil.com</c> CONTAINS the allowed
    /// host and belongs to someone else; <c>notquestlog.gg</c> ends with it but without the dot.
    /// </summary>
    [Theory]
    [InlineData("https://questlog.gg.evil.com/build")]
    [InlineData("https://notquestlog.gg/build")]
    [InlineData("https://evil.com/questlog.gg")]
    [InlineData("https://evil.com/?next=questlog.gg")]
    public void LookalikeHostsDoNotOpen(string link)
    {
        Assert.False(LinkPolicy.IsAllowed(link, out var uri));
        Assert.Null(uri);
    }

    /// <summary>
    /// Schemes other than http(s). Handed to ShellExecute these do something quite unlike opening a web
    /// page, and several of those somethings are worse.
    /// </summary>
    [Theory]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:windowsupdate")]
    [InlineData("ftp://questlog.gg/x")]
    [InlineData("mailto:someone@questlog.gg")]
    public void OnlyHttpAndHttpsOpen(string link) =>
        Assert.False(LinkPolicy.IsAllowed(link, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("questlog.gg/build")]
    public void NonsenseDoesNotOpen(string? link) =>
        Assert.False(LinkPolicy.IsAllowed(link, out _));
}
