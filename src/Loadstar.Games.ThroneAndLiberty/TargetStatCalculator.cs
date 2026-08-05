using Loadstar.Core.Model;

namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// Works out what a target build's gear is actually worth, per stat.
///
/// <para>This exists so advice can say "your Endurance reads 1,431 against this build's 1,900"
/// instead of reasoning from imbalance alone. The three sources it sums — traits, runes and set
/// bonuses — were chosen because they are the ones that resolve <b>exactly</b>: trait values are
/// stated outright in the build payload, a rune's value is a lookup from its id, stat and level, and
/// set bonuses are a lookup from piece count.</para>
///
/// <para><b>What it deliberately does not include</b>, and why that matters more than what it does:
/// base attributes and their derived contributions, item base stats, heroic picks, resonance and
/// potential. Those need either a table that is not published or an item level the build never
/// states. A total that silently omitted them would be <em>low</em>, and a low target is worse than
/// no target — it tells the player they have arrived when they have not. So
/// <see cref="TargetStats.ExcludedSources"/> is part of the result, and callers are expected to show
/// it rather than present the number as complete.</para>
/// </summary>
public static class TargetStatCalculator
{
    /// <summary>
    /// Maps a rune id to its type. The id encodes it: <c>Weapon_Def_Rune_…</c> is a defense rune.
    /// <c>All</c> is the chaos wildcard, which counts as any type for synergy purposes.
    /// </summary>
    private static readonly (string Token, string Type)[] RuneTypes =
    [
        ("_Atk_", "attack"),
        ("_Def_", "defense"),
        ("_Ast_", "assist"),
        ("_All_", "chaos"),
    ];

    public static TargetStats Compute(TargetBuild build, TraitReference reference)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(reference);

        var totals = new Dictionary<string, StatTotal>(StringComparer.OrdinalIgnoreCase);
        var unresolved = new List<string>();

        void Add(string statId, int value, string source)
        {
            if (value == 0)
            {
                return;
            }

            if (!totals.TryGetValue(statId, out var existing))
            {
                existing = new StatTotal { StatId = statId };
            }

            var bySource = new Dictionary<string, int>(existing.BySource, StringComparer.OrdinalIgnoreCase);
            bySource[source] = bySource.GetValueOrDefault(source) + value;

            totals[statId] = existing with { Total = existing.Total + value, BySource = bySource };
        }

        foreach (var (slot, item) in build.Equipment)
        {
            // 1. Traits. Values are stated in the payload already — the build carries the resolved
            //    number for the pip level it targets, so there is nothing to look up.
            foreach (var (statId, value) in item.Traits)
            {
                Add(statId, value, "traits");
            }

            // 2. Runes. runeId + statId + level is an exact lookup.
            foreach (var rune in item.Runes)
            {
                if (ResolveRune(reference, rune) is { } value)
                {
                    Add(rune.StatId, value, "runes");
                }
                else
                {
                    unresolved.Add($"{slot}: rune {rune.RuneId} ({rune.StatId} at level {rune.Level})");
                }
            }
        }

        // 3. Rune synergies. Ordered, so this has to be computed per slot rather than counted.
        foreach (var synergy in ResolveSynergies(build, reference))
        {
            foreach (var (statId, value) in synergy.Stats)
            {
                Add(statId, value, "rune synergy");
            }
        }

        // 4. Set bonuses, which need the piece counts first.
        var sets = ResolveSets(build, reference);

        foreach (var set in sets)
        {
            foreach (var bonus in set.Active)
            {
                foreach (var (statId, value) in bonus.Stats)
                {
                    Add(statId, value, "set bonus");
                }
            }
        }

