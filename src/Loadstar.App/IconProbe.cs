using System.Diagnostics;
using System.Net.Http;

using Loadstar.Capture.Windows;
using Loadstar.Core.Capture;
using Loadstar.Games.ThroneAndLiberty;

namespace Loadstar.App;

/// <summary>
/// Measures whether icon identification actually works, against real data.
///
/// <para><b>This exists because the code it measures asks for it in writing.</b>
/// <see cref="IconIndex.DefaultTolerance"/>'s own remarks say the thresholds were calibrated on
/// SYNTHETIC icons built from a shared template, that the gap between "same icon rescaled" (4–14 bits)
/// and "different icons" (23–71 bits) was "narrower than is comfortable", and that it "must be
/// re-measured against real game icons ... before the feature is trusted. Real sprites should separate
/// better than synthetic ones, but that is a prediction, not a result." This turns it into a result.</para>
///
/// <para>Reachable as <c>--icon-probe &lt;capture.png&gt;</c>, alongside <c>--settings</c> and
/// <c>--ask</c>. It prints numbers and changes nothing.</para>
/// </summary>
internal static class IconProbe
{
    /// <summary>
    /// The purple the game draws behind an equipment icon, sampled from a real character sheet. What was
    /// transparent in questlog's art is flattened onto it so both sides share a background.
    /// </summary>
    private static readonly (byte B, byte G, byte R) SlotBackdrop = (0x8E, 0x3F, 0x86);

    /// <summary>
    /// Centres an image in a larger frame filled with the tile colour, adding <paramref name="percent"/> of
    /// its size as margin on every side.
    ///
    /// <para><b>This is the axis none of the earlier attempts controlled.</b> PerceptualHash resamples
    /// whatever region it is handed into a fixed 17x16 grid, so RESIZING an image does not change its hash at
    /// all — the two renderings were never disagreeing about pixel size. What they disagree about is how much
    /// of the frame the artwork occupies: questlog's asset is cropped tight to the art, the game's tile has
    /// disc margin all round it. Padding one to match the other is the only thing that moves that.</para>
    /// </summary>
    private static Bgra32Image Pad(Bgra32Image image, int percent, (byte B, byte G, byte R) background)
    {
        if (percent <= 0)
        {
            return image;
        }

        var marginX = image.Width * percent / 100;
        var marginY = image.Height * percent / 100;
        var width = image.Width + (marginX * 2);
        var height = image.Height + (marginY * 2);
        var stride = width * Bgra32Image.BytesPerPixel;
        var padded = new Bgra32Image(new byte[stride * height], width, height, stride);

        padded.Fill(padded.Bounds, background.B, background.G, background.R, 255);

        for (var y = 0; y < image.Height; y++)
        {
            Array.Copy(
                image.Pixels,
                y * image.Stride,
                padded.Pixels,
                ((y + marginY) * stride) + (marginX * Bgra32Image.BytesPerPixel),
                image.Width * Bgra32Image.BytesPerPixel);
        }

        return padded;
    }

    /// <summary>Composites transparent pixels onto the tile colour, in place.</summary>
    private static void Flatten(Bgra32Image image, (byte B, byte G, byte R) background)
    {
        for (var i = 0; i < image.Pixels.Length; i += Bgra32Image.BytesPerPixel)
        {
            var alpha = image.Pixels[i + 3];

            if (alpha == 255)
            {
                continue;
            }

            var inverse = 255 - alpha;

            image.Pixels[i] = (byte)(((image.Pixels[i] * alpha) + (background.B * inverse)) / 255);
            image.Pixels[i + 1] = (byte)(((image.Pixels[i + 1] * alpha) + (background.G * inverse)) / 255);
            image.Pixels[i + 2] = (byte)(((image.Pixels[i + 2] * alpha) + (background.R * inverse)) / 255);
            image.Pixels[i + 3] = 255;
        }
    }

    /// <summary>
    /// The one slot identity established independently: tile ten of the reference capture holds this ring,
    /// confirmed by comparing the cropped tile against questlog's own icon by eye. Everything about whether
    /// a metric works is measured against it, because a ranking with no known answer in it says nothing.
    /// </summary>
    private const string Verified = "ring_aa_S1_004";

