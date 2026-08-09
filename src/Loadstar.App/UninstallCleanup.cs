using System.Diagnostics;

namespace Loadstar.App;

/// <summary>
/// Ends the running tray copy, then removes the two things an MSI uninstall cannot reach by itself: the
/// per-user autostart entry, and the per-user data directory holding settings, logs and the encrypted API key.
///
/// <para><b>Why the app does this rather than the installer.</b> Both live in the user's own hive and
/// profile, and the package is <c>perMachine</c> — so its uninstall runs elevated in whichever account
/// launched it, and <c>HKCU</c> / <c>LocalApplicationData</c> resolved from there are the wrong hive and
/// the wrong profile if that is not the person who used the app. A <c>RemoveRegistryValue</c> or
/// <c>RemoveFolder</c> in the package would silently miss, and an HKCU key in a perMachine package is the
/// ICE38 / repair-loop trap the installer deliberately avoids. Running the app itself, impersonated, is the
/// one way to land in the right place. See <see cref="RunKeyStartupKey"/>, whose documentation previously
/// concluded this was simply not fixable — it is, from this side of the boundary rather than that one.</para>
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
        CloseOtherInstances();
        ClearAutostart();
        DeleteUserData();
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
            new RunKeyStartupKey().Delete();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // A stale Run value pointing at a deleted executable is skipped by Windows, so failing here
            // costs the user an inert line in Task Manager's Startup tab and nothing else.
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
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Most likely the log file still being held, if a copy outlived CloseOtherInstances above or a
            // second one started in between. The residue is then a log and a settings file, and neither is
            // worth failing an uninstall over.
        }
    }
}
