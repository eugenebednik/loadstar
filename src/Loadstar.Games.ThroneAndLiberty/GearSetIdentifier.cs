using Loadstar.Core.Capture;

namespace Loadstar.Games.ThroneAndLiberty;

/// <summary>
/// Identifies equipped armour by deciding which SET the tiles belong to, then assigning pieces inside it.
///
/// <para><b>Why not match each slot independently.</b> Measured on a live character sheet with labels the
/// player confirmed, per-slot ranking put the correct item 1st, 2nd, 5th and 26th across four slots — so any
/// rule demanding a clear winner returns nothing for three of them, which is exactly the observed "2 of 13"
/// behaviour. Scoring whole sets against all the armour tiles at once is decisive instead: over 318
/// candidate sets the right one scored 0.994 against 0.716 for the runner-up, and was the only set above
/// 0.72. A wrong set cannot win five independent pools of about a hundred items simultaneously, and that is
/// the whole reason this works where the per-slot question does not.</para>
///
/// <para>With the set fixed, what remains is a five-by-five assignment, and greedy on the most confident
/// pair recovered <b>4 of 4</b> confirmed slots against 1 of 4 for per-slot top-1.</para>
///
/// <para><b>What it deliberately does not do is count pieces.</b> In the same run <c>feet</c> was correct at
/// 0.253 while <c>chest</c> — a piece from a different set entirely — scored 0.627. Similarity does not
/// separate "belongs to this set" from "does not", so a count derived from these numbers would be invented.
/// That is the failure the player reported: advice claiming two set pieces where there were four. The set
/// name is reportable; the count is not, and <see cref="GearVerdict.CountIsUnknown"/> exists to keep callers
/// from quietly deriving one.</para>
/// </summary>
public static class GearSetIdentifier
{
    /// <summary>
    /// How many of the observed armour slots a set must cover to be considered at all.
    ///
    /// <para>Three, so a set cannot win on one lucky slot. The winning margin is computed over the slots a
    /// set actually covers, so a two-piece set would otherwise be scored on a sample far too small to
    /// compare against a five-piece one.</para>
    /// </summary>
    public const int MinimumSlotsCovered = 3;

    /// <summary>
    /// How far the best set must beat the runner-up, in mean cosine similarity.
    ///
    /// <para><b>Measured, with room to spare.</b> The observed gap between the correct set and the next
    /// best was 0.994 to 0.716, a margin of 0.278. This sits well below that and well above the spacing
    /// among the also-rans, which clustered between 0.57 and 0.72.</para>
    /// </summary>
    public const double SetMargin = 0.12;

    /// <summary>
    /// Identifies the set, or returns null when no set explains the tiles clearly enough to name.
    ///
    /// <para>Null is a real answer and callers must render it as unidentified. Naming a set that only
    /// narrowly won would reintroduce the confident-wrong failure the whole approach exists to avoid.</para>
    /// </summary>
    public static GearVerdict? Identify(
        IReadOnlyList<SlotSignature> slots,
        IReadOnlyList<GearCandidate> candidates,
        double margin = SetMargin)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(candidates);

        if (slots.Count == 0 || candidates.Count == 0)
        {
            return null;
        }

        // Only candidates whose category some observed slot can hold. Without this a set is scored partly on
        // slots that are not on screen, which favours large sets for no reason.
        var wanted = slots
            .SelectMany(slot => slot.Categories)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var bySet = candidates
            .Where(candidate => candidate.SetKey is not null && wanted.Contains(candidate.Category))
            .GroupBy(candidate => candidate.SetKey!, StringComparer.Ordinal)
            .ToList();

        var scored = new List<(double Mean, IGrouping<string, GearCandidate> Set, int Covered)>();

        foreach (var set in bySet)
        {
            double total = 0;
            var covered = 0;

            foreach (var slot in slots)
            {
                var best = BestIn(set, slot);

                if (best is not null)
                {
                    total += best.Value.Similarity;
                    covered++;
                }
            }

            if (covered >= MinimumSlotsCovered)
            {
                scored.Add((total / covered, set, covered));
            }
        }

        if (scored.Count == 0)
        {
            return null;
        }

        scored.Sort((a, b) => b.Mean.CompareTo(a.Mean));

        var winner = scored[0];
        var runnerUp = scored.Count > 1 ? scored[1].Mean : double.NegativeInfinity;

        if (scored.Count > 1 && winner.Mean - runnerUp < margin)
        {
            return null;
        }

