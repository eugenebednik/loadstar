namespace Loadstar.Core.Capture;

/// <summary>
/// Finds the equipment slots on a character sheet, without being told where they are.
///
/// <para><b>Why not fixed coordinates.</b> The first attempt hand-measured rectangles off one 2560x1600
/// capture. They were wrong on that capture and would have been wrong on every other one: 4K, ultrawide
/// and any UI scale move them. Nothing about a pixel rectangle survives contact with someone else's
/// monitor.</para>
///
/// <para><b>Why not ask the model.</b> It is genuinely good at layout and this would work. But it costs a
/// round trip on every capture, returns coordinates that are approximate by nature, and cannot be
/// regression-tested offline. The slots turn out to be findable from their own appearance, which is free,
/// deterministic, and checkable against a saved screenshot forever.</para>
///
/// <para><b>What makes them findable.</b> Each slot is a bronze annulus around a rarity-coloured disc.
/// Measured off a real sheet: the ring runs (113,87,71) to (83,65,51) — warm, R &gt; G &gt; B — while the
/// epic disc is (34,18,46) and the panel behind it (26,37,66), both of which have B &gt; R. So one
/// comparison separates the ring from everything it sits between, INCLUDING pale artwork, whose channels
/// are near-equal. The ring is also the invariant part: the disc is purple at epic and orange at heroic,
/// so anything keyed on the disc colour would fail on a player's best items.</para>
///
/// <para><b>Colour alone is not enough and is not relied on.</b> The sheet has other bronze furniture — a
/// crest, panel borders, the stat plate. What no other element does is appear as a dozen circles of
/// identical size in two columns, so acceptance rests on that structure rather than on the hue. If the
/// structure is not there, this returns nothing; see <see cref="Locate"/>.</para>
/// </summary>
public static class EquipmentSlotLocator
{
    /// <summary>
    /// How much warmer than blue a pixel must be to count as ring.
    ///
    /// <para>18 from measurement, not taste. Ring samples run +32 to +44; the epic disc is −12 to −17, the
    /// panel −40, and white artwork +4 to +10. 18 sits in the gap with room either side.</para>
    /// </summary>
    private const int WarmthThreshold = 18;

    /// <summary>Slots are round, so a ring's bounding box is square. Tolerance absorbs antialiasing.</summary>
    private const double MaxAspectDeviation = 0.28;

    /// <summary>
    /// An annulus fills only its rim, so a filled blob of the right size is not a slot. Measured: a ring
    /// ~6px thick on a ~114px circle covers roughly a fifth of its box.
    /// </summary>
    private const double MaxFillRatio = 0.62;

    /// <summary>
    /// Fewest same-sized circles that count as an equipment grid. A character sheet shows thirteen; this
    /// allows for a few being lost to overlapping badges or set glows.
    /// </summary>
    private const int MinimumSlots = 8;

    /// <summary>
    /// Locates the slots, largest cluster of same-sized circles first.
    ///
    /// <para>Returns an EMPTY list when the structure is not found, which is the important behaviour: a
    /// capture of the open world, the inventory, or a sheet at an unforeseen layout must produce nothing
    /// rather than a dozen plausible rectangles. Everything downstream treats no slots as "not the
    /// character sheet", which is recoverable; a wrong rectangle silently identifies the wrong item, which
    /// is the failure this whole approach exists to prevent.</para>
    /// </summary>
    /// <param name="image">A full-window capture.</param>
    public static IReadOnlyList<SlotRegion> Locate(Bgra32Image image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var rings = FindRingComponents(image);

        if (rings.Count < MinimumSlots)
        {
            return [];
        }

        // Grouped by size, because the slots are identical and nothing else on the sheet is. Bucketed at
        // 12% so antialiasing and a pixel of scaling do not split one cluster in two.
        var clusters = new List<List<PixelRect>>();

        foreach (var ring in rings.OrderByDescending(r => r.Width))
        {
            var cluster = clusters.FirstOrDefault(c =>
                Math.Abs(c[0].Width - ring.Width) <= c[0].Width * 0.12
                && Math.Abs(c[0].Height - ring.Height) <= c[0].Height * 0.12);

            if (cluster is null)
            {
                clusters.Add([ring]);
            }
            else
            {
                cluster.Add(ring);
            }
        }

        var best = clusters.OrderByDescending(c => c.Count).First();

        if (best.Count < MinimumSlots)
        {
            return [];
        }

        return FitGrid(best);
    }

