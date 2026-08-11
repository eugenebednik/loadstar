namespace Loadstar.Core.Update;

/// <summary>
/// Version comparison for this project's numbering, which is not what <see cref="Version"/> assumes.
///
/// <para><b>Only the first three components count.</b> An assembly reports <c>0.22.0.0</c> while a release
/// calls itself <c>0.22.0</c>, and <see cref="Version"/> treats those as different because one has a revision
/// of 0 and the other of -1. Comparing them directly would offer the current version as an update, forever.</para>
///
/// <para><b>And the third component is a build counter, not a patch level.</b> CI numbers rolling builds
/// <c>major.minor.run_number</c>, so a development build is deliberately AHEAD of the release before it —
/// 0.23.62 against 0.22.0. That is by design, per the scheme in Directory.Build.props, and it means an update
/// check must offer only strictly greater versions. Anything else would invite someone on a rolling build to
/// "update" downwards, which the installer would then refuse with a downgrade error, having already made them
/// download it.</para>
/// </summary>
public static class AppVersion
{
    /// <summary>
    /// Parses <c>major.minor.build</c>, tolerating a fourth component and a leading <c>v</c>.
    ///
    /// <para>Returns null rather than throwing, because the input is a remote file this app does not control.</para>
    /// </summary>
    public static (int Major, int Minor, int Build)? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();

        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        var parts = trimmed.Split('.', StringSplitOptions.TrimEntries);

        if (parts.Length < 3)
        {
            return null;
        }

        if (!int.TryParse(parts[0], out var major)
            || !int.TryParse(parts[1], out var minor)
            || !int.TryParse(parts[2], out var build))
        {
            return null;
        }

        return major < 0 || minor < 0 || build < 0 ? null : (major, minor, build);
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> is strictly newer than <paramref name="current"/>. False when
    /// either fails to parse — an unreadable version is not grounds for offering an install.
    /// </summary>
    public static bool IsNewer(string? candidate, string? current)
    {
        if (Parse(candidate) is not { } newer || Parse(current) is not { } mine)
        {
            return false;
        }

        return newer.CompareTo(mine) > 0;
    }
}