        return new TargetStats
        {
            ByStat = totals,
            Sets = sets,
            UnresolvedContributions = unresolved,
        };
    }

    /// <summary>
    /// A rune's value at its level, or null when the tables do not carry it.
    ///
    /// <para>Null rather than zero: an unresolvable rune is reported, because treating it as zero
    /// would quietly lower the target.</para>
    /// </summary>
    private static int? ResolveRune(TraitReference reference, TargetRune rune)
    {
        if (!reference.RuneLevels.TryGetValue(rune.RuneId, out var byStat)
            || !byStat.TryGetValue(rune.StatId, out var levels)
            || levels.Count == 0)
        {
            return null;
        }

        // Chaos runes are max_level 1 and arrive at full value, so a level beyond the table is
        // normal rather than an error — clamp instead of failing.
        var index = Math.Clamp(rune.Level, 0, levels.Count - 1);

        return levels[index];
    }

    private static IReadOnlyList<RuneSynergy> ResolveSynergies(TargetBuild build, TraitReference reference)
    {
        var found = new List<RuneSynergy>();

        foreach (var (slot, item) in build.Equipment)
        {
            if (item.Runes.Count != 3)
            {
                continue;
            }

            var order = item.Runes.Select(r => RuneType(r.RuneId)).ToArray();

            // A synergy needs one of each type. Chaos substitutes for any, but resolving which type
            // it is standing in for needs the game's own choice, which the build does not record —
            // so a chaos rune makes the synergy undeterminable here rather than absent.
            if (order.Any(t => t is null) || order.Contains("chaos") || order.Distinct().Count() != 3)
            {
                continue;
            }

            var key = TraitReference.SynergyKey(SynergyCategory(slot), order!);

            if (reference.Synergies.TryGetValue(key, out var synergy))
            {
                found.Add(synergy);
            }
        }

        return found;
    }

    private static string? RuneType(string runeId) =>
        RuneTypes.FirstOrDefault(t => runeId.Contains(t.Token, StringComparison.OrdinalIgnoreCase)).Type;

    /// <summary>
    /// Slot name to the category the synergy table is keyed by. Both rings share one category, and
    /// both weapon hands are "weapon".
    /// </summary>
    private static string SynergyCategory(string slot) => slot switch
    {
        "ring_1" or "ring_2" => "ring",
        "main_hand" or "off_hand" => "weapon",
        _ => slot,
    };

    private static IReadOnlyList<GearSetProgress> ResolveSets(TargetBuild build, TraitReference reference)
    {
        var counts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (slot, item) in build.Equipment)
        {
            if (string.IsNullOrWhiteSpace(item.ItemId)
                || !reference.ItemToSet.TryGetValue(item.ItemId, out var setId))
            {
                continue;
            }

            if (!counts.TryGetValue(setId, out var slots))
            {
                counts[setId] = slots = [];
            }

            slots.Add(slot);
        }

        var progress = new List<GearSetProgress>();

        foreach (var (setId, slots) in counts)
        {
            if (!reference.Sets.TryGetValue(setId, out var set))
            {
                continue;
            }

            var pieces = slots.Count;

            progress.Add(new GearSetProgress
            {
                Set = set,
                Pieces = pieces,
                Slots = slots,
                Active = [.. set.Bonuses.Where(b => pieces >= b.PieceCount)],

                // The nearest unmet tier, which is the whole point: a set one piece short of a
                // threshold is a cheap, real gain, and recommending the full set instead is a
                // different and much larger ask.
                Next = set.Bonuses.Where(b => pieces < b.PieceCount)
                    .OrderBy(b => b.PieceCount)
                    .FirstOrDefault(),
            });
        }

        return [.. progress.OrderByDescending(p => p.Pieces)];
    }
}

public sealed record TargetStats
{
    public IReadOnlyDictionary<string, StatTotal> ByStat { get; init; }
        = new Dictionary<string, StatTotal>();

    public IReadOnlyList<GearSetProgress> Sets { get; init; } = [];

    /// <summary>Runes whose value could not be looked up. Reported, never treated as zero.</summary>
    public IReadOnlyList<string> UnresolvedContributions { get; init; } = [];

    /// <summary>
    /// What is deliberately absent from every total here.
    ///
    /// <para>Fixed text rather than computed, because the omissions are structural: they need data
    /// questlog does not publish or an item level the build never states. Callers must surface this
    /// — see the class remarks on why a silently-partial target is worse than none.</para>
    /// </summary>
    public static IReadOnlyList<string> ExcludedSources =>
    [
        "Base attributes and everything they contribute (accuracy, evasion, endurance and the rest "
        + "scale from Perception, Dexterity and Fortitude).",
        "Item base stats — the build does not state an item level, and stats are tabulated per level.",
        "Heroic picks, resonance and potential.",
        "Any slot whose rune order includes a chaos rune, since which type it substitutes for is not recorded.",
    ];

    public StatTotal? For(string statId) =>
        ByStat.TryGetValue(statId, out var total) ? total : null;
}

public sealed record StatTotal
{
    public required string StatId { get; init; }
    public int Total { get; init; }

    /// <summary>Contribution per source — traits, runes, rune synergy, set bonus.</summary>
    public IReadOnlyDictionary<string, int> BySource { get; init; }
        = new Dictionary<string, int>();

    public string Describe() =>
        $"{StatId} {Total:N0} (" + string.Join(", ", BySource.OrderByDescending(s => s.Value)
            .Select(s => $"{s.Key} {s.Value:N0}")) + ")";
}

public sealed record GearSetProgress
{
    public required GearSet Set { get; init; }
    public required int Pieces { get; init; }
    public IReadOnlyList<string> Slots { get; init; } = [];
    public IReadOnlyList<GearSetBonus> Active { get; init; } = [];

    /// <summary>The nearest tier not yet reached, or null when the set is complete.</summary>
    public GearSetBonus? Next { get; init; }

    public int? PiecesToNext => Next is null ? null : Next.PieceCount - Pieces;

    public string Describe()
    {
        var head = $"{Set.Name} — {Pieces} piece(s), {Active.Count} bonus tier(s) active";

        return PiecesToNext is { } needed
            ? $"{head}; {needed} more for the {Next!.PieceCount}-piece bonus"
            : $"{head}; complete";
    }
}
