namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// The 45 weapon-pair "classes". Throne and Liberty has no class system — you equip two weapons and
/// the pair has a name — so this is the only way to say what a character *is*.
///
/// <para><b>Why it matters more than a cosmetic label.</b> The app reads the player's two weapons off
/// the character sheet, which means it can identify their class without being told, and from there
/// look up what the community actually plays. That is the difference between requiring the player to
/// paste a build URL and just working.</para>
///
/// <para><b>Captured from questlog's own class filter, 2026-08-06</b>, by reading the URL slug each
/// filter option produces. Not inferred, and not taken from community guides — those are stale on the
/// two newest weapons. Orb and Gauntlet between them account for 17 of the 45 pairs, and published
/// lists still show 21 or 28 classes because they predate one or both.</para>
///
/// <para>Verified three ways: 45 names map to 45 distinct pairs, which is exactly C(10,2) for the ten
/// weapons with no pair missing and none repeated; every weapon appears in exactly 9 classes; and the
/// five the product owner named independently (Oracle, Seeker, Gladiator, Ravager, Bulwark) all
/// match.</para>
/// </summary>
public static class TlClasses
{
    /// <summary>
    /// The ten weapons, by the id questlog and the app both use. <c>sword</c> is Sword and Shield,
    /// <c>sword2h</c> is the Greatsword, <c>wand</c> is Wand and Tome, <c>gauntlet</c> is Gauntlets.
    /// </summary>
    public static readonly IReadOnlyList<string> Weapons =
    [
        "bow", "crossbow", "dagger", "gauntlet", "orb", "spear", "staff", "sword", "sword2h", "wand",
    ];

    /// <summary>
    /// Class name keyed by the weapon pair, order-independent.
    ///
    /// <para>Order-independent deliberately: which weapon is main hand and which is off hand is the
    /// player's choice and questlog's slugs are not consistent about it (<c>sword2h-sword</c> for
    /// Crusader, <c>sword-bow</c> for Warden). The class is the same either way, so a lookup that
    /// depended on the order would silently fail to identify half of them.</para>
    /// </summary>
    private static readonly Dictionary<string, string> ByPair = Build(new[]
    {
        ("Bastion", "gauntlet", "sword"),
        ("Battleweaver", "crossbow", "staff"),
        ("Berserker", "sword", "dagger"),
        ("Brawler", "gauntlet", "dagger"),
        ("Bulwark", "gauntlet", "orb"),
        ("Cavalier", "spear", "crossbow"),
        ("Channeler", "gauntlet", "staff"),
        ("Crucifix", "orb", "crossbow"),
        ("Crusader", "sword2h", "sword"),
        ("Darkblighter", "dagger", "wand"),
        ("Disciple", "sword", "staff"),
        ("Enigma", "orb", "staff"),
        ("Eradicator", "spear", "staff"),
        ("Fury", "crossbow", "wand"),
        ("Gladiator", "spear", "sword2h"),
        ("Guardian", "orb", "sword"),
        ("Impaler", "spear", "bow"),
        ("Infiltrator", "bow", "dagger"),
        ("Invocator", "staff", "wand"),
        ("Juggernaut", "gauntlet", "sword2h"),
        ("Justicar", "orb", "sword2h"),
        ("Liberator", "bow", "staff"),
        ("Lunarch", "orb", "dagger"),
        ("Marauder", "gauntlet", "crossbow"),
        ("Mystic", "gauntlet", "wand"),
        ("Oracle", "orb", "wand"),
        ("Outrider", "crossbow", "sword2h"),
        ("Paladin", "sword2h", "wand"),
        ("Polaris", "orb", "spear"),
        ("Raider", "crossbow", "sword"),
        ("Ranger", "bow", "sword2h"),
        ("Ravager", "sword2h", "dagger"),
        ("Scorpion", "crossbow", "dagger"),
        ("Scout", "bow", "crossbow"),
        ("Scryer", "orb", "bow"),
        ("Seeker", "bow", "wand"),
        ("Sentinel", "staff", "sword2h"),
        ("Shadowdancer", "spear", "dagger"),
        ("Skirmisher", "gauntlet", "spear"),
        ("Spellblade", "staff", "dagger"),
        ("Steelheart", "spear", "sword"),
        ("Strider", "gauntlet", "bow"),
        ("Templar", "sword", "wand"),
        ("Voidlance", "spear", "wand"),
        ("Warden", "sword", "bow"),
    });

    private static Dictionary<string, string> Build((string Name, string A, string B)[] rows)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, a, b) in rows)
        {
            map[Key(a, b)] = name;
        }

        return map;
    }

    /// <summary>Canonical order-independent key for a weapon pair.</summary>
    private static string Key(string a, string b)
    {
        var first = a.Trim().ToLowerInvariant();
        var second = b.Trim().ToLowerInvariant();

        return string.CompareOrdinal(first, second) <= 0 ? $"{first}+{second}" : $"{second}+{first}";
    }

    /// <summary>Every class name, alphabetically.</summary>
    public static IReadOnlyList<string> All => ByPair.Values.OrderBy(n => n, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// The class for a weapon pair, or null when the pair is not one of the 45.
    ///
    /// <para>Returns null rather than a guess. An unrecognised pair means either a misread weapon or a
    /// weapon added after this table was captured, and inventing a class name would be the same
    /// failure as inventing a boss name — the player cannot tell it is wrong.</para>
    /// </summary>
    public static string? Name(string? weaponA, string? weaponB)
    {
        if (string.IsNullOrWhiteSpace(weaponA) || string.IsNullOrWhiteSpace(weaponB))
        {
            return null;
        }

        return ByPair.TryGetValue(Key(weaponA, weaponB), out var name) ? name : null;
    }

    /// <summary>The class for a weapon list, which is how questlog reports a build's weapons.</summary>
    public static string? Name(IReadOnlyList<string>? weapons) =>
        weapons is { Count: 2 } ? Name(weapons[0], weapons[1]) : null;

    /// <summary>
    /// The two weapons a class uses, or null for an unknown name. Alphabetical, since main and off
    /// hand are the player's choice rather than a property of the class.
    /// </summary>
    public static IReadOnlyList<string>? WeaponsFor(string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            return null;
        }

        foreach (var (pair, name) in ByPair)
        {
            if (name.Equals(className.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return pair.Split('+');
            }
        }

        return null;
    }

    /// <summary>
    /// A label for the player, naming the class and the weapons it is made of.
    ///
    /// <para>Both halves on purpose. The class name alone means nothing to a player who has never
    /// looked one up, and the weapons alone do not connect to anything they can search for.</para>
    /// </summary>
    public static string Describe(string? weaponA, string? weaponB)
    {
        var pretty = $"{Pretty(weaponA)} + {Pretty(weaponB)}";
        var name = Name(weaponA, weaponB);

        return name is null ? pretty : $"{name} ({pretty})";
    }

    /// <summary>Weapon ids as the game names them, since the ids are not what any player would say.</summary>
    public static string Pretty(string? weapon) => weapon?.Trim().ToLowerInvariant() switch
    {
        "sword" => "Sword and Shield",
        "sword2h" => "Greatsword",
        "wand" => "Wand and Tome",
        "bow" => "Longbow",
        "dagger" => "Daggers",
        "crossbow" => "Crossbows",
        "gauntlet" => "Gauntlets",
        "staff" => "Staff",
        "spear" => "Spear",
        "orb" => "Orb",
        null or "" => "unknown",
        var other => other,
    };
}
