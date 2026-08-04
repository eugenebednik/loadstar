using Loadstar.Core.Model;

namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// A questlog character and every loadout saved under it.
///
/// This type exists because a questlog "build" URL does not resolve to one build. The reference
/// character has six, each with its own equipment, target attributes and weapon pair. Collapsing
/// them to a single target would silently pick one at random and then give advice against a
/// loadout the player isn't running — so the choice is surfaced instead of guessed.
/// </summary>
public sealed record CharacterBuilds
{
    public required string Slug { get; init; }
    public required string Name { get; init; }
    public int? Level { get; init; }

    /// <summary>Author's tags, e.g. <c>pve</c>, <c>healer</c>. These decide which stat axis to optimise.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    public required IReadOnlyList<TargetBuild> Builds { get; init; }

    /// <summary>
    /// True when the player must choose. Callers should prompt rather than defaulting.
    /// </summary>
    public bool RequiresSelection => Builds.Count > 1;

    /// <summary>
    /// True when the author tagged this as a healer build. Worth checking before applying
    /// damage-oriented reasoning — on a healer, Wisdom and Perception carry the build and the
    /// Max Damage stat families are largely beside the point.
    /// </summary>
    public bool IsHealer => Tags.Any(t => t.Equals("healer", StringComparison.OrdinalIgnoreCase));

    public bool IsPvp => Tags.Any(t => t.Equals("pvp", StringComparison.OrdinalIgnoreCase));
}
