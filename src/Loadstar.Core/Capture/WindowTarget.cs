namespace Loadstar.Core.Capture;

/// <summary>
/// Which window Loadstar reads.
///
/// <para>This is stored configuration the user confirmed once, not something inferred fresh on
/// every capture — and that distinction came from a real mis-capture rather than caution. Searching
/// window titles for "THRONE AND LIBERTY" matched <b>Firefox</b>, because a questlog build page was
/// open and the tab title contained the game's name. The tool was one step from sending a browser
/// window to the AI provider. A wiki, a Discord channel or a YouTube video collides the same way,
/// and the more engaged the player the likelier the collision.</para>
///
/// <para>So process name is the primary key: <c>TL.exe</c> does not collide with a tab title. Title
/// matching remains available, because clients get renamed and a user may need it, but it never
/// silently outranks a process match.</para>
/// </summary>
public sealed record WindowTarget
{
    /// <summary>
    /// Process name, with or without <c>.exe</c>. The reliable key — matched exactly, so it cannot
    /// collide with whatever a browser happens to be displaying.
    /// </summary>
    public string? ProcessName { get; init; }

    /// <summary>Window title substring. A fallback, subordinate to <see cref="ProcessName"/>.</summary>
    public string? TitleMatch { get; init; }

    /// <summary>
    /// When true, a title-only match is allowed to select a process on
    /// <see cref="WindowTargeting.CommonlyMismatchedProcesses"/>. Off by default; the user has to
    /// mean it.
    /// </summary>
    public bool AllowAnyProcess { get; init; }

    public static WindowTarget ForProcess(string processName) =>
        new() { ProcessName = WindowTargeting.NormalizeProcessName(processName) };

    public static WindowTarget ForTitle(string titleMatch) =>
        new() { TitleMatch = titleMatch };

    /// <summary>Derives a target from a path to the game's executable.</summary>
    public static WindowTarget ForExecutable(string executablePath) =>
        ForProcess(Path.GetFileNameWithoutExtension(executablePath));

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ProcessName) || !string.IsNullOrWhiteSpace(TitleMatch);

    public override string ToString() => (ProcessName, TitleMatch) switch
    {
        ({ } p, { } t) when !string.IsNullOrWhiteSpace(t) => $"process \"{p}\" (title contains \"{t}\")",
        ({ } p, _) => $"process \"{p}\"",
        (_, { } t) => $"title containing \"{t}\"",
        _ => "(not configured)",
    };
}

public static class WindowTargeting
{
    /// <summary>
    /// Processes that routinely display a game's name without being the game.
    ///
    /// <para>Not a security boundary — it is a guard against the specific, observed accident of a
    /// title substring matching a browser tab. A user who genuinely wants to capture one of these
    /// sets <see cref="WindowTarget.AllowAnyProcess"/>.</para>
    /// </summary>
    public static readonly IReadOnlySet<string> CommonlyMismatchedProcesses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "firefox", "chrome", "msedge", "opera", "brave", "vivaldi", "iexplore", "safari",
            "discord", "slack", "obs64", "obs32", "obs", "steamwebhelper", "steam",
            "notepad", "code", "devenv", "explorer", "claude", "spotify",
        };

    /// <summary>Strips a trailing <c>.exe</c> and trims, so users can type either form.</summary>
    public static string NormalizeProcessName(string processName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);

        var trimmed = processName.Trim();

        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;
    }
}
