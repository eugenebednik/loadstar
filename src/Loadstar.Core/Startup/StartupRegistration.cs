namespace Loadstar.Core.Startup;

/// <summary>
/// Whether Loadstar launches when the user signs in.
///
/// <para><b>The registry entry IS the setting — there is deliberately no copy in settings.json.</b>
/// Windows gives the user their own controls for this: Task Manager's Startup tab, Settings → Startup, and
/// any number of third-party tools, all of which can turn an entry off without telling the application. A
/// stored flag would then disagree with reality, and the checkbox would confidently show the wrong state
/// with no way for the user to tell which one was true. Reading the entry back means the checkbox cannot
/// lie, at the cost of one registry read when the dialog opens.</para>
///
/// <para><b>Per-user, so no elevation.</b> This writes under HKCU, which is the current user's own
/// preference about their own sign-in. It is not a machine-wide or system setting and needs no admin
/// rights; a tray app that demanded elevation to offer a startup checkbox would be doing something
/// wrong.</para>
///
/// <para>Nothing here throws. Registry access can be denied by group policy on a managed machine, and a
/// failed checkbox must report a failure, not take the settings dialog down with it.</para>
/// </summary>
public sealed class StartupRegistration
{
    private readonly IStartupKey _key;
    private readonly string? _executablePath;

    /// <param name="executablePath">
    /// The executable to launch. Null when the host cannot report one — under some single-file hosts
    /// <c>Environment.ProcessPath</c> is null — in which case this reports unsupported rather than
    /// registering a command that cannot work.
    /// </param>
    public StartupRegistration(IStartupKey key, string? executablePath)
    {
        ArgumentNullException.ThrowIfNull(key);

        _key = key;
        _executablePath = string.IsNullOrWhiteSpace(executablePath) ? null : executablePath;
    }

    /// <summary>False when there is no executable path to register, so the UI can disable the checkbox.</summary>
    public bool IsSupported => _executablePath is not null;

    /// <summary>
    /// The command that should be registered: the executable path, QUOTED.
    ///
    /// <para>The quotes are not cosmetic. The default install path contains a space — <c>C:\Program
    /// Files\Loadstar\Loadstar.exe</c> — and Windows parses an unquoted Run value up to the first space,
    /// so it would try to launch <c>C:\Program</c> and silently fail at every sign-in.</para>
    /// </summary>
    public string? DesiredCommand => _executablePath is null ? null : $"\"{_executablePath}\"";

    /// <summary>
    /// Whether Loadstar is registered to start, and registered as THIS executable.
    ///
    /// <para>An entry pointing somewhere else still counts as enabled: the user's intent is on record, and
    /// the path is a detail this class repairs rather than a reason to report the feature off. See
    /// <see cref="Synchronise"/>.</para>
    /// </summary>
    public bool IsEnabled()
    {
        try
        {
            return _key.Read() is not null;
        }
        catch
        {
            // Cannot read it, so cannot claim it is on.
            return false;
        }
    }

    /// <summary>Turns it on or off. Returns false when the change could not be made.</summary>
    public bool Set(bool enabled)
    {
        if (enabled && !IsSupported)
        {
            return false;
        }

        try
        {
            if (enabled)
            {
                _key.Write(DesiredCommand!);
            }
            else
            {
                _key.Delete();
            }

            return true;
        }
        catch
        {
            // Group policy can deny this outright on a managed machine.
            return false;
        }
    }

    /// <summary>
    /// Repairs a registered command that points at the wrong executable, and does nothing otherwise.
    ///
    /// <para><b>This is what makes the feature survive an upgrade.</b> The MSI can install to a different
    /// directory than the version that wrote the entry, and a user can simply move a portable build. The
    /// entry would then point at a path that no longer exists, and the failure mode is the worst kind:
    /// the checkbox still reads "on", nothing errors, and the app just never starts again. Rewriting the
    /// path whenever the running executable disagrees with the stored one costs a string comparison at
    /// launch.</para>
    ///
    /// <para>Deliberately does NOT enable anything. If the user has not asked for autostart there is no
    /// entry, and this leaves it that way.</para>
    /// </summary>
    /// <returns>True when an entry existed and was rewritten.</returns>
    public bool Synchronise()
    {
        if (!IsSupported)
        {
            return false;
        }

        try
        {
            var current = _key.Read();

            if (current is null || Matches(current))
            {
                return false;
            }

            _key.Write(DesiredCommand!);

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Whether a stored command already points at this executable.
    ///
    /// <para>Tolerant on purpose. The value may or may not be quoted depending on what wrote it, Windows
    /// paths are case-insensitive, and separators can be mixed — none of those differences mean the entry
    /// is stale, and treating them as stale would rewrite the value on every single launch.</para>
    /// </summary>
    private bool Matches(string stored)
    {
        var path = stored.Trim().Trim('"');

        return string.Equals(
            Normalise(path),
            Normalise(_executablePath!),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalise(string path) => path.Replace('/', '\\').TrimEnd('\\');
}

/// <summary>
/// The stored autostart command, abstracted so <see cref="StartupRegistration"/> can be tested.
///
/// <para>The registry implementation lives in the Windows-only app project; this keeps the decisions —
/// quoting, staleness, idempotence — in code that runs anywhere and can be exercised without touching a
/// real machine's sign-in behaviour.</para>
/// </summary>
public interface IStartupKey
{
    /// <summary>The registered command, or null when there is no entry.</summary>
    string? Read();

    void Write(string command);

    /// <summary>Removes the entry. Must succeed silently when there is nothing to remove.</summary>
    void Delete();
}
