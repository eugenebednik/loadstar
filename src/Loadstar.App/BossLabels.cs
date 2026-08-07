using Loadstar.Games.ThroneAndLiberty;

namespace Loadstar.App;

/// <summary>
/// Turns a <see cref="BossSpawn"/> into text for the player, in their own language.
///
/// <para><b>Why this is here and not on BossSpawn.</b> <see cref="BossSpawn.GenericName"/> lives in the
/// game-knowledge assembly, which cannot see <see cref="Strings"/> — and should not. That layer holds
/// the schedule and the event types, which are DATA; how they read to a player is presentation. So the
/// event type travels down as an identifier and comes back up as a label here.</para>
///
/// <para><b>Boss names are deliberately NOT translated.</b> The prompt already carries this rule for the
/// advice text and it applies just as much to the overlay: a player reading "Ramux" has to find Ramux on
/// their own screen, in whichever of the game's seven languages their client runs. Translating a proper
/// noun makes it unfindable. Only the generic labels — Field Bosses, Arch Bosses, Siege, Guild Boss —
/// are ours to translate, because they are our words rather than the game's.</para>
/// </summary>
internal static class BossLabels
{
    /// <summary>
    /// What the overlay row should say: the boss names when the schedule has them, and a localised
    /// event label when it does not.
    /// </summary>
    public static string DisplayName(BossSpawn spawn)
    {
        ArgumentNullException.ThrowIfNull(spawn);

        if (spawn.Names.Count == 0)
        {
            return Generic(spawn);
        }

        var named = string.Join(", ", spawn.Names);

        // The guild marker matters more than it looks: a player without a guild cannot enter these at
        // all, so an unmarked row sends them across the map for a contest they are locked out of.
        return spawn.HasGuildContest ? $"{named} {Strings.Get("event.guildMarker")}" : named;
    }

    /// <summary>
    /// The localised event label.
    ///
    /// <para>An event type with no translation falls through to <see cref="BossSpawn.GenericName"/>,
    /// which humanises PascalCase. That is deliberate rather than a gap: the schedule is published as
    /// data and can introduce a type without an app release, so a new one has to render as readable
    /// English rather than as a missing-key placeholder. It will be untranslated until someone adds the
    /// key, which is the honest state.</para>
    /// </summary>
    private static string Generic(BossSpawn spawn) => spawn.EventType switch
    {
        // A guild slot names itself even with no boss identified, because "which boss" is the part
        // nobody has read while "guild only" decides whether to set out at all.
        "FieldBosses" => Strings.Get(spawn.HasGuildContest ? "event.guildBoss" : "event.fieldBosses"),
        "ArchBosses" => Strings.Get("event.archBosses"),
        "DynamicEvents" => Strings.Get("event.dynamicEvents"),
        "Siege" => Strings.Get("event.siege"),
        "TaxDelivery" => Strings.Get("event.taxDelivery"),
        _ => spawn.GenericName,
    };
}
