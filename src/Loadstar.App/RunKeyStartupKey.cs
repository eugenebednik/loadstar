using Microsoft.Win32;

using Loadstar.Core.Startup;

namespace Loadstar.App;

/// <summary>
/// Autostart through the per-user Run key: <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>.
///
/// <para><b>HKCU, never HKLM.</b> This is one user's preference about their own sign-in, so it belongs in
/// their own hive: no elevation, no effect on anyone else who uses the machine, and nothing left behind for
/// other accounts. The HKLM equivalent would need admin rights and would be the wrong scope besides.</para>
///
/// <para><b>The Run key rather than a shortcut in the Startup folder.</b> Both work. The Run key is a
/// single value that can be read back exactly as written, which is what lets the checkbox reflect the real
/// state instead of a stored guess; a shortcut means creating and parsing a .lnk to answer the same
/// question. It is also the entry Task Manager's Startup tab shows, so a user who wants to turn this off
/// from outside the app finds it where they expect.</para>
///
/// <para><b>An uninstall leaves this behind, and that is not fixable from the installer.</b> Windows
/// never removes Run entries by itself, and the obvious answer — a <c>RemoveRegistryValue</c> in the MSI —
/// does not work here: the package is <c>perMachine</c>, so it runs elevated in whichever account
/// performed the install, which need not be the account that ticked the box. Its HKCU is the wrong hive,
/// and an HKCU key in a perMachine package is the ICE38 / repair-loop trap the installer already avoids
/// deliberately.</para>
///
/// <para>The residue is a Run value pointing at a path that no longer exists, which Windows skips. The
/// route out is Task Manager's Startup tab, which is why the checkbox's hint names it — that sentence is
/// there for this case, not as filler.</para>
/// </summary>
internal sealed class RunKeyStartupKey : IStartupKey
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// The value name, which is also the label Task Manager shows next to the entry. Stable on purpose:
    /// renaming it would orphan the entry every existing user has, leaving them with autostart they can no
    /// longer turn off from inside the app.
    /// </summary>
    private const string ValueName = "Loadstar";

    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);

        return key?.GetValue(ValueName) as string;
    }

    public void Write(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException($@"Could not open HKCU\{RunKeyPath} for writing.");

        key.SetValue(ValueName, command, RegistryValueKind.String);
    }

    public void Delete()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);

        // throwOnMissingValue: false — removing something that is already gone is the desired end state,
        // not a failure. The user unchecked a box; they do not care whether there was anything there.
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
