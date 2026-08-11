using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;

using Loadstar.Core.Update;

namespace Loadstar.App;

/// <summary>
/// Checks for a newer release, and installs it when the player says so.
///
/// <para><b>It cannot be silent, and pretending otherwise would be the wrong design.</b> The package is
/// <c>perMachine</c>, so installing writes to Program Files and needs elevation — which means a UAC prompt
/// the player has to accept. A background service running as SYSTEM could avoid that, and would be a
/// permanently-elevated process on the machine in exchange for saving one click on an app that updates every
/// few days. So: detect and offer, never install unasked.</para>
///
/// <para><b>Upgrading over a running copy works</b> because the MSI carries <c>MajorUpgrade</c> and
/// <c>util:CloseApplication</c>, and the extension schedules the close before <c>InstallFiles</c> — which is
/// exactly the right place for an upgrade, whatever its shortcomings on uninstall. So the app does not need to
/// exit first; msiexec closes it.</para>
///
/// <para><b>The download is verified before it is executed.</b> TLS authenticates github.com, but a truncated
/// download is an everyday event and a half-written installer is precisely the kind of thing that fails
/// partway through changing the machine. The digest comes from the release itself, published by CI alongside
/// the installers.</para>
/// </summary>
internal sealed class UpdateService
{
    /// <summary>
    /// Always the newest release GitHub considers latest, so this URL never needs updating and cannot point at
    /// a version the release page disagrees with.
    /// </summary>
    private const string ManifestUrl =
        "https://github.com/eugenebednik/loadstar/releases/latest/download/latest.json";

    private const string DownloadBase =
        "https://github.com/eugenebednik/loadstar/releases/latest/download/";

    private readonly Func<HttpClient> _clientFactory;

    public UpdateService(Func<HttpClient>? clientFactory = null) =>
        _clientFactory = clientFactory ?? (() => new HttpClient { Timeout = TimeSpan.FromMinutes(5) });

    /// <summary>The version this build reports, as <c>major.minor.build</c>.</summary>
    public static string CurrentVersion
    {
        get
        {
            var version = typeof(UpdateService).Assembly.GetName().Version;

            return version is null
                ? "0.0.0"
                : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    /// <summary>
    /// Looks for a newer release. Returns null when there is none, or when anything went wrong.
    ///
    /// <para>A failed check is silent by design. This runs at startup while somebody is opening a game; a
    /// dialog because GitHub was briefly unreachable would be worse than not checking at all.</para>
    /// </summary>
    public async Task<UpdateAvailable?> CheckAsync(string languageCode, CancellationToken cancel)
    {
        try
        {
            using var client = _clientFactory();

            client.DefaultRequestHeaders.UserAgent.ParseAdd($"Loadstar/{CurrentVersion} (update check)");

            var json = await client.GetStringAsync(ManifestUrl, cancel);
            var manifest = UpdateManifest.Parse(json);

            if (manifest is null)
            {
                Core.Diagnostics.Log.Info("Update: manifest missing or unreadable; nothing offered.");

                return null;
            }

            if (!AppVersion.IsNewer(manifest.Version, CurrentVersion))
            {
                Core.Diagnostics.Log.Info(
                    $"Update: running {CurrentVersion}, latest release is {manifest.Version}. Nothing to do.");

                return null;
            }

            var installer = manifest.For(languageCode);

            if (installer?.File is null || string.IsNullOrWhiteSpace(installer.Sha256))
            {
                // A manifest that names a version but no verifiable installer is not actionable. Offering it
                // would mean downloading something with nothing to check it against.
                Core.Diagnostics.Log.Warn(
                    $"Update: {manifest.Version} is newer but carries no verifiable installer for '{languageCode}'.");

                return null;
            }

            Core.Diagnostics.Log.Info(
                $"Update: {manifest.Version} available (running {CurrentVersion}), installer {installer.File}.");

            return new UpdateAvailable(manifest.Version!, installer);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or OperationCanceledException or IOException)
        {
            Core.Diagnostics.Log.Info($"Update: check failed ({ex.GetType().Name}); staying quiet.");

            return null;
        }
    }

    /// <summary>
    /// Downloads the installer and verifies its digest. Returns the path, or null on any failure.
    ///
    /// <para>Written to a fresh temp file per attempt rather than a fixed name, so a previous partial download
    /// can never be mistaken for this one.</para>
    /// </summary>
    public async Task<string?> DownloadAsync(UpdateAvailable update, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(update);

        var directory = Path.Combine(Path.GetTempPath(), "Loadstar-update-" + Guid.NewGuid().ToString("n")[..8]);
        var path = Path.Combine(directory, update.Installer.File!);

        try
        {
            Directory.CreateDirectory(directory);

            using var client = _clientFactory();

            client.DefaultRequestHeaders.UserAgent.ParseAdd($"Loadstar/{CurrentVersion} (update)");

            using (var response = await client.GetAsync(
                DownloadBase + update.Installer.File,
                HttpCompletionOption.ResponseHeadersRead,
                cancel))
            {
                response.EnsureSuccessStatusCode();

                await using var source = await response.Content.ReadAsStreamAsync(cancel);
                await using var destination = File.Create(path);

                await source.CopyToAsync(destination, cancel);
            }

            var actual = await ComputeSha256Async(path, cancel);

            if (!actual.Equals(update.Installer.Sha256!.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                // Deleted rather than left on disk: a file that failed verification must not be sitting
                // somewhere an impatient human could double-click it.
                Core.Diagnostics.Log.Error(
                    $"Update: digest mismatch. Expected {update.Installer.Sha256}, got {actual}. Discarded.",
                    null);

                TryDelete(directory);

                return null;
            }

            Core.Diagnostics.Log.Info($"Update: downloaded and verified {update.Installer.File}.");

            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or OperationCanceledException or IOException or UnauthorizedAccessException)
        {
            Core.Diagnostics.Log.Warn($"Update: download failed ({ex.GetType().Name}).");

            TryDelete(directory);

            return null;
        }
    }

    /// <summary>
    /// Hands the verified installer to msiexec and returns whether it started.
    ///
    /// <para><b>Not silent, and not detached from the user.</b> A visible install is what makes the UAC prompt
    /// make sense: the player asked for this a moment ago, so an elevation request with the installer's own
    /// window behind it reads as the thing they asked for rather than as something surprising.</para>
    ///
    /// <para>This app is not asked to exit first. The MSI closes it as part of the upgrade, and doing it here
    /// as well would mean quitting before knowing whether the player accepted the elevation prompt — leaving
    /// them with no tray icon and no installer.</para>
    /// </summary>
    public static bool LaunchInstaller(string installerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);

        try
        {
            var started = Process.Start(new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                // /i to install, /qb for a basic UI: progress and errors are visible, but none of the
                // welcome, licence and folder dialogs a first install needs and an upgrade does not.
                Arguments = $"/i \"{installerPath}\" /qb",
                UseShellExecute = true,
            });

            Core.Diagnostics.Log.Info($"Update: launched msiexec for {Path.GetFileName(installerPath)}.");

            return started is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            // The commonest cause is the player declining the elevation prompt, which is a choice rather
            // than a fault.
            Core.Diagnostics.Log.Warn($"Update: could not start the installer ({ex.GetType().Name}).");

            return false;
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancel)
    {
        await using var stream = File.OpenRead(path);

        var hash = await SHA256.HashDataAsync(stream, cancel);

        return Convert.ToHexString(hash);
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is untidy, not harmful.
        }
    }
}

/// <summary>A newer release, with the installer chosen for the player's language.</summary>
internal sealed record UpdateAvailable(string Version, UpdateInstaller Installer);