    /// <summary>
    /// Keeps only the circles that lie on a two-column grid, and labels each with its row and column.
    ///
    /// <para><b>Two jobs at once, and the second is why this exists.</b> Detection on a real sheet found a
    /// ring outside the equipment panel — same size, same colours, genuinely circular, just somewhere else
    /// entirely. Size clustering cannot reject it because it IS the right size. Grid membership can: the
    /// equipment slots share two x positions and a constant vertical pitch, and a stray does not.</para>
    ///
    /// <para>And a labelled position is what makes a category filter possible at all. Which slot a tile is
    /// tells you what KIND of item can be in it, which removes most of the catalogue from contention — a
    /// pair of trousers was matched into an earring slot precisely because nothing ruled it out.</para>
    /// </summary>
    private static IReadOnlyList<SlotRegion> FitGrid(List<PixelRect> rings)
    {
        var slotWidth = rings[0].Width;
        var tolerance = Math.Max(3, slotWidth / 6);

        // THE TWO MOST POPULOUS x positions, not the widest gap between them. Splitting at the widest gap
        // was tried and a single stray circle destroyed it: a false positive at x=503 sat 1,152px from the
        // real columns at 1,655 and 1,793, so the widest gap fell either side of the OUTLIER and the split
        // put one stray in one column and both real columns in the other. Detection went from 14 slots to 8.
        //
        // Population is immune to that. A grid column holds several slots; a stray holds one.
        var columns = new List<List<int>>();

        foreach (var x in rings.Select(r => r.X).OrderBy(x => x))
        {
            var bucket = columns.FirstOrDefault(c => Math.Abs(c[0] - x) <= tolerance);

            if (bucket is null)
            {
                columns.Add([x]);
            }
            else
            {
                bucket.Add(x);
            }
        }

        if (columns.Count < 2)
        {
            return [];
        }

        var ordered = columns
            .OrderByDescending(c => c.Count)
            .Take(2)
            .OrderBy(c => c[0])
            .ToArray();

        // Two columns each holding a single slot is not a grid; it is two strays that happen to align.
        if (ordered[0].Count + ordered[1].Count < MinimumSlots)
        {
            return [];
        }

        var leftCentre = Median(ordered[0].ToArray());
        var rightCentre = Median(ordered[1].ToArray());

        var onGrid = rings
            .Select(r => (
                Ring: r,
                Column: Math.Abs(r.X - leftCentre) <= tolerance ? 0
                    : Math.Abs(r.X - rightCentre) <= tolerance ? 1
                    : -1))
            .Where(entry => entry.Column >= 0)
            .OrderBy(entry => entry.Ring.Y)
            .ToList();

        if (onGrid.Count < MinimumSlots)
        {
            return [];
        }

        // Rows banded by half a slot height, so the two columns of one row stay together even when a pixel
        // apart vertically.
        var band = Math.Max(1, slotWidth / 2);
        var rows = new List<int>();
        var row = 0;

        for (var i = 0; i < onGrid.Count; i++)
        {
            if (i > 0 && onGrid[i].Ring.Y - onGrid[i - 1].Ring.Y > band)
            {
                row++;
            }

            rows.Add(row);
        }

        return onGrid
            .Select((entry, i) => Describe(entry.Ring, rows[i], entry.Column))
            .OrderBy(slot => slot.Row)
            .ThenBy(slot => slot.Column)
            .ToArray();
    }

    private static int Median(int[] values)
    {
        Array.Sort(values);

        return values[values.Length / 2];
    }

    /// <summary>
    /// Turns a ring's bounding box into the regions worth hashing.
    ///
    /// <para>The disc is the ring box inset by the rim. The artwork square is inscribed in the disc, which
    /// is what keeps the ring out of the hash — leaving it in was what defeated normalisation last time:
    /// the ring is a second non-backdrop colour touching the crop edges, so the artwork bounding box grew
    /// to the whole tile and every slot came back unidentified at near-noise distances.</para>
    /// </summary>
    private static SlotRegion Describe(PixelRect ring, int row, int column)
    {
        // A twelfth of the diameter, from the measured ~6px rim on a ~114px slot, plus its dark outline.
        var rim = Math.Max(2, ring.Width / 12);
        var disc = new PixelRect(
            ring.X + rim,
            ring.Y + rim,
            Math.Max(1, ring.Width - (rim * 2)),
            Math.Max(1, ring.Height - (rim * 2)));

        // The square inscribed in the disc: radius / sqrt(2), so ~0.707 of the diameter. It clips a little
        // artwork at the extremes, and using the whole disc instead was tried and MEASURED WORSE — on a
        // slot verified by eye to hold the Sacred Tree Resurrection Ring, the inscribed square put that item
        // top of the ranking at 71 bits while the full disc dropped it out of the top three entirely. The
        // clipping costs less than the extra rarity-disc background costs.
        // 0.707 is 1/sqrt(2) — the square inscribed in the disc — and it is a MEASURED optimum as well as a
        // geometric one. Sweeping the hashed fraction from 0.50 to 1.00 against a verified tile put the
        // correct item at rank 1284, 942, 3, **1**, 5, 151, 830 and 312 respectively, peaking sharply here.
        // The peak landing exactly on the inscribed square is the evidence that the game draws item art to
        // fill that square, rather than a parameter fitted to one sample.
        var sideX = Math.Max(1, (int)(disc.Width * 0.707));
        var sideY = Math.Max(1, (int)(disc.Height * 0.707));

        // Inset PER AXIS. Deriving the vertical inset from the width was a real bug: tiles are only
        // approximately square, so it pushed the crop off-centre vertically and cost two of the three
        // identifications the corrected version makes.
        return new SlotRegion(
            ring,
            disc,
            new PixelRect(
                disc.X + ((disc.Width - sideX) / 2),
                disc.Y + ((disc.Height - sideY) / 2),
                sideX,
                sideY),
            row,
            column);
    }

