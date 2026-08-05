using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Turns docs/anti-cheat-posture.md from a good intention into something CI checks.
///
/// <para>Every row of that document's "Forbidden" table is a native API, and the thing they all
/// have in common is that reaching them from C# means declaring a P/Invoke. So the compiled IL is
/// scanned for those declarations and the build fails if one appears — behind any flag, in any code
/// path, called or not.</para>
///
/// <para>It is a blunt instrument and that is deliberate. It cannot prove the absence of every
/// possible abuse, and it is not trying to; it makes the specific, well-understood ways this
/// project could get its users banned impossible to add quietly. The realistic failure mode for a
/// rule like this is not somebody defeating it, it is somebody adding
/// <c>ReadProcessMemory</c> at 2am to fix the fullscreen case and nobody noticing in review.</para>
/// </summary>
public sealed class AntiCheatPostureTests
{
    /// <summary>
    /// Native modules any Loadstar assembly may call into.
    ///
    /// <para>An allowlist rather than a denylist, because the interesting risk is a module nobody
    /// thought to forbid — <c>detours.dll</c>, <c>minhook.dll</c>, a wrapped <c>d3d9.dll</c>. Adding
    /// an entry here is a one-line diff that a reviewer cannot miss, which is the point.</para>
    ///
    /// <para>Deliberately does not pre-authorise <c>user32</c> or <c>kernel32</c>. The posture
    /// document permits <c>RegisterHotKey</c>, so whoever adds hotkeys will need to add
    /// <c>user32</c> here — and that is exactly the moment the surrounding P/Invokes deserve a
    /// second look.</para>
    /// </summary>
    private static readonly HashSet<string> AllowedModules = new(StringComparer.OrdinalIgnoreCase)
    {
        // WinRT activation, to reach the GraphicsCaptureItem interop factory.
        "combase",

        // Creates Loadstar's own Direct3D device to receive compositor-delivered frames.
        // Not the game's device, and no game function is wrapped to obtain it.
        "d3d11",

        // Global hotkeys only — RegisterHotKey / UnregisterHotKey, which the posture document
        // permits by name. The direction is what makes them safe: the OS notifies us when the user
        // presses a combination. Nothing is sent to the game and no input is synthesised.
        //
        // user32 also contains SendInput, PostMessage, SendMessage, SetWindowsHookEx and
        // SetForegroundWindow. All of those are on ForbiddenEntryPoints below and stay there —
        // allowing the module does not allow the module's contents.
        "user32",

        // Title bar colour on Loadstar's own windows, so a dark theme does not get a white caption.
        // Presentation only: it sets an attribute on a window we own and reads nothing back.
        "dwmapi",
    };

    /// <summary>
    /// The functions from the posture document's forbidden table, plus the obvious neighbours of
    /// each. Checked regardless of which module declares them, since <c>user32</c> holds both
    /// permitted and prohibited calls.
    /// </summary>
    private static readonly HashSet<string> ForbiddenEntryPoints = new(StringComparer.OrdinalIgnoreCase)
    {
        // "ReadProcessMemory / WriteProcessMemory on the game"
        "readprocessmemory",
        "writeprocessmemory",
        "ntreadvirtualmemory",
        "ntwritevirtualmemory",
        "virtualallocex",
        "virtualprotectex",
        "openprocess",
        "ntopenprocess",

        // "DLL injection into the game process"
        "createremotethread",
        "createremotethreadex",
        "ntcreatethreadex",
        "rtlcreateuserthread",
        "queueuserapc",
        "setthreadcontext",

        // "Hooking D3D/DXGI, Present, or any game function"
        "setwindowshookex",
        "detourattach",
        "detourtransactionbegin",
        "detourupdatethread",
        "mh_createhook",
        "mh_enablehook",
        "lhinstallhook",

        // "SendInput, keybd_event, PostMessage to the game window"
        "sendinput",
        "keybd_event",
        "mouse_event",
        "postmessage",
        "sendmessage",
        "sendmessagetimeout",
        "sendmessagecallback",
        "sendnotifymessage",
        "postthreadmessage",
        "setcursorpos",
        "blockinput",
        "setforegroundwindow",

        // "Driver-level or kernel-mode anything"
        "ntloaddriver",
        "deviceiocontrol",
        "createservice",
        "openscmanager",
    };

    /// <summary>
    /// The exact native surface, recorded so that any change to it fails until someone updates this
    /// list. The denylist above catches the known-bad; this catches the unknown-bad by making
    /// <em>every</em> addition visible in review.
    /// </summary>
    private static readonly HashSet<string> ExpectedNativeSurface = new(StringComparer.OrdinalIgnoreCase)
    {
        "combase!WindowsCreateString",
        "combase!WindowsDeleteString",
        "combase!RoGetActivationFactory",
        "d3d11!D3D11CreateDevice",
        "d3d11!CreateDirect3D11DeviceFromDXGIDevice",

        // Reviewed and approved 2026-08-04, when the tray shell landed. Permitted explicitly by
        // docs/anti-cheat-posture.md: "Global hotkeys via RegisterHotKey, which routes through the
        // OS, not the game."
        "user32!RegisterHotKey",
        "user32!UnregisterHotKey",

        // Reviewed and approved 2026-08-04 alongside the light/dark theme. Sets
        // DWMWA_USE_IMMERSIVE_DARK_MODE on Loadstar's own windows.
        "dwmapi!DwmSetWindowAttribute",
    };