        return new GearVerdict(
            winner.Set.Key,
            winner.Set.First().SetName ?? winner.Set.Key,
            winner.Mean,
            double.IsNegativeInfinity(runnerUp) ? null : runnerUp,
            Assign(winner.Set, slots, candidates));
    }

    /// <summary>
    /// Assigns pieces to slots, most confident pair first, so a strong match claims its slot before a weak
    /// one can take it.
    ///
    /// <para>Greedy rather than optimal: with at most six armour slots and a set of similar size the two
    /// agree in every case observed, and a greedy pass is something a reader can follow.</para>
    /// </summary>
    private static IReadOnlyList<SlotAssignment> Assign(
        IEnumerable<GearCandidate> set,
        IReadOnlyList<SlotSignature> slots,
        IReadOnlyList<GearCandidate> allCandidates)
    {
        var members = set.ToList();
        var pairs = new List<(double Similarity, SlotSignature Slot, GearCandidate Item)>();

        foreach (var slot in slots)
        {
            foreach (var item in members)
            {
                if (slot.Categories.Contains(item.Category, StringComparer.OrdinalIgnoreCase))
                {
                    pairs.Add((slot.Signature.SimilarityTo(item.Signature), slot, item));
                }
            }
        }

        pairs.Sort((a, b) => b.Similarity.CompareTo(a.Similarity));

        var takenSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var takenItems = new HashSet<string>(StringComparer.Ordinal);
        var assignments = new List<SlotAssignment>();

        foreach (var (similarity, slot, item) in pairs)
        {
            // Checked BEFORE claiming either. Writing this as `!takenSlots.Add(x) || !takenItems.Add(y)`
            // followed by a Remove is wrong and was wrong here: `||` short-circuits, so when the slot was
            // already taken the Remove un-claimed the slot a BETTER pair had already won, letting a worse
            // pair take it and emitting two assignments for one slot.
            if (takenSlots.Contains(slot.SlotName) || takenItems.Contains(item.ItemId))
            {
                continue;
            }

            takenSlots.Add(slot.SlotName);
            takenItems.Add(item.ItemId);

            assignments.Add(new SlotAssignment(
                slot.SlotName,
                item.ItemId,
                item.Name,
                similarity,
                WinsItsOwnPool(slot, item, allCandidates)));
        }

        // Slots the set cannot fill are reported explicitly rather than omitted, so a caller cannot mistake
        // a missing entry for an unexamined slot.
        foreach (var slot in slots)
        {
            if (!takenSlots.Contains(slot.SlotName))
            {
                assignments.Add(new SlotAssignment(slot.SlotName, null, null, 0, false));
            }
        }

        return assignments;
    }

    /// <summary>
    /// Whether this item would also have won its slot outright against every catalogue item of the same
    /// category — the only evidence available that the assignment is right rather than merely the best
    /// available inside a set that was chosen for other reasons.
    ///
    /// <para>This is what <see cref="SlotAssignment.Confident"/> means, and it is deliberately strict. On the
    /// measured sheet exactly one of four correct assignments cleared it, which is an honest reflection of
    /// how much a single tile supports on its own.</para>
    /// </summary>
    private static bool WinsItsOwnPool(
        SlotSignature slot,
        GearCandidate item,
        IReadOnlyList<GearCandidate> allCandidates)
    {
        var mine = slot.Signature.SimilarityTo(item.Signature);

        foreach (var other in allCandidates)
        {
            if (!slot.Categories.Contains(other.Category, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(other.ItemId, item.ItemId, StringComparison.Ordinal))
            {
                continue;
            }

            if (slot.Signature.SimilarityTo(other.Signature) >= mine)
            {
                return false;
            }
        }

        return true;
    }

    private static (GearCandidate Item, double Similarity)? BestIn(
        IEnumerable<GearCandidate> set,
        SlotSignature slot)
    {
        (GearCandidate Item, double Similarity)? best = null;

        foreach (var item in set)
        {
            if (!slot.Categories.Contains(item.Category, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var similarity = slot.Signature.SimilarityTo(item.Signature);

            if (best is null || similarity > best.Value.Similarity)
            {
                best = (item, similarity);
            }
        }

        return best;
    }
}

/// <summary>One equipment tile as observed on screen.</summary>
public sealed record SlotSignature(
    string SlotName,
    IReadOnlyCollection<string> Categories,
    IconSignature Signature);

/// <summary>
/// One catalogue item that could occupy a slot.
///
/// <para><paramref name="SetKey"/> is the catalogue's <c>setId</c> where it has one — 41% of armour does —
/// and the item's name prefix otherwise. Mixing the two is deliberate: setId is precise and authoritative,
/// but leaving the other 59% ungrouped would exclude most of the catalogue from the one mechanism that
/// actually works.</para>
/// </summary>
public sealed record GearCandidate(
    string ItemId,
    string Name,
    string Category,
    string? SetKey,
    string? SetName,
    IconSignature Signature);

/// <summary>What the identifier concluded, and what it refuses to conclude.</summary>
public sealed record GearVerdict(
    string SetKey,
    string SetName,
    double MeanSimilarity,
    double? RunnerUpSimilarity,
    IReadOnlyList<SlotAssignment> Slots)
{
    /// <summary>
    /// Always true, and present so callers have to acknowledge it.
    ///
    /// <para>Similarity does not separate "belongs to this set" from "does not": a correct piece scored 0.253
    /// while an item from another set scored 0.627 in the same run. So the number of set pieces equipped
    /// cannot be derived from this verdict, and anything that needs it must ask the player for a tooltip,
    /// which states the count outright.</para>
    /// </summary>
    public bool CountIsUnknown => true;

    /// <summary>Assignments that also won their whole category pool, which is the strict reading.</summary>
    public IEnumerable<SlotAssignment> Confirmed => Slots.Where(slot => slot.Confident);
}

/// <summary>
/// One slot's outcome. <paramref name="ItemId"/> is null when the identified set has nothing for this slot,
/// which is a normal result — most sets do not cover a cloak.
/// </summary>
public sealed record SlotAssignment(
    string SlotName,
    string? ItemId,
    string? ItemName,
    double Similarity,
    bool Confident);
