using Loadstar.Games.ThroneAndLiberty;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Weapon identification is the read where being wrong does the most damage and is least likely to be
/// noticed: a wrong pair names a different class, and every recommendation afterwards is confidently
/// aimed at a character the player is not playing. Nothing downstream contradicts it.
///
/// <para>So these tests are mostly about what must be REJECTED.</para>
/// </summary>
public sealed class WeaponReadingTests
{
    private static string Reply(string weapons, string? source = null) =>
        $"{{\"headline\":\"x\",\"weapons\":{weapons}"
        + (source is null ? string.Empty : $",\"weaponsSource\":\"{source}\"")
        + "}";

    [Fact]
    public void AValidPairIsReadWithItsClassAndSource()
    {
        var reading = TlObservationParser.ParseWeapons(Reply("[\"orb\",\"wand\"]", "tooltip"));

        Assert.NotNull(reading);
        Assert.Equal(["orb", "wand"], reading.Weapons);
        Assert.Equal("Oracle", reading.ClassName);
        Assert.True(reading.IsTextRead);
    }

    /// <summary>
    /// A pair that is not one of the 45 is rejected outright. This is the guard against a hallucinated
    /// weapon name becoming a stored setting — validating against the real class table rather than just
    /// counting two entries.
    /// </summary>
    [Theory]
    [InlineData("[\"orb\",\"lute\"]")]           // invented weapon
    [InlineData("[\"sword\",\"sword\"]")]        // same weapon twice is not a pair
    [InlineData("[\"bow\"]")]                    // one weapon identifies nothing
    [InlineData("[\"bow\",\"wand\",\"orb\"]")]   // three
    [InlineData("[]")]
    [InlineData("[\"\",\"  \"]")]
    [InlineData("[123,456]")]
    public void ImpossiblePairsAreRejected(string weapons)
    {
        Assert.Null(TlObservationParser.ParseWeapons(Reply(weapons)));
    }

    [Fact]
    public void MissingOrMalformedRepliesYieldNothing()
    {
        Assert.Null(TlObservationParser.ParseWeapons("{\"headline\":\"no weapons here\"}"));
        Assert.Null(TlObservationParser.ParseWeapons("not json at all"));
        Assert.Null(TlObservationParser.ParseWeapons("{\"weapons\":\"orb and wand\"}"));
    }

    /// <summary>
    /// The distinction the whole design rests on: text reads are trusted, icon reads are not. An
    /// UNSTATED source counts as an icon read, so a model that omits the field gets the careful path
    /// rather than the trusting one.
    /// </summary>
    [Theory]
    [InlineData("tooltip", true)]
    [InlineData("mastery", true)]
    [InlineData("skills", true)]
    [InlineData("icon", false)]
    [InlineData("guess", false)]
    [InlineData(null, false)]
    public void OnlyTextSourcesCountAsATextRead(string? source, bool expected)
    {
        var reading = TlObservationParser.ParseWeapons(Reply("[\"orb\",\"wand\"]", source));

        Assert.NotNull(reading);
        Assert.Equal(expected, reading.IsTextRead);
    }

    /// <summary>
    /// Pair comparison is by class, so main/off-hand order does not read as a different character.
    /// Without this, a model reporting the same weapons in the other order would look like a change and
    /// reset the corroboration count forever.
    /// </summary>
    [Fact]
    public void PairComparisonIgnoresOrder()
    {
        var reading = TlObservationParser.ParseWeapons(Reply("[\"orb\",\"wand\"]", "icon"));

        Assert.NotNull(reading);
        Assert.True(reading.SamePairAs(["wand", "orb"]));
        Assert.True(reading.SamePairAs(["orb", "wand"]));
        Assert.False(reading.SamePairAs(["bow", "wand"]));
        Assert.False(reading.SamePairAs(["orb"]));
        Assert.False(reading.SamePairAs(null));
    }

    /// <summary>
    /// Case and whitespace are normalised, because these come from a language model rather than a form.
    /// </summary>
    [Fact]
    public void SourceAndWeaponsAreNormalised()
    {
        var reading = TlObservationParser.ParseWeapons(Reply("[\" Orb \",\"WAND\"]", "ToolTip"));

        Assert.NotNull(reading);
        Assert.Equal(["orb", "wand"], reading.Weapons);
        Assert.True(reading.IsTextRead);
    }

    /// <summary>
    /// The stat observations and the weapon read are parsed independently, so one being absent must not
    /// take the other with it.
    /// </summary>
    [Fact]
    public void WeaponsAndObservedStatsDoNotDependOnEachOther()
    {
        const string statsOnly = """
            {"headline":"x","observedStats":[{"stat":"Wisdom","total":96,"base":30}]}
            """;

        Assert.Null(TlObservationParser.ParseWeapons(statsOnly));
        Assert.Single(TlObservationParser.Parse(statsOnly));

        var weaponsOnly = Reply("[\"spear\",\"sword2h\"]", "mastery");

        Assert.Equal("Gladiator", TlObservationParser.ParseWeapons(weaponsOnly)!.ClassName);
        Assert.Empty(TlObservationParser.Parse(weaponsOnly));
    }
}