    /// <summary>
    /// Connected components of ring-coloured pixels that look like annuli.
    ///
    /// <para>Iterative flood fill with an explicit stack. A recursive one overflows on a real capture:
    /// several thousand connected pixels is an ordinary component here.</para>
    /// </summary>
    private static List<PixelRect> FindRingComponents(Bgra32Image image)
    {
        var visited = new bool[image.Width * image.Height];
        var found = new List<PixelRect>();
        var stack = new Stack<int>();

        // A slot is a substantial fraction of the window; anything tiny is furniture or noise. Scaled
        // from the image so this holds at any resolution.
        var minimumSide = Math.Max(12, Math.Min(image.Width, image.Height) / 60);
        var maximumSide = Math.Min(image.Width, image.Height) / 4;

        for (var seed = 0; seed < visited.Length; seed++)
        {
            if (visited[seed] || !IsRing(image, seed % image.Width, seed / image.Width))
            {
                continue;
            }

            var minX = int.MaxValue;
            var minY = int.MaxValue;
            var maxX = int.MinValue;
            var maxY = int.MinValue;
            var count = 0;

            stack.Push(seed);
            visited[seed] = true;

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                var x = current % image.Width;
                var y = current / image.Width;

                count++;

                if (x < minX) { minX = x; }
                if (x > maxX) { maxX = x; }
                if (y < minY) { minY = y; }
                if (y > maxY) { maxY = y; }

                // 8-connected: the rim is thin and diagonal steps keep it from breaking into arcs.
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx;
                        var ny = y + dy;

                        if (nx < 0 || ny < 0 || nx >= image.Width || ny >= image.Height)
                        {
                            continue;
                        }

                        var next = (ny * image.Width) + nx;

                        if (!visited[next] && IsRing(image, nx, ny))
                        {
                            visited[next] = true;
                            stack.Push(next);
                        }
                    }
                }
            }

            var width = maxX - minX + 1;
            var height = maxY - minY + 1;

            if (width < minimumSide || height < minimumSide || width > maximumSide || height > maximumSide)
            {
                continue;
            }

            if (Math.Abs(width - height) > Math.Max(width, height) * MaxAspectDeviation)
            {
                continue;
            }

            // An annulus is mostly hole. This is what rejects the sheet's solid bronze furniture, which
            // passes the colour and aspect tests perfectly well.
            if ((double)count / (width * height) > MaxFillRatio)
            {
                continue;
            }

            found.Add(new PixelRect(minX, minY, width, height));
        }

        return found;
    }

    private static bool IsRing(Bgra32Image image, int x, int y)
    {
        var offset = (y * image.Stride) + (x * Bgra32Image.BytesPerPixel);
        var b = image.Pixels[offset];
        var g = image.Pixels[offset + 1];
        var r = image.Pixels[offset + 2];

        // Warm, and mid-toned. The brightness band excludes the ring's dark outline below and specular
        // highlights above, both of which are warm but not the rim itself.
        return r - b >= WarmthThreshold
            && r - g >= WarmthThreshold / 2
            && r is >= 40 and <= 210;
    }
}

/// <summary>
/// One equipment slot, as three nested rectangles.
/// </summary>
/// <param name="Ring">The bronze annulus, which is what was actually detected.</param>
/// <param name="Disc">Inside the rim: the rarity-coloured field plus the artwork.</param>
/// <param name="Artwork">
/// The square inscribed in the disc — the only one of the three safe to hash, because it is the only one
/// that excludes the rim.
/// </param>
/// <param name="Row">Grid row, zero at the top.</param>
/// <param name="Column">Grid column: 0 left, 1 right.</param>
public readonly record struct SlotRegion(
    PixelRect Ring,
    PixelRect Disc,
    PixelRect Artwork,
    int Row,
    int Column);
