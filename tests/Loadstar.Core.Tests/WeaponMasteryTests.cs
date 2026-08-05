using Loadstar.Games.ThroneAndLiberty;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Mastery thresholds, with the emphasis on the cross-weapon property.
///
/// <para>The mistake these guard against is reading 220 as the ceiling on the thresholds too, which
/// makes the 650 and 780 tiers look unreachable and turns "level a second weapon" into apparently
/// wasted effort. The thresholds are totals; the cap is per weapon.</para>
/// </summary>
public sealed class WeaponMasteryTests
{
    [Fact]
    public void TopSkillTierIsUnreachableWithThreeWeapons()
    {
        // Three maxed weapons total 660, short of 780. This is the derivation that makes
        // "level a fourth weapon" a prerequisite rather than a suggestion.
        Assert.Equal(660, 3 * WeaponMastery.MaxPointsPerWeapon);
        Assert.Equal(4, WeaponMastery.MinimumWeaponsFor(780));
    }

    [Theory]
    [InlineData(130, 1)]
    [InlineData(260, 2)]
    [InlineData(390, 2)]
    [InlineData(520, 3)]
    [InlineData(650, 3)]
    [InlineData(780, 4)]
    public void MinimumWeaponCountFollowsThePerWeaponCap(int threshold, int expectedWeapons)
    {
        Assert.Equal(expectedWeapons, WeaponMastery.MinimumWeaponsFor(threshold));
    }

    [Fact]
    public void SecondSlotAlreadyRequiresASecondWeapon()
    {
        // 260 exceeds a single weapon's 220 ceiling, so even the second slot cannot be reached
        // by a player who only ever levels their main.
        Assert.True(WeaponMastery.SlotThresholds[1] > WeaponMastery.MaxPointsPerWeapon);
        Assert.Equal(2, WeaponMastery.MinimumWeaponsFor(WeaponMastery.SlotThresholds[1]));
    }

    [Fact]
    public void NextMilestoneReportsDistanceAndPrerequisite()
    {
        var milestone = WeaponMastery.NextMilestone(500)!;

        Assert.Equal(520, milestone.Threshold);
        Assert.Equal(20, milestone.PointsRemaining);
        Assert.True(milestone.UnlocksSlot);
        Assert.Equal(3, milestone.MinimumWeapons);
        Assert.Contains("at least 3 weapons", milestone.Describe());
    }

    [Fact]
    public void SkillsBeyondTheLastSlotAreFlaggedAsAChoiceNotAGain()
    {
        // 650 and 780 grant skills but no slot. Presenting them as a straight upgrade would
        // overstate them — past 520 the player is picking between skills, not accumulating.
        var milestone = WeaponMastery.NextMilestone(600)!;

        Assert.Equal(650, milestone.Threshold);
        Assert.False(milestone.UnlocksSlot);
        Assert.Contains("choose between them", milestone.Describe());
    }

    [Fact]
    public void NoMilestoneRemainsOnceEverythingIsUnlocked()
    {
        Assert.Null(WeaponMastery.NextMilestone(780));
        Assert.Null(WeaponMastery.NextMilestone(1000));
    }

    [Fact]
    public void TwelveSkillsAcrossSixThresholds()
    {
        Assert.Equal(12, WeaponMastery.SkillThresholds.Count * WeaponMastery.SkillsPerThreshold);
        Assert.Equal(4, WeaponMastery.ActiveSkillSlots);
    }
}
