namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// Weapon Mastery thresholds, and the thing about them that inverts the obvious advice.
///
/// <para><b>Points cap at 220 per weapon, but the thresholds that unlock Mastery Skill Slots and
/// Mastery Skills are measured against the TOTAL across every weapon.</b> That is not a detail. It
/// means levelling a second or third weapon is real progress toward shared slots rather than a
/// distraction from your main — and since the top tier sits at 780, which is beyond what three
/// maxed weapons can reach, the final skills are simply unreachable without a fourth.</para>
///
/// <para>The economics reinforce it: a secondary weapon passively earns <b>50%</b> of the active
/// weapon's mastery XP just from playing, so off-weapon progress accrues at half rate for no extra
/// effort.</para>
///
/// <para>Provenance: thresholds and the 12-skill structure come from mastery guides, which are
/// usable here — unlike item guides, the Weapon Mastery system is largely unchanged since before
/// 4.0.0, and the only revision was raising the ceiling from 200 to 220. The 220 figure is
/// player-reported and not yet confirmed against a live client.</para>
/// </summary>
public static class WeaponMastery
{
    /// <summary>Maximum mastery points obtainable on a single weapon.</summary>
    public const int MaxPointsPerWeapon = 220;

    /// <summary>Share of the active weapon's mastery XP that a secondary weapon earns.</summary>
    public const double SecondaryWeaponXpShare = 0.50;

    /// <summary>Sollant charged each time a mastery passive is deactivated.</summary>
    public const int DeactivationCostSollant = 10_000;

    /// <summary>
    /// Total points, across all weapons, at which the four Mastery Skill Slots unlock.
    /// </summary>
    public static readonly IReadOnlyList<int> SlotThresholds = [130, 260, 390, 520];

    /// <summary>
    /// Total points at which Mastery Skills unlock, two at a time — twelve in all. Note this runs
    /// past the last slot threshold: 650 and 780 grant skills without granting somewhere to put
    /// them, so beyond 520 the constraint becomes choosing between skills, not collecting them.
    /// </summary>
    public static readonly IReadOnlyList<int> SkillThresholds = [130, 260, 390, 520, 650, 780];

    /// <summary>Mastery skills granted per threshold reached.</summary>
    public const int SkillsPerThreshold = 2;

    /// <summary>Mastery skills that may be active simultaneously.</summary>
    public const int ActiveSkillSlots = 4;

    /// <summary>
    /// Fewest weapons that must be levelled to reach <paramref name="totalPoints"/>, given the
    /// per-weapon ceiling.
    ///
    /// <para>This is the number that turns a threshold into an instruction. Reaching 780 needs four
    /// weapons because three maxed ones total only 660 — so "level a fourth weapon" is a concrete
    /// prerequisite, not a suggestion.</para>
    /// </summary>
    public static int MinimumWeaponsFor(int totalPoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalPoints);

        return (int)Math.Ceiling((double)totalPoints / MaxPointsPerWeapon);
    }

    /// <summary>
    /// The next slot or skill threshold above <paramref name="currentTotal"/>, with what it grants
    /// and what it requires. Null once everything is unlocked.
    /// </summary>
    public static MasteryMilestone? NextMilestone(int currentTotal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentTotal);

        var next = SkillThresholds.Cast<int?>().FirstOrDefault(t => currentTotal < t);

        if (next is not { } threshold)
        {
            return null;
        }

        return new MasteryMilestone
        {
            Threshold = threshold,
            PointsRemaining = threshold - currentTotal,
            UnlocksSlot = SlotThresholds.Contains(threshold),
            SkillsUnlocked = SkillsPerThreshold,
            MinimumWeapons = MinimumWeaponsFor(threshold),
        };
    }
}

public sealed record MasteryMilestone
{
    public required int Threshold { get; init; }
    public required int PointsRemaining { get; init; }

    /// <summary>True when this threshold also grants somewhere to equip a skill.</summary>
    public required bool UnlocksSlot { get; init; }

    public required int SkillsUnlocked { get; init; }

    /// <summary>Weapons that must be levelled to reach this at all, given the 220 per-weapon cap.</summary>
    public required int MinimumWeapons { get; init; }

    public string Describe()
    {
        var reward = UnlocksSlot
            ? $"a 4th-of-{WeaponMastery.ActiveSkillSlots} mastery skill slot plus {SkillsUnlocked} skills"
            : $"{SkillsUnlocked} more mastery skills (no new slot — you will have to choose between them)";

        var weapons = MinimumWeapons > 1
            ? $" Requires at least {MinimumWeapons} weapons levelled, since one caps at {WeaponMastery.MaxPointsPerWeapon}."
            : string.Empty;

        return $"{PointsRemaining} mastery points from {Threshold} total, which grants {reward}.{weapons}";
    }
}
