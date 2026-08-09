namespace Loadstar.App;

/// <summary>
/// Removes the two things an MSI uninstall cannot reach by itself: the per-user autostart entry, and the
/// per-user data directory holding settings, logs and the encrypted API key.
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

    public static void Run()
    {
        ClearAutostart();
        DeleteUserData();
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
            // Most likely the log file still being held, which happens if the tray copy had not finished
            // exiting. The installer closes it first for exactly this reason; if that raced, the residue is
            // a log and a settings file, and neither is worth failing an uninstall over.
        }
    }
}