    public static async Task<int> RunAsync(string? capturePath)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };

        http.DefaultRequestHeaders.UserAgent.ParseAdd("Loadstar/0.7 (icon index)");

        var cacheDirectory = Path.Combine(new Core.Configuration.SettingsStore().Directory, "icons");

        Directory.CreateDirectory(cacheDirectory);

        Console.WriteLine("Fetching the equipment catalogue…");

        var catalog = await new QuestlogClient(http)
            .GetEquipmentCatalogAsync(new Core.Configuration.SettingsStore().Directory, CancellationToken.None);

        if (catalog is null)
        {
            Console.WriteLine("Catalogue unavailable.");

            return 1;
        }

        // Every item that HAS an icon, grouped so a shared icon is fetched once.
        var byIcon = catalog.Items
            .Where(i => TlIconSource.UrlFor(i.Icon) is not null)
            .GroupBy(i => TlIconSource.UrlFor(i.Icon)!)
            .ToArray();

        Console.WriteLine($"{catalog.Count} items, {byIcon.Length} distinct icons.");

        var watch = Stopwatch.StartNew();
        var index = new IconIndex { BuiltAt = DateTimeOffset.UtcNow };
        var hashes = new Dictionary<string, IconHash>(StringComparer.Ordinal);
        var colours = new Dictionary<string, ColourSignature>(StringComparer.Ordinal);

        // The descriptor that replaced the hash for this job. Collected alongside rather than instead of, so
        // the two can be compared on the same capture in one run.
        var candidates = new List<GearCandidate>();
        var failures = 0;

        foreach (var group in byIcon)
        {
            var bytes = await LoadAsync(http, group.Key, cacheDirectory);

            if (bytes is null)
            {
                failures++;

                continue;
            }

            try
            {
                // Decoded WITHOUT flattening so the alpha survives to locate the artwork, then cropped to
                // it, then flattened. Order matters: flatten first and every pixel is opaque, so there is
                // nothing left to find the art with.
                var raw = await ImageDecoder.DecodeAsync(bytes);
                var colour = ColourSignature.FromAlpha(raw, raw.Bounds);

                // THE CONFIGURATION THAT MEASURED BEST, and it is not the obvious one. Crop to the artwork
                // and flatten what was transparent onto THE GAME'S DISC COLOUR, then compare against the
                // game's tile untouched.
                //
                // Isolating the artwork on both sides and flattening both onto a neutral grey was tried and
                // measured WORSE — the verified item fell from rank 1 at 71 bits to rank 3 at 86. Matching
                // the two BACKGROUNDS turns out to matter more than removing them: the segmentation is never
                // identical on the two sides, so a neutral fill puts a different silhouette boundary into
                // each hash, while a shared purple leaves the same one in both.
                var image = raw.Crop(ArtworkBounds.FromAlpha(raw));

                Flatten(image, SlotBackdrop);

                var hash = PerceptualHash.Compute(image);

                // One entry per ITEM, so a shared icon legitimately produces several entries with the
                // same hash — which the index must then report as ambiguous rather than picking one.
                var signature = IconSignature.Compute(image);

                foreach (var item in group)
                {
                    index.Add(item.Name, hash, item.EquipmentType);
                    hashes[item.Id] = hash;
                    colours[item.Id] = colour;

                    // setId where the catalogue has one (41% of armour), name prefix otherwise. Leaving the
                    // rest ungrouped would exclude most of the catalogue from set inference.
                    var setKey = !string.IsNullOrWhiteSpace(item.SetId) ? item.SetId : SetPrefix(item.Name);

                    if (setKey is not null && item.EquipmentType is not null)
                    {
                        candidates.Add(new GearCandidate(
                            item.Id, item.Name, item.EquipmentType, setKey, SetPrefix(item.Name), signature));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  decode failed for {group.Key}: {ex.GetType().Name}");
                failures++;
            }
        }

        Console.WriteLine(
            $"Hashed {hashes.Count} items in {watch.Elapsed.TotalSeconds:0.0}s ({failures} failures).");

        ReportSeparation(hashes);
        ReportColourSeparation(colours);

        if (!string.IsNullOrWhiteSpace(capturePath) && File.Exists(capturePath))
        {
            await SweepPaddingAsync(capturePath, byIcon, cacheDirectory, http, catalog);
        }

        if (!string.IsNullOrWhiteSpace(capturePath))
        {
            await MatchCaptureAsync(capturePath, index, hashes, colours, catalog, candidates);
        }

        WriteGearIndex(candidates);

        return 0;
    }

    /// <summary>
    /// How far apart real item icons actually sit — the number the tolerance depends on.
    ///
    /// <para>Reports the nearest DIFFERENT-icon neighbour for a sample of items. If that distribution
    /// overlaps the rescale drift, no threshold can separate them and the whole approach needs a
    /// colour-aware signature instead of a luminance one.</para>
    /// </summary>
    private static void ReportSeparation(Dictionary<string, IconHash> hashes)
    {
        var distinct = hashes.Values.Distinct().ToArray();

        Console.WriteLine($"\n{distinct.Length} distinct hashes from {hashes.Count} items.");

        if (distinct.Length < 2)
        {
            return;
        }

        var nearest = new List<int>();

        // Sampled rather than exhaustive: 1,500 icons is 1.1M pairs, and a sample of 300 against all of
        // them is plenty to see the shape of the distribution.
        foreach (var probe in distinct.Take(300))
        {
            var best = int.MaxValue;

            foreach (var other in distinct)
            {
                if (other.Equals(probe))
                {
                    continue;
                }

                best = Math.Min(best, probe.DistanceTo(other));
            }

            nearest.Add(best);
        }

        nearest.Sort();

        Console.WriteLine("Nearest different-icon distance, over 300 sampled icons:");
        Console.WriteLine($"  min {nearest[0]}   p5 {Pick(nearest, 0.05)}   median {Pick(nearest, 0.50)}"
            + $"   p95 {Pick(nearest, 0.95)}   max {nearest[^1]}");
        Console.WriteLine($"  within the {IconIndex.DefaultTolerance}-bit tolerance: "
            + $"{nearest.Count(d => d <= IconIndex.DefaultTolerance)} of {nearest.Count}");
    }

    /// <summary>
    /// The same nearest-neighbour measurement for colour, so the two can be compared on equal terms.
    /// </summary>
    private static void ReportColourSeparation(Dictionary<string, ColourSignature> colours)
    {
        var distinct = colours.Values.Take(400).ToArray();
        var nearest = new List<int>();

        foreach (var probe in distinct.Take(300))
        {
            var best = int.MaxValue;

            foreach (var other in distinct)
            {
                if (ReferenceEquals(other, probe))
                {
                    continue;
                }

                best = Math.Min(best, probe.DistanceTo(other));
            }

            nearest.Add(best);
        }

        nearest.Sort();

        Console.WriteLine();
        Console.WriteLine(
            $"Colour signature, nearest different-icon distance (0..{ColourSignature.Total * 2}):");
        Console.WriteLine($"  min {nearest[0]}   p5 {Pick(nearest, 0.05)}   median {Pick(nearest, 0.50)}"
            + $"   p95 {Pick(nearest, 0.95)}   max {nearest[^1]}");
    }

    private static int Pick(List<int> sorted, double quantile) =>
        sorted[Math.Clamp((int)(sorted.Count * quantile), 0, sorted.Count - 1)];

    /// <summary>
    /// Hashes hand-specified slot rectangles out of a real capture and reports what the index says.
    ///
    /// <para>Rectangles are passed in rather than detected, deliberately: this measures the MATCHER. If
    /// identification cannot be made to work with perfect crops, automatic slot detection is wasted
    /// effort, and if it can, detection becomes the only remaining problem.</para>
    /// </summary>
    private static async Task MatchCaptureAsync(
        string capturePath,
        IconIndex index,
        Dictionary<string, IconHash> hashes,
        Dictionary<string, ColourSignature> colours,
        EquipmentCatalog catalog,
        IReadOnlyList<GearCandidate> candidates)
    {
        if (!File.Exists(capturePath))
        {
            Console.WriteLine($"\nNo capture at {capturePath}.");

            return;
        }

        var capture = await ImageDecoder.DecodeAsync(await File.ReadAllBytesAsync(capturePath));

        Console.WriteLine($"\nCapture {capture.Width}x{capture.Height}: {capturePath}");

        // DETECTED, not measured. See EquipmentSlotLocator: hand-measured rectangles were wrong on the
        // capture they were measured from and would be wrong on any other resolution.
        var located = EquipmentSlotLocator.Locate(capture);

        Console.WriteLine($"Detected {located.Count} equipment slots.");

        if (located.Count == 0)
        {
            Console.WriteLine("  No slot grid found — treated as 'not the character sheet'.");

            return;
        }

        var slots = located
            .Select(region => (Slot: $"r{region.Row}c{region.Column}", Region: region))
            .ToArray();

        foreach (var (slot, located2) in slots)
        {
            var region = located2.Artwork;

            if (region.Intersect(capture.Bounds).IsEmpty)
            {
                Console.WriteLine($"  {slot,-9} region outside the capture");

                continue;
            }

            // The tile is mostly rarity disc and bronze ring; only the art can be compared. FromBackdrop
            // samples the disc colour from the corners, so it adapts to the rarity rather than assuming
            // purple.
            // The tile as the game drew it, disc and all. No bounding-box crop: two attempts at one both
            // measured worse than leaving it alone.
            var hash = PerceptualHash.Compute(capture, region);

            // Constrained to the ONE category this slot holds, from the grid order the product owner
            // supplied. Falls back to the row's coarse armour/accessory split when the tile count disagrees
            // with the thirteen slots that should be there, because an index into the wrong list is worse
            // than no index.
            var allowed = slots.Length == TlEquipmentLayout.Order.Count
                ? TlEquipmentLayout.CategoriesForIndex(Array.FindIndex(slots, e => e.Slot == slot))
                : TlEquipmentLayout.CategoriesForRow(located2.Row);

            var match = index.MatchAcrossRenderings(hash, allowed);

            // The disc region, not the artwork bbox: a histogram does not care where the pixels are, so
            // there is nothing to gain from cropping and something to lose.
            var captureColour = ColourSignature.FromBackdrop(capture, located2.Disc);

            var byColour = colours
                .Select(pair => (Id: pair.Key, Distance: captureColour.DistanceTo(pair.Value)))
                .OrderBy(pair => pair.Distance)
                .Take(3)
                .Select(pair => $"{catalog.Find(pair.Id)?.Name ?? pair.Id} @{pair.Distance}")
                .ToArray();

            // The runner-up matters more than the winner: it says whether the answer was decisive or a
            // coin toss the margin happened to allow.
            var ranked = hashes
                .Select(pair => (Id: pair.Key, Distance: hash.DistanceTo(pair.Value)))
                .OrderBy(pair => pair.Distance)
                .Take(3)
                .Select(pair => $"{catalog.Find(pair.Id)?.Name ?? pair.Id} @{pair.Distance}")
                .ToArray();

            var slotName = slots.Length == TlEquipmentLayout.Order.Count
                ? TlEquipmentLayout.SlotNameForIndex(Array.FindIndex(slots, e => e.Slot == slot))
                : null;

            Console.WriteLine($"  {slot,-6} {slotName,-9} -> {match?.Name ?? "(unidentified)"}");
            Console.WriteLine($"            colour({captureColour.SampleCount}px): {string.Join(" | ", byColour)}");

            // GROUND TRUTH, for the one slot whose identity was verified by eye against questlog's own
            // icon: the tenth tile holds the Sacred Tree Resurrection Ring. Printing where the correct
            // answer actually RANKS is the measurement that decides whether a metric has weak signal worth
            // combining or no signal at all — a top-three listing cannot tell those apart.
            if (slot == "r4c0" && colours.ContainsKey(Verified))
            {
                var hashRank = hashes
                    .OrderBy(pair => hash.DistanceTo(pair.Value))
                    .Select((pair, i) => (pair.Key, Rank: i + 1))
                    .First(pair => pair.Key == Verified);

                var colourRank = colours
                    .OrderBy(pair => captureColour.DistanceTo(pair.Value))
                    .Select((pair, i) => (pair.Key, Rank: i + 1))
                    .First(pair => pair.Key == Verified);

                Console.WriteLine(
                    $"            GROUND TRUTH {catalog.Find(Verified)?.Name}: "
                    + $"hash rank {hashRank.Rank}/{hashes.Count} @{hash.DistanceTo(hashes[Verified])}bits, "
                    + $"colour rank {colourRank.Rank}/{colours.Count} "
                    + $"@{captureColour.DistanceTo(colours[Verified])}");
            }
            Console.WriteLine($"            nearest: {string.Join(" | ", ranked)}");
        }

        ReportSetIdentification(capture, located, slots, candidates);
    }

    /// <summary>
    /// Writes the signature index the shipping app loads, so identification costs no downloads at runtime.
    ///
    /// <para><b>Why ship it rather than build it on the user's machine.</b> Building requires fetching about
    /// 1,500 icons from questlog's CDN, which is minutes of first-run latency and a hard dependency on a
    /// third party being up — against a project that treats being offline as the normal state. Quantised to
    /// signed bytes the whole index is a few hundred kilobytes against a 62 MB installer, so there is no
    /// reason not to carry it.</para>
    ///
    /// <para>Quantisation is safe here because each channel is already unit-length: values live in roughly
    /// [-1, 1], so one byte per component costs about 0.4% of full scale, far below the margins the
    /// identifier decides on.</para>
    /// </summary>
    private static void WriteGearIndex(IReadOnlyList<GearCandidate> candidates)
    {
        // Only what a character sheet actually holds. Weapons and artifacts are not identified this way.
        var wanted = TlEquipmentLayout.Order
            .SelectMany((_, i) => TlEquipmentLayout.CategoriesForIndex(i))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = candidates
            .Where(c => wanted.Contains(c.Category))
            .GroupBy(c => c.ItemId, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(c => c.ItemId, StringComparer.Ordinal)
            .Select(c => new
            {
                id = c.ItemId,
                name = c.Name,
                category = c.Category,
                setKey = c.SetKey,
                setName = c.SetName,
                signature = Convert.ToBase64String(Quantise(c.Signature)),
            })
            .ToList();

        var path = Path.Combine(AppContext.BaseDirectory, "gear-index.json");

        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(
            new { builtAt = DateTimeOffset.UtcNow, grid = IconSignature.Grid, items = rows }));

        Console.WriteLine();
        Console.WriteLine($"Gear index: {rows.Count} items -> {path} "
            + $"({new FileInfo(path).Length / 1024:n0} KB)");
    }

    private static byte[] Quantise(IconSignature signature)
    {
        var bytes = new byte[signature.Length];

        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(sbyte)Math.Clamp((int)Math.Round(signature.Values[i] * 127f), -127, 127);
        }

        return bytes;
    }

    /// <summary>Two words joined, which is how a set reads on screen when the catalogue has no setId for it.</summary>
    private static string? SetPrefix(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length > 2 ? string.Join(' ', parts.Take(2)) : name.Trim();
    }

    /// <summary>
    /// The set-first result, which is the measurement that matters. Per-slot ranking above answers "what is
    /// this one tile", which no single tile supports; this answers "what set explains all of them", which
    /// several tiles together answer decisively.
    /// </summary>
    private static void ReportSetIdentification(
        Bgra32Image capture,
        IReadOnlyList<SlotRegion> located,
        (string Slot, SlotRegion Region)[] slots,
        IReadOnlyList<GearCandidate> candidates)
    {
        Console.WriteLine();
        Console.WriteLine("SET-FIRST IDENTIFICATION");

        if (slots.Length != TlEquipmentLayout.Order.Count)
        {
            Console.WriteLine($"  {slots.Length} tiles found, expected {TlEquipmentLayout.Order.Count} — "
                + "slot names are unreliable, so this is skipped rather than guessed.");

            return;
        }

        var observed = new List<SlotSignature>();

        for (var i = 0; i < slots.Length; i++)
        {
            var name = TlEquipmentLayout.SlotNameForIndex(i);
            var categories = TlEquipmentLayout.CategoriesForIndex(i);

            // Armour only: accessories do not come in sets that span slots, so including them would score a
            // set on evidence it cannot supply.
            if (name is not "head" and not "chest" and not "hands" and not "legs" and not "feet" and not "cloak")
            {
                continue;
            }

            observed.Add(new SlotSignature(
                name,
                categories,
                IconSignature.Compute(capture, slots[i].Region.Artwork)));
        }

        Console.WriteLine($"  {observed.Count} armour tiles, {candidates.Count} catalogue candidates");

        var verdict = GearSetIdentifier.Identify(observed, candidates);

        if (verdict is null)
        {
            Console.WriteLine("  no set explained the tiles clearly enough to name (correct answer when true)");

            return;
        }

        Console.WriteLine($"  SET: {verdict.SetName}  mean {verdict.MeanSimilarity:0.000}"
            + $"  runner-up {verdict.RunnerUpSimilarity:0.000}"
            + $"  margin {verdict.MeanSimilarity - (verdict.RunnerUpSimilarity ?? 0):0.000}");

        foreach (var slot in verdict.Slots.OrderBy(s => s.SlotName, StringComparer.Ordinal))
        {
            Console.WriteLine($"    {slot.SlotName,-6} {slot.ItemName ?? "(set has none)",-32} "
                + $"{slot.Similarity:0.000} {(slot.Confident ? "confirmed" : "assigned")}");
        }

        Console.WriteLine("  piece count is NOT reported: similarity cannot separate set members from "
            + "non-members. Ask for a tooltip.");
    }

    /// <summary>
    /// Rebuilds the index at several padding values and reports what each is worth.
    ///
    /// <para>Two numbers per setting, and the second is the honest one. The verified tile's RANK is the
    /// direct measurement but there is only one of it, so tuning on it alone would be fitting a parameter to
    /// a single sample. CONFIDENT MATCHES across all fourteen tiles is the corroboration: a wrong padding
    /// cannot produce many winners that each clear the margin, because noise ties.</para>
    /// </summary>
    private static async Task SweepPaddingAsync(
        string capturePath,
        IGrouping<string, CatalogItem>[] byIcon,
        string cacheDirectory,
        HttpClient http,
        EquipmentCatalog catalog)
    {
        var capture = await ImageDecoder.DecodeAsync(await File.ReadAllBytesAsync(capturePath));
        var tiles = EquipmentSlotLocator.Locate(capture)
            .Select(slot => PerceptualHash.Compute(capture, slot.Artwork))
            .ToArray();

        // THE CAPTURE CROP, swept first, because the padding sweep below established that the index side is
        // already framed correctly: 0% padding was optimal and every increase made it monotonically worse.
        // So if the two still disagree about framing, the disagreement is on this side.
        var located = EquipmentSlotLocator.Locate(capture);
        var flatIndex = new IconIndex();
        var flatHashes = new Dictionary<string, IconHash>(StringComparer.Ordinal);

        foreach (var group in byIcon)
        {
            var bytes0 = await LoadAsync(http, group.Key, cacheDirectory);

            if (bytes0 is null)
            {
                continue;
            }

            var raw0 = await ImageDecoder.DecodeAsync(bytes0);
            var art0 = raw0.Crop(ArtworkBounds.FromAlpha(raw0));

            Flatten(art0, SlotBackdrop);

            var hash0 = PerceptualHash.Compute(art0);

            foreach (var item in group)
            {
                flatIndex.Add(item.Name, hash0, item.EquipmentType);
                flatHashes[item.Id] = hash0;
            }
        }

        Console.WriteLine();
        Console.WriteLine("Capture-crop sweep — fraction of the disc diameter that is hashed:");
        Console.WriteLine("  crop  verified rank  bits  margin   confident matches / 14");

        foreach (var fraction in (double[])[0.50, 0.58, 0.65, 0.707, 0.78, 0.86, 0.94, 1.00])
        {
            var cropped = located.Select(slot =>
            {
                var side = Math.Max(1, (int)(slot.Disc.Width * fraction));
                var insetX = (slot.Disc.Width - side) / 2;
                var sideY = Math.Max(1, (int)(slot.Disc.Height * fraction));
                var insetY = (slot.Disc.Height - sideY) / 2;

                return PerceptualHash.Compute(
                    capture,
                    new PixelRect(slot.Disc.X + insetX, slot.Disc.Y + insetY, side, sideY));
            }).ToArray();

            var confidentCount = cropped.Count(t => flatIndex.MatchAcrossRenderings(t) is not null);
            var probe = cropped.Length > 9 ? cropped[9] : cropped[0];
            var order = flatHashes
                .Select(pair => (pair.Key, Distance: probe.DistanceTo(pair.Value)))
                .OrderBy(pair => pair.Distance)
                .ToArray();
            var verifiedRank = Array.FindIndex(order, pair => pair.Key == Verified) + 1;

            Console.WriteLine(
                $"  {fraction,4:0.00}  {verifiedRank,11}   {probe.DistanceTo(flatHashes[Verified]),4}"
                + $"  {order[1].Distance - order[0].Distance,6}   {confidentCount,3}");
        }

        Console.WriteLine();
        Console.WriteLine("Padding sweep — questlog art padded to match the tile's margin:");
        Console.WriteLine("  pad   verified rank  bits  margin   confident matches / 14");

        foreach (var percent in (int[])[0, 5, 10, 15, 20, 25, 30, 40])
        {
            var index = new IconIndex();
            var hashes = new Dictionary<string, IconHash>(StringComparer.Ordinal);

            foreach (var group in byIcon)
            {
                var bytes = await LoadAsync(http, group.Key, cacheDirectory);

                if (bytes is null)
                {
                    continue;
                }

                var raw = await ImageDecoder.DecodeAsync(bytes);
                var art = raw.Crop(ArtworkBounds.FromAlpha(raw));

                Flatten(art, SlotBackdrop);

                var hash = PerceptualHash.Compute(Pad(art, percent, SlotBackdrop));

                foreach (var item in group)
                {
                    index.Add(item.Name, hash, item.EquipmentType);
                    hashes[item.Id] = hash;
                }
            }

            var confident = tiles.Count(t => index.MatchAcrossRenderings(t) is not null);

            var verifiedTile = tiles.Length > 9 ? tiles[9] : tiles[0];
            var ranked = hashes
                .Select(pair => (pair.Key, Distance: verifiedTile.DistanceTo(pair.Value)))
                .OrderBy(pair => pair.Distance)
                .ToArray();
            var rank = Array.FindIndex(ranked, pair => pair.Key == Verified) + 1;
            var margin = ranked.Length > 1 ? ranked[1].Distance - ranked[0].Distance : 0;

            Console.WriteLine(
                $"  {percent,3}%   {rank,11}   {verifiedTile.DistanceTo(hashes[Verified]),4}"
                + $"  {margin,6}   {confident,3}");
        }
    }

    private static async Task<byte[]?> LoadAsync(HttpClient http, string url, string cacheDirectory)
    {
        var file = Path.Combine(cacheDirectory, TlIconSource.CacheFileNameFor(url.Replace(TlIconSource.BaseUrl, string.Empty))
            ?? Path.GetFileName(url));

        try
        {
            if (File.Exists(file) && new FileInfo(file).Length > 0)
            {
                return await File.ReadAllBytesAsync(file);
            }

            var bytes = await http.GetByteArrayAsync(url);

            await File.WriteAllBytesAsync(file, bytes);

            return bytes;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            return null;
        }
    }
}
