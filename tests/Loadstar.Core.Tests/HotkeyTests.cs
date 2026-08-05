using Loadstar.Core.Configuration;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Hotkey parsing. Users type these into a settings box, so the input is arbitrary text and the
/// failure mode has to be "no hotkey, with a warning" rather than a crash on startup.
/// </summary>
public sealed class HotkeyTests
{
    [Fact]
    public void ParsesTheDefaultCaptureHotkey()
    {
        var hotkey = Hotkey.TryParse("Ctrl+Alt+S")!;

        Assert.Equal(Hotkey.ModControl | Hotkey.ModAlt, hotkey.Modifiers);
        Assert.Equal('S', (char)hotkey.VirtualKey);
        Assert.Equal("Ctrl+Alt+S", hotkey.Display);
    }

    [Theory]
    [InlineData("ctrl+alt+s")]
    [InlineData("CONTROL + ALT + S")]
    [InlineData("Ctl-Alt-S")]
    public void AcceptsAliasesSpacingAndDashSeparators(string text)
    {
        // Settings files get hand-edited, so tolerate the obvious spellings rather than silently
        // dropping the user's hotkey.
        Assert.Equal("Ctrl+Alt+S", Hotkey.TryParse(text)!.Display);
    }

    [Fact]
    public void NormalisesToAStableDisplayForm()
    {
        // Modifier order is canonicalised so a round-trip through settings does not churn the value.
        Assert.Equal("Ctrl+Alt+Shift+F5", Hotkey.TryParse("shift+alt+ctrl+f5")!.Display);
    }

    [Fact]
    public void FunctionKeysMapToTheirVirtualKeyCodes()
    {
        Assert.Equal(0x70u, Hotkey.TryParse("Ctrl+F1")!.VirtualKey);
        Assert.Equal(0x7Bu, Hotkey.TryParse("Ctrl+F12")!.VirtualKey);
    }

    [Fact]
    public void NamedKeysAreSupported()
    {
        Assert.Equal(0x20u, Hotkey.TryParse("Ctrl+Space")!.VirtualKey);
        Assert.Equal(0x2Du, Hotkey.TryParse("Alt+Insert")!.VirtualKey);
    }

    [Fact]
    public void ModifierlessHotkeyIsRejected()
    {
        // Registering a bare key would swallow it globally in every application — losing the "S"
        // key everywhere is a worse outcome than refusing the setting.
        Assert.Null(Hotkey.TryParse("S"));
        Assert.Null(Hotkey.TryParse("F5"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+NotAKey")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+Alt")]
    public void MalformedInputYieldsNullRatherThanThrowing(string? text)
    {
        Assert.Null(Hotkey.TryParse(text));
    }

    [Fact]
    public void WinKeyIsRecognised()
    {
        var hotkey = Hotkey.TryParse("Win+Shift+L")!;

        Assert.Equal(Hotkey.ModWin | Hotkey.ModShift, hotkey.Modifiers);
    }
}
