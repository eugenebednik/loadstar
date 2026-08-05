using System.Text.Json;

namespace Loadstar.Core.Configuration;

/// <summary>
/// Loads and saves <see cref="LoadstarSettings"/> as JSON under the user's local app data.
///
/// <para>The one behaviour worth stating: a settings file that fails to parse is treated as
/// <b>absent</b>, which means <c>CaptureConsentGiven</c> comes back false. Corruption must never be
/// able to read as consent — the safe direction is to ask again, not to assume permission that may
/// never have been given.</para>
/// </summary>
public sealed class SettingsStore
{
    private readonly string _path;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

        // Case-insensitive matching, because this file gets hand-edited and by tools that do not
        // share the camelCase convention. A PascalCase edit otherwise matches nothing, and since
        // unmatched properties are not an error, EVERY setting silently reverts to its default —
        // which looks exactly like the app losing your configuration for no reason. It cost a long
        // debugging detour once already.
        PropertyNameCaseInsensitive = true,
    };

    public SettingsStore(string? directory = null)
    {
        Directory = directory ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Loadstar");

        System.IO.Directory.CreateDirectory(Directory);
        _path = System.IO.Path.Combine(Directory, "settings.json");
    }

    /// <summary>Where settings and the encrypted credential blob live.</summary>
    public string Directory { get; }

    /// <summary>Full path of the settings file. Named to avoid shadowing <see cref="System.IO.Path"/>.</summary>
    public string FilePath => _path;

    public LoadstarSettings Load()
    {
        if (!File.Exists(_path))
        {
            return new LoadstarSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<LoadstarSettings>(File.ReadAllText(_path), Options)
                ?? new LoadstarSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new LoadstarSettings();
        }
    }

    public void Save(LoadstarSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Write-then-move, so an interrupted save cannot leave a half-written file that would
        // silently reset consent on the next start.
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, _path, overwrite: true);
    }
}
