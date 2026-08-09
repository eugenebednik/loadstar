using System.Diagnostics;

namespace Loadstar.App;

/// <summary>
/// Ends the running tray copy, then removes the two things an MSI uninstall cannot reach by itself: the
/// per-user autostart entry, and the per-user data directory holding settings, logs and the encrypted API key.
///
/// <para><b>Why the app does this rather than the installer.</b> Both live in the user's own hive and
/// profile, and the package is <c>perMachine</c>. A <c>RemoveRegistryValue</c> or <c>RemoveFolder</c> in the
/// package cannot reach them, and an HKCU key in a perMachine package is the ICE38 / repair-loop trap the
/// installer deliberately avoids. Running the app itself is the one way to land in the right place. See
/// <see cref="RunKeyStartupKey"/>, whose documentation concluded this was not fixable — it is, from this
/// side of the boundary rather than that one.</para>
///
/// <para><b>The context is decided entirely on the installer side, and getting it wrong is silent.</b> The
/// first attempt was scheduled as an immediate action, which in a perMachine package runs in the LocalSystem
/// server process — so this code read SYSTEM's hive and SYSTEM's AppData, deleted nothing, and reported
/// success. It must be DEFERRED and impersonated; see Loadstar.wxs, which now carries all three corrections
/// that took to find.</para>
///
/// <para><b>MEASURED LIMITATION, 2026-08-09: invoked from the installer this does not reliably work, and the
/// cause appears to be antivirus rather than the installer.</b> Across four full install-and-uninstall cycles
/// the process launched correctly and reported the right identity, not SYSTEM, with the right USERPROFILE —
/// and then saw a registry hive with six Run values where the real one had eight, and a
/// <see cref="DirectoryNotFoundException"/> for a directory holding 1,528 files. The same executable, same
/// user, run straight from a shell, cleared both correctly. That difference points at the msiexec-spawned
/// process being sandboxed: the MSI is unsigned, and this machine runs an AV that virtualises freshly
/// installed unsigned binaries. Code signing is the likely fix and is already outstanding.</para>
///
/// <para>So treat the per-user half as BEST EFFORT. What the uninstall does reliably is close the running app
/// (process enumeration is not affected — that part was verified working), remove every installed file, the
/// shortcuts and the Add/Remove entry, and finish without demanding a reboot. Release notes must not promise
/// more than that. The residue when it fails is a Run value pointing at a deleted executable, which Windows
/// skips, plus settings and a credential left on disk — so the honest instruction for someone who wants those
/// gone is to delete the folder named in the trace line below.</para>
///
/// <para><b>It still only cleans the invoking user.</b> Another account that also ran Loadstar keeps its own
/// Run value and its own data, because nothing running as one user may reach into another's profile. That is
/// a real limit, not an oversight, and it is why the checkbox's hint names Task Manager's Startup tab.</para>
///
/// <para><b>Deleting the data directory is deliberate, and upgrades are excluded.</b> It holds an API key,
/// so someone removing the app should not have their credential left on disk indefinitely. A version-to-
/// version update does not come through here: <c>MajorUpgrade</c> uninstalls the old package with
/// <c>UPGRADINGPRODUCTCODE</c> set, and the installer conditions this action on that being absent — so
/// settings and keys survive every update and go only on a real uninstall.</para>
///
/// <para><b>Nothing here may throw, and nothing here may prompt.</b> It runs inside an uninstall with no
/// user present to answer a dialog, and a non-zero exit or a message box would turn "your settings were left
/// behind" into "the uninstall appears to have hung". Every failure is swallowed on purpose; leaving a file
/// behind is a far better outcome than a stuck uninstaller. The action is also marked
/// <c>Return="ignore"</c> on the installer side, so this is belt and braces.</para>
/// </summary>
internal static class UninstallCleanup
{
    /// <summary>
    /// The flag the installer passes. Not <c>--uninstall</c>: that reads like a request to uninstall, and a
    /// curious user who found it in a log and ran it by hand would get something they did not expect.
    /// </summary>
    public const string Flag = "--uninstall-cleanup";

    /// <summary>
    /// Order is load-bearing: the tray copy holds an open handle on the log inside the directory that is
    /// about to be deleted, so it has to go first or the delete fails on the one file guaranteed to be open.
    /// </summary>
    public static void Run()
    {
        // WHO AM I, written before anything is attempted. This runs inside an uninstaller with no console,
        // no window and a Return="ignore" wrapper, so without this the only observable outcome is "the files
        // are still there" — which is true both when the code fails and when it succeeds against the wrong
        // user's profile. Three separate installer bugs here were each invisible for exactly that reason, and
        // each cost a full install-and-uninstall cycle to guess at. One line of identity turns the next one
        // into a lookup.
        Trace($"cleanup running as {Environment.UserName} on {Environment.MachineName}, "
            + $"appdata={Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}");

        Diagnose();

        CloseOtherInstances();
        ClearAutostart();
        DeleteUserData();
    }

    /// <summary>
    /// One-off identity and access dump. Exists because the two APIs this class depends on —
    /// <see cref="Directory.Exists"/> and <c>OpenSubKey</c> — both report "not there" when the real answer is
    /// "not allowed", so a permissions problem is indistinguishable from a clean no-op. Both reported nothing
    /// present while the directory held 1,528 files.
    /// </summary>
    private static void Diagnose()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();

