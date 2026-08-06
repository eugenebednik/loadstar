using Loadstar.Games.ThroneAndLiberty;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// The class table is a captured dataset, so the tests that matter are the ones that would catch it
/// being captured wrong: completeness against the combinatorics, and the handful of mappings a person
/// confirmed independently.
/// </summary>
public sealed class TlClassesTests
{
    /// <summary>
    /// Ten weapons pair 45 ways, and all 45 are classes. This is the check that would have caught a
    /// truncated capture — community lists still publish 21 or 28 classes because they predate Spear,
    /// Orb or Gauntlets, and copying one of those would look complete.
    /// </summary>
    [Fact]
    public void EveryWeaponPairIsAClassAndNoPairIsRepeated()
    {
        var weapons = TlClasses.Weapons;
        Assert.Equal(10, weapons.Count);

        var names = new List<string>();

        for (var i = 0; i < weapons.Count; i++)
        {
            for (var j = i + 1; j < weapons.Count; j++)
            {
                var name = TlClasses.Name(weapons[i], weapons[j]);

                Assert.NotNull(name);
                names.Add(name);
            }
        }

        Assert.Equal(45, names.Count);
        Assert.Equal(45, names.Distinct().Count());
        Assert.Equal(45, TlClasses.All.Count);
    }

    /// <summary>Each weapon is half of exactly nine classes — it pairs with the other nine.</summary>
    [Fact]
    public void EachWeaponAppearsInExactlyNineClasses()
    {
        foreach (var weapon in TlClasses.Weapons)
        {
            var count = TlClasses.All.Count(name => TlClasses.WeaponsFor(name)!.Contains(weapon));

            Assert.Equal(9, count);
        }
    }

    /// <summary>
    /// The five confirmed by the product owner, independently of the capture. If the table were ever
    /// re-captured wrongly, these are the rows a person would notice.
    /// </summary>
    [Theory]
    [InlineData("orb", "wand", "Oracle")]
    [InlineData("bow", "wand", "Seeker")]
    [InlineData("spear", "sword2h", "Gladiator")]
    [InlineData("sword2h", "dagger", "Ravager")]
    [InlineData("gauntlet", "orb", "Bulwark")]
    public void ConfirmedClassesResolve(string a, string b, string expected)
    {
        Assert.Equal(expected, TlClasses.Name(a, b));

        // And the reverse order, because which weapon is main hand is the player's choice.
        Assert.Equal(expected, TlClasses.Name(b, a));
    }

    /// <summary>
    /// Order independence in general. questlog's own slugs are inconsistent about it —
    /// <c>sword2h-sword</c> for Crusader but <c>sword-bow</c> for Warden — so a lookup keyed on order
    /// would silently miss about half of them.
    /// </summary>
    [Fact]
    public void LookupIsOrderAndCaseIndependent()
    {
        Assert.Equal("Crusader", TlClasses.Name("sword2h", "sword"));
        Assert.Equal("Crusader", TlClasses.Name("sword", "sword2h"));
        Assert.Equal("Warden", TlClasses.Name("SWORD", "Bow"));
        Assert.Equal("Templar", TlClasses.Name([" wand ", "sword"]));
    }

    /// <summary>
    /// An unrecognised pair returns null rather than a guess. A weapon added after this table was
    /// captured must read as "unknown", not as a plausible wrong class — same rule as boss names.
    /// </summary>
    [Fact]
    public void UnknownPairsReturnNullRatherThanGuessing()
    {
        Assert.Null(TlClasses.Name("sword", "sword"));      // not a pair
        Assert.Null(TlClasses.Name("scythe", "wand"));      // hypothetical future weapon
        Assert.Null(TlClasses.Name("bow", null));
        Assert.Null(TlClasses.Name(["bow"]));               // only one weapon read
        Assert.Null(TlClasses.Name((IReadOnlyList<string>?)null));
        Assert.Null(TlClasses.WeaponsFor("Necromancer"));
    }

    /// <summary>Round trip: a class's weapons resolve back to that class.</summary>
    [Fact]
    public void WeaponsForRoundTripsThroughName()
    {
        foreach (var name in TlClasses.All)
        {
            var weapons = TlClasses.WeaponsFor(name);

            Assert.NotNull(weapons);
            Assert.Equal(2, weapons.Count);
            Assert.Equal(name, TlClasses.Name(weapons[0], weapons[1]));
        }
    }

    /// <summary>
    /// The player-facing label carries both the class and the weapons. The name alone means nothing to
    /// someone who has never looked one up; the weapons alone connect to nothing searchable.
    /// </summary>
    [Fact]
    public void DescribeNamesTheClassAndTheWeapons()
    {
        Assert.Equal("Oracle (Orb + Wand and Tome)", TlClasses.Describe("orb", "wand"));
        Assert.Equal("Gladiator (Spear + Greatsword)", TlClasses.Describe("spear", "sword2h"));

        // Unknown pair still describes the weapons, which is the useful half.
        Assert.Equal("Staff + Staff", TlClasses.Describe("staff", "staff"));
    }
}
