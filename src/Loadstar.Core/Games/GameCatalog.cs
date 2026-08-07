namespace Loadstar.Core.Games;

/// <summary>
/// The games this build can advise on, for the picker shown when the app loads.
///
/// <para><b>Registered rather than discovered.</b> Core cannot see the game modules — they depend on it,
/// not the other way round — so the composition root hands them in at startup. That is also why this is
/// not reflection over loaded assemblies: an explicit registration list is one place to read to know
/// what ships, and it cannot silently pick up something half-built.</para>
/// </summary>
public sealed class GameCatalog
{
    private readonly Dictionary<string, IGameModule> _modules;

    public GameCatalog(params IGameModule[] modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        if (modules.Length == 0)
        {
            throw new ArgumentException("A catalogue with no games cannot advise on anything.", nameof(modules));
        }

        var duplicate = modules.GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            // Ids are the settings key, so a collision would mean the stored game silently resolves to
            // whichever module happened to register second.
            throw new ArgumentException($"Two modules share the id '{duplicate.Key}'.", nameof(modules));
        }

        _modules = modules.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        All = modules.OrderBy(m => m.DisplayName, StringComparer.CurrentCulture).ToArray();
    }

    /// <summary>Every registered game, by display name, which is the order a picker wants.</summary>
    public IReadOnlyList<IGameModule> All { get; }

    /// <summary>
    /// The module for a stored id, or null when it is unknown.
    ///
    /// <para>Null rather than a fallback: a settings file naming a game this build does not have is
    /// worth telling the player about, not papering over by advising on a different game. Silently
    /// switching would produce confidently wrong advice about a game they are not playing — the same
    /// failure the Russian-client detection exists to prevent.</para>
    /// </summary>
    public IGameModule? Find(string? gameId) =>
        !string.IsNullOrWhiteSpace(gameId) && _modules.TryGetValue(gameId, out var module) ? module : null;

    /// <summary>
    /// What to select when nothing is stored yet. The only game while there is one; once there are
    /// several this becomes a first-run choice rather than a default.
    /// </summary>
    public IGameModule Default => All[0];

    /// <summary>True when the player genuinely has a choice, so the picker is worth showing at all.</summary>
    public bool HasChoice => All.Count > 1;
}