            Trace($"  identity={identity.Name} system={identity.IsSystem} "
                + $"impersonation={identity.ImpersonationLevel} token={identity.Token}");
            Trace($"  USERPROFILE={Environment.GetEnvironmentVariable("USERPROFILE")}");
            Trace($"  LOCALAPPDATA={Environment.GetEnvironmentVariable("LOCALAPPDATA")}");
            Trace($"  HKCU resolves to {Microsoft.Win32.Registry.CurrentUser.Name}");

            using var run = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: false);

            Trace(run is null
                ? "  Run KEY ITSELF could not be opened (access denied or wrong hive)"
                : $"  Run key opened, {run.GetValueNames().Length} values, Loadstar={run.GetValue("Loadstar") ?? "(none)"}");

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Loadstar");

            // GetFileSystemEntries THROWS where Exists silently returns false, which is the entire point.
            try
            {
                Trace($"  enumerating {dir}: {Directory.GetFileSystemEntries(dir).Length} entries");
            }
            catch (Exception ex)
            {
                Trace($"  enumerating {dir} threw {ex.GetType().Name}: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Trace($"  diagnose failed: {ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>
    /// Appends one line to a machine-wide log, which is the only place both a normal user and LocalSystem can
    /// reliably write. Deliberately NOT the app's own log directory: that is one of the things being deleted,
    /// and a diagnostic that disappears with its subject is no diagnostic at all.
    /// </summary>
    private static void Trace(string line)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Loadstar-uninstall.log");

            File.AppendAllText(path, $"{DateTimeOffset.Now:O}  {line}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Diagnostics must never be the reason an uninstall misbehaves.
        }
    }

    /// <summary>
    /// Ends any other running Loadstar, so the uninstall is not deleting files out from under a live process.
    ///
    /// <para><b>Why this is here rather than left to the installer's CloseApplication.</b> The MSI does carry
    /// <c>util:CloseApplication</c>, and the extension schedules it before <c>InstallFiles</c> — correct for
    /// an upgrade, where the files are being overwritten. On an UNINSTALL the deletions happen in
    /// <c>RemoveFiles</c>, which the built MSI sequences at 3500, while CloseApplications lands at 3999. So
    /// the app would be closed five hundred steps after its own executable was deleted, and the files-in-use
    /// prompt this was supposed to prevent would appear anyway. Verified by reading InstallExecuteSequence
    /// out of the built package, not assumed.</para>
    ///
    /// <para>This runs from an immediate custom action sequenced at 3499, and the immediate pass completes
    /// in full before any file operation in the script executes — so by the time anything is deleted, this
    /// has already finished. Loadstar is tray-resident and therefore running at essentially every uninstall,
    /// which is what makes it worth being careful about.</para>
    /// </summary>
    private static void CloseOtherInstances()
    {
        Process[] others;

        try
        {
            others = [.. Process.GetProcessesByName("Loadstar")
                .Where(p => p.Id != Environment.ProcessId)];
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return;
        }

        foreach (var other in others)
        {
            try
            {
                // Asked first. The tray app has no visible window so this often does nothing, but when it
                // does the app shuts down through its own path rather than being cut off mid-write.
                other.CloseMainWindow();

                if (!other.WaitForExit(milliseconds: 3000))
                {
                    // Then not asked. Nothing is lost: settings are written when a dialog is accepted, not
                    // at exit, and the alternative is a half-removed install that needs a reboot.
                    other.Kill();
                    other.WaitForExit(milliseconds: 2000);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception
                or NotSupportedException or SystemException)
            {
                // Already gone, or another user's copy this account may not touch. Either way the uninstall
                // continues — Windows Installer's own files-in-use handling is the fallback.
            }
            finally
            {
                other.Dispose();
            }
        }
    }

    private static void ClearAutostart()
    {
        try
        {
            var before = new RunKeyStartupKey().Read();

            new RunKeyStartupKey().Delete();

            Trace($"autostart: was {(before is null ? "absent" : "'" + before + "'")}, "
                + $"now {(new RunKeyStartupKey().Read() is null ? "absent" : "STILL PRESENT")}");
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // A stale Run value pointing at a deleted executable is skipped by Windows, so failing here
            // costs the user an inert line in Task Manager's Startup tab and nothing else.
            Trace($"autostart: FAILED {ex.GetType().Name} {ex.Message}");
        }
    }

    private static void DeleteUserData()
    {
        // Computed here rather than through SettingsStore, whose constructor creates the directory — asking
        // it for the path would recreate the very folder this is about to remove, and on a machine where the
        // user never ran the app it would leave a new empty folder as the parting gift of an uninstall.
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Loadstar");

        try
        {
            var existed = Directory.Exists(directory);

            if (existed)
            {
                Directory.Delete(directory, recursive: true);
            }

            Trace($"data dir {directory}: existed={existed}, now exists={Directory.Exists(directory)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Most likely the log file still being held, if a copy outlived CloseOtherInstances above or a
            // second one started in between. The residue is then a log and a settings file, and neither is
            // worth failing an uninstall over.
            Trace($"data dir: FAILED {ex.GetType().Name} {ex.Message}");
        }
    }
}
