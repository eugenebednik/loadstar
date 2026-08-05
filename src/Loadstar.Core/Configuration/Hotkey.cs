namespace Loadstar.Core.Configuration;

/// <summary>
/// A parsed global hotkey, e.g. <c>Ctrl+Alt+S</c>.
///
/// <para>Parsing lives in Core rather than beside the Win32 registration because it is pure string
/// handling with a lot of edge cases — user-typed settings, differing separators, alias spellings —
/// and that is worth testing without a message pump. The Windows layer only turns the result into a
/// <c>RegisterHotKey</c> call.</para>
/// </summary>
public sealed record Hotkey
{
    /// <summary>Win32 modifier flags: Alt 1, Control 2, Shift 4, Win 8.</summary>
    public required uint Modifiers { get; init; }

    /// <summary>Win32 virtual-key code.</summary>
    public required uint VirtualKey { get; init; }

    /// <summary>The normalised text form, so round-tripping through settings is stable.</summary>
    public required string Display { get; init; }

    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    /// <summary>
    /// Parses a hotkey string. Returns null rather than throwing — a bad value in a settings file
    /// should degrade to "no hotkey" with a visible warning, not stop the app from starting.
    /// </summary>
    public static Hotkey? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split(['+', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            return null;
        }

        uint modifiers = 0;
        string? keyName = null;

        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL" or "CTL":
                    modifiers |= ModControl;
                    break;
                case "ALT":
                    modifiers |= ModAlt;
                    break;
                case "SHIFT":
                    modifiers |= ModShift;
                    break;
                case "WIN" or "WINDOWS" or "META" or "SUPER":
                    modifiers |= ModWin;
                    break;
                default:
                    // More than one non-modifier token means the string is malformed rather than
                    // ambiguous — "Ctrl+A+B" is not a hotkey.
                    if (keyName is not null)
                    {
                        return null;
                    }

                    keyName = part;
                    break;
            }
        }

        if (keyName is null || TryVirtualKey(keyName) is not { } vk)
        {
            return null;
        }

        // A bare key with no modifier would swallow that key globally, across every application.
        // Refusing is friendlier than letting someone lose the "S" key everywhere.
        if (modifiers == 0)
        {
            return null;
        }

        return new Hotkey
        {
            Modifiers = modifiers,
            VirtualKey = vk,
            Display = Format(modifiers, keyName),
        };
    }

    private static uint? TryVirtualKey(string name)
    {
        var upper = name.ToUpperInvariant();

        if (upper.Length == 1)
        {
            var c = upper[0];

            if (c is >= 'A' and <= 'Z')
            {
                return c;
            }

            if (c is >= '0' and <= '9')
            {
                return c;
            }
        }

        if (upper.Length is 2 or 3 && upper[0] == 'F' &&
            int.TryParse(upper[1..], out var fn) && fn is >= 1 and <= 24)
        {
            return (uint)(0x70 + fn - 1);
        }

        return upper switch
        {
            "SPACE" => 0x20,
            "ENTER" or "RETURN" => 0x0D,
            "TAB" => 0x09,
            "ESC" or "ESCAPE" => 0x1B,
            "INS" or "INSERT" => 0x2D,
            "DEL" or "DELETE" => 0x2E,
            "HOME" => 0x24,
            "END" => 0x23,
            "PGUP" or "PAGEUP" => 0x21,
            "PGDN" or "PAGEDOWN" => 0x22,
            "`" or "TILDE" or "GRAVE" => 0xC0,
            _ => null,
        };
    }

    private static string Format(uint modifiers, string keyName)
    {
        var parts = new List<string>(4);

        if ((modifiers & ModControl) != 0) { parts.Add("Ctrl"); }
        if ((modifiers & ModAlt) != 0) { parts.Add("Alt"); }
        if ((modifiers & ModShift) != 0) { parts.Add("Shift"); }
        if ((modifiers & ModWin) != 0) { parts.Add("Win"); }

        parts.Add(keyName.Length == 1 ? keyName.ToUpperInvariant() : Capitalise(keyName));

        return string.Join("+", parts);
    }

    private static string Capitalise(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    public override string ToString() => Display;
}