    [Fact]
    public void NoAssemblyDeclaresAForbiddenNativeFunction()
    {
        var violations = AllImports()
            .Where(import => ForbiddenEntryPoints.Contains(import.NormalizedEntryPoint))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "docs/anti-cheat-posture.md forbids these, in any code path, behind any flag:\n" +
            string.Join("\n", violations.Select(v => "  " + v)) +
            "\n\nIf a feature seems to require one of these, the feature does not ship.");
    }

    [Fact]
    public void EveryNativeModuleIsOnTheAllowlist()
    {
        var unexpected = AllImports()
            .Where(import => !AllowedModules.Contains(import.NormalizedModule))
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            "These call into native modules the posture allowlist does not cover:\n" +
            string.Join("\n", unexpected.Select(v => "  " + v)) +
            "\n\nIf the call is legitimate, add the module to AllowedModules in this file — and " +
            "check docs/anti-cheat-posture.md still holds while you are there.");
    }

    [Fact]
    public void NativeSurfaceMatchesTheRecordedBaseline()
    {
        var actual = AllImports()
            .Select(import => $"{import.NormalizedModule}!{import.EntryPoint}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = actual.Except(ExpectedNativeSurface, StringComparer.OrdinalIgnoreCase).ToArray();
        var removed = ExpectedNativeSurface.Except(actual, StringComparer.OrdinalIgnoreCase).ToArray();

        Assert.True(
            added.Length == 0 && removed.Length == 0,
            "The set of native calls Loadstar makes has changed.\n" +
            (added.Length > 0 ? "  Added:   " + string.Join(", ", added) + "\n" : string.Empty) +
            (removed.Length > 0 ? "  Removed: " + string.Join(", ", removed) + "\n" : string.Empty) +
            "\nThis is not automatically a problem — it is a prompt to re-read " +
            "docs/anti-cheat-posture.md and confirm the new surface is still observation-only. " +
            "Update ExpectedNativeSurface once you have.");
    }

    /// <summary>
    /// A scan that finds nothing passes every assertion above, so the scan's own reach is asserted
    /// too. Without this the suite would go quietly green the moment an output path changed.
    /// </summary>
    [Fact]
    public void ScannerActuallyFoundTheAssembliesItIsMeantToCheck()
    {
        var assemblies = AssemblyScanner.FindLoadstarAssemblies();

        Assert.True(
            assemblies.Count > 0,
            $"Found no built Loadstar assemblies under {AssemblyScanner.FindRepositoryRoot()}\\src. " +
            "The posture scan cannot pass by having nothing to look at.");

        var names = assemblies.Select(Path.GetFileName).ToArray();

        // Named explicitly rather than counted, because the scan once passed while silently missing
        // the application itself: the glob was "Loadstar.*.dll" and the shell assembly is
        // "Loadstar.dll". Everything with native code must be listed here by name.
        foreach (var required in new[] { "Loadstar.Capture.Windows.dll", "Loadstar.dll" })
        {
            Assert.True(
                names.Contains(required, StringComparer.OrdinalIgnoreCase),
                $"{required} was not scanned, so this posture run proves nothing about it. " +
                "Assemblies seen: " + string.Join(", ", names));
        }
    }

    /// <summary>
    /// The capture assembly is where the risk concentrates, so its surface is pinned tightly enough
    /// that a reviewer can hold it in their head.
    /// </summary>
    [Fact]
    public void CaptureAssemblyKeepsANarrowNativeSurface()
    {
        var imports = AssemblyScanner
            .FindLoadstarAssemblies()
            .Where(path => Path.GetFileName(path).Equals("Loadstar.Capture.Windows.dll", StringComparison.OrdinalIgnoreCase))
            .SelectMany(AssemblyScanner.ReadNativeImports)
            .Select(import => $"{import.NormalizedModule}!{import.EntryPoint}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(
            imports.Length <= 8,
            "The capture assembly's native surface has grown to " + imports.Length + " calls:\n" +
            string.Join("\n", imports.Select(i => "  " + i)) +
            "\n\nIt is meant to stay small enough to audit at a glance.");
    }

    private static IReadOnlyList<NativeImport> AllImports() =>
        AssemblyScanner
            .FindLoadstarAssemblies()
            .SelectMany(AssemblyScanner.ReadNativeImports)
            .ToArray();
}
