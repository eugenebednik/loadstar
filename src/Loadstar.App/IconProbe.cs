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
    /// The purple the game draws behind an equipment icon, sampled from a real character sheet.
    ///
    /// <para>Transparent icon pixels are flattened onto this so the questlog copy and the in-game copy
    /// agree everywhere the artwork is absent — which is most of a tile.</para>
    /// </summary>
    private static readonly (byte B, byte G, byte R) SlotBackdrop = (0x8E, 0x3F, 0x86);

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
                var artwork = ArtworkBounds.FromAlpha(raw);
                var image = raw.Crop(artwork);

                FlattenOpaque(image, SlotBackdrop);

                var hash = PerceptualHash.Compute(image);

                // One entry per ITEM, so a shared icon legitimately produces several entries with the
                // same hash — which the index must then report as ambiguous rather than picking one.
                foreach (var item in group)
                {
                    index.Add(item.Name, hash, item.EquipmentType);
                    hashes[item.Id] = hash;
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

        if (!string.IsNullOrWhiteSpace(capturePath))
        {
            await MatchCaptureAsync(capturePath, index, hashes, catalog);
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
        EquipmentCatalog catalog)
    {
        if (!File.Exists(capturePath))
        {
            Console.WriteLine($"\nNo capture at {capturePath}.");

            return;
        }

        var capture = await ImageDecoder.DecodeAsync(await File.ReadAllBytesAsync(capturePath));

        Console.WriteLine($"\nCapture {capture.Width}x{capture.Height}: {capturePath}");

        // Measured off a 2560x1600 character sheet. The artwork sits inside the ring, so these crop the
        // inner disc and leave the bronze border and the level badge out of the hash.
        (string Slot, PixelRect Region)[] slots =
        [
            ("head",     new PixelRect(1678, 205, 84, 84)),
            ("cloak",    new PixelRect(1808, 205, 84, 84)),
            ("chest",    new PixelRect(1678, 335, 84, 84)),
            ("gloves",   new PixelRect(1808, 335, 84, 84)),
            ("legs",     new PixelRect(1678, 465, 84, 84)),
            ("boots",    new PixelRect(1808, 465, 84, 84)),
            ("necklace", new PixelRect(1678, 592, 84, 84)),
            ("bracelet", new PixelRect(1808, 592, 84, 84)),
            ("ring1",    new PixelRect(1678, 700, 84, 84)),
            ("ring2",    new PixelRect(1808, 700, 84, 84)),
            ("earring",  new PixelRect(1678, 808, 84, 84)),
            ("belt",     new PixelRect(1808, 808, 84, 84)),
        ];

        foreach (var (slot, region) in slots)
        {
            if (region.Intersect(capture.Bounds).IsEmpty)
            {
                Console.WriteLine($"  {slot,-9} region outside the capture");

                continue;
            }

            // The tile is mostly rarity disc and bronze ring; only the art can be compared. FromBackdrop
            // samples the disc colour from the corners, so it adapts to the rarity rather than assuming
            // purple.
            var artwork = ArtworkBounds.FromBackdrop(capture, region);
            var hash = PerceptualHash.Compute(capture, artwork);
            var match = index.Match(hash);

            // The runner-up matters more than the winner: it says whether the answer was decisive or a
            // coin toss the margin happened to allow.
            var ranked = hashes
                .Select(pair => (Id: pair.Key, Distance: hash.DistanceTo(pair.Value)))
                .OrderBy(pair => pair.Distance)
                .Take(3)
                .Select(pair => $"{catalog.Find(pair.Id)?.Name ?? pair.Id} @{pair.Distance}")
                .ToArray();

            Console.WriteLine($"  {slot,-9} -> {match?.Name ?? "(unidentified)"}");
            Console.WriteLine($"            region {region.Width}x{region.Height} -> artwork bbox "
                + $"{artwork.Width}x{artwork.Height} at +{artwork.X - region.X},+{artwork.Y - region.Y}"
                + (artwork.Width >= region.Width && artwork.Height >= region.Height
                    ? "   <-- NO CROP: the whole tile read as artwork"
                    : string.Empty));
            Console.WriteLine($"            nearest: {string.Join(" | ", ranked)}");
        }
    }

    /// <summary>
    /// Composites transparent pixels onto the tile colour, after the artwork has been located.
    ///
    /// <para>A duplicate of what ImageDecoder can do inline, needed here only because the crop has to
    /// happen between decoding and flattening.</para>
    /// </summary>
    private static void FlattenOpaque(Bgra32Image image, (byte B, byte G, byte R) background)
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
