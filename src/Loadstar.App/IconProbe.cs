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
                foreach (var item in group)
                {
                    index.Add(item.Name, hash, item.EquipmentType);
                    hashes[item.Id] = hash;
                    colours[item.Id] = colour;
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

        if (!string.IsNullOrWhiteSpace(capturePath))
        {
            await MatchCaptureAsync(capturePath, index, hashes, colours, catalog);
        }

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
        EquipmentCatalog catalog)
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
            .Select((region, i) => (Slot: $"slot{i + 1}", Region: region))
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
            var match = index.MatchAcrossRenderings(hash);

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

            Console.WriteLine($"  {slot,-9} -> {match?.Name ?? "(unidentified)"}");
            Console.WriteLine($"            colour({captureColour.SampleCount}px): {string.Join(" | ", byColour)}");

            // GROUND TRUTH, for the one slot whose identity was verified by eye against questlog's own
            // icon: the tenth tile holds the Sacred Tree Resurrection Ring. Printing where the correct
            // answer actually RANKS is the measurement that decides whether a metric has weak signal worth
            // combining or no signal at all — a top-three listing cannot tell those apart.
            if (slot == "slot10" && colours.ContainsKey(Verified))
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
