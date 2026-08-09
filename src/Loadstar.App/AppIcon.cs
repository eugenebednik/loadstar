using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Loadstar.App;

/// <summary>
/// The application mark: a gold guiding star on a deep blue disc. Drawn in code rather than loaded from an
/// asset, so it renders crisply at any size instead of being scaled from one bitmap.
///
/// <para><b>This is the single source of truth for the icon, including the one embedded in the executable.</b>
/// The <c>.ico</c> that Windows shows in Explorer, the Start Menu and Add/Remove Programs is generated from
/// <see cref="Render"/> by <c>--write-icon</c>, so there is no second copy of the design to drift out of step.
/// Before that existed the exe had no icon resource at all, and every shell surface fell back to the generic
/// Windows one while the tray showed the real mark.</para>
///
/// <para><b>Geometry is proportional, never pixel literals.</b> The same code has to hold up at 16px in a tray
/// and at 256px in Explorer's extra-large view, and the earlier version was written in absolute coordinates
/// for 32px only.</para>
///
/// <para>Built once and cached for the process lifetime. <c>Bitmap.GetHicon</c> hands back an unmanaged handle
/// that would normally want <c>DestroyIcon</c>, and reaching for that would mean adding a <c>user32</c>
/// P/Invoke — which the anti-cheat posture test would flag, correctly, since it cannot tell a housekeeping
/// call from a dangerous one. One handle held for the lifetime of the process is the cheaper answer than
/// widening that allowlist.</para>
/// </summary>
internal static class AppIcon
{
    private static Icon? _shared;

    /// <summary>
    /// The palette, taken verbatim from the design so the icon and its source stay comparable.
    /// </summary>
    private static readonly Color TileTop = Color.FromArgb(255, 0x18, 0x20, 0x30);
    private static readonly Color TileBottom = Color.FromArgb(255, 0x0A, 0x0D, 0x14);
    private static readonly Color RimLight = Color.FromArgb(255, 0x5C, 0x6B, 0x8A);
    private static readonly Color RimDark = Color.FromArgb(255, 0x15, 0x1A, 0x24);

    /// <summary>Three stops, light to deep, running diagonally — a two-stop ramp looked flat and brassy.</summary>
    private static readonly Color GoldLight = Color.FromArgb(255, 0xFF, 0xE0, 0x82);
    private static readonly Color GoldMid = Color.FromArgb(255, 0xFF, 0xB3, 0x00);
    private static readonly Color GoldDeep = Color.FromArgb(255, 0xE6, 0x51, 0x00);

    public static Icon Shared
    {
        get
        {
            if (_shared is null)
            {
                using var bitmap = Render(32);

                _shared = Icon.FromHandle(bitmap.GetHicon());
            }

            return _shared;
        }
    }

    /// <summary>
    /// Draws the mark at an arbitrary size. Caller owns the bitmap.
    /// </summary>
    public static Bitmap Render(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);

        // EVERY DIMENSION IS A FRACTION OF `size`, taken from the design's 256-unit grid. Writing them as
        // pixel literals is what tied the previous mark to 32px and made it fall apart everywhere else.
        var u = size / 256f;
        var inset = 8 * u;
        var tile = new RectangleF(inset, inset, size - (inset * 2), size - (inset * 2));

        using (var path = RoundedRect(tile, 52 * u))
        {
            using var fill = new LinearGradientBrush(tile, TileTop, TileBottom, 45f);

            g.FillPath(fill, path);

            // Rim drawn INSIDE the tile bounds. A centred stroke would put half its width outside the
            // rectangle, where the bitmap edge clips it and the corners come out flat.
            using var rim = new LinearGradientBrush(tile, RimLight, RimDark, 45f);
            using var pen = new Pen(rim, 8 * u) { Alignment = PenAlignment.Inset };

            g.DrawPath(pen, path);
        }

        using var gold = new LinearGradientBrush(tile, GoldLight, GoldDeep, 45f)
        {
            InterpolationColors = new ColorBlend
            {
                Colors = [GoldLight, GoldMid, GoldDeep],
                Positions = [0f, 0.5f, 1f],
            },
        };

        // The chevron: a waypoint marker pointing up. Round caps and joins, so the arms end in soft tips
        // rather than the chiselled ones a default flat cap gives.
        using (var pen = new Pen(gold, 24 * u)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        })
        {
            g.DrawLines(pen,
            [
                new PointF(48 * u, 148 * u),
                new PointF(128 * u, 68 * u),
                new PointF(208 * u, 148 * u),
            ]);
        }

        // The vertical spindle through it, which is what turns an arrow into a star-like waypoint.
        g.FillPolygon(gold,
        [
            new PointF(128 * u, 32 * u),
            new PointF(156 * u, 128 * u),
            new PointF(128 * u, 224 * u),
            new PointF(100 * u, 128 * u),
        ]);

        // The bright core. Last, so it sits over both gold shapes, and it is the one element that still
        // reads at 16px once the chevron has thinned to about a pixel.
        using (var core = new SolidBrush(Color.White))
        {
            var r = 16 * u;

            g.FillEllipse(core, (128 * u) - r, (128 * u) - r, r * 2, r * 2);
        }

        return bitmap;
    }

    /// <summary>
    /// A rectangle with rounded corners, as a path. GDI+ has no primitive for one.
    /// </summary>
    private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();

        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }

    /// <summary>
    /// The two bitmaps WixUI shows in the installer: the 493x58 banner across most dialogs, and the
    /// 493x312 panel behind the welcome and finish dialogs. Without them the installer uses WiX's stock
    /// artwork, which is a generic disk graphic and looks like somebody else's product.
    ///
    /// <para><b>The text areas are kept LIGHT on purpose.</b> WixUI draws its titles and body copy in a dark
    /// foreground at fixed positions, and it does not know what is behind them — so filling these bitmaps
    /// edge to edge with the app's dark navy would produce dark text on a dark panel and an unreadable
    /// installer. The dark brand colour is confined to the left strip of the panel and to the right end of
    /// the banner, which are the regions WixUI leaves empty.</para>
    ///
    /// <para>24-bit, not 32-bit: MSI's bitmap control ignores an alpha channel, and a 32bpp image with
    /// transparency renders with black fringing where it expected opaque pixels.</para>
    /// </summary>
    public static (Bitmap Banner, Bitmap Dialog) RenderInstallerArt()
    {
        return (Banner(), Dialog());
    }

    private static Bitmap Banner()
    {
        var bitmap = new Bitmap(493, 58, PixelFormat.Format24bppRgb);

        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Light, because the dialog title is drawn over the left of this strip in a dark colour.
        using (var wash = new LinearGradientBrush(
            new Rectangle(0, 0, 493, 58),
            Color.White,
            Color.FromArgb(255, 0xE8, 0xEC, 0xF4),
            0f))
        {
            g.FillRectangle(wash, 0, 0, 493, 58);
        }

        // The mark at the right end, clear of the title text on the left.
        using var mark = Render(40);

        g.DrawImage(mark, 493 - 40 - 12, 9, 40, 40);

        // A hairline along the bottom, which is what stops the banner floating away from the dialog body.
        using (var edge = new Pen(Color.FromArgb(255, 0xC8, 0xD0, 0xE0)))
        {
            g.DrawLine(edge, 0, 57, 493, 57);
        }

        return bitmap;
    }

    private static Bitmap Dialog()
    {
        var bitmap = new Bitmap(493, 312, PixelFormat.Format24bppRgb);

        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Right two thirds light: this is where the welcome and finish text goes, including the
        // "start Loadstar now" checkbox.
        using (var wash = new LinearGradientBrush(
            new Rectangle(0, 0, 493, 312),
            Color.White,
            Color.FromArgb(255, 0xEE, 0xF1, 0xF7),
            90f))
        {
            g.FillRectangle(wash, 0, 0, 493, 312);
        }

        // Left strip in the brand colour, the width WixUI's own artwork uses.
        const int strip = 164;

        using (var brand = new LinearGradientBrush(
            new Rectangle(0, 0, strip, 312),
            Color.FromArgb(255, 0x18, 0x20, 0x30),
            Color.FromArgb(255, 0x0A, 0x0D, 0x14),
            60f))
        {
            g.FillRectangle(brand, 0, 0, strip, 312);
        }

        using var mark = Render(96);

        g.DrawImage(mark, (strip - 96) / 2, 96, 96, 96);

        // A gold hairline down the seam, picking up the accent from the mark.
        using (var seam = new Pen(Color.FromArgb(120, 0xFF, 0xB3, 0x00)))
        {
            g.DrawLine(seam, strip, 0, strip, 312);
        }

        return bitmap;
    }

    /// <summary>
    /// Packs the mark into a Windows <c>.ico</c> at several resolutions.
    ///
    /// <para>Written by hand because <see cref="Icon"/> cannot save a multi-image icon — <c>Icon.Save</c>
    /// writes back only what it was constructed from. Shipping a single 32px image instead would leave
    /// Explorer's large and extra-large views upscaling a thumbnail, which is exactly the blurry result this
    /// is meant to avoid.</para>
    ///
    /// <para><b>Entries are uncompressed DIBs, not PNGs.</b> PNG inside .ico is legal and Windows Explorer
    /// reads it, but <see cref="Icon"/> itself does not — it throws "requested range extends past the end of
    /// the array" on a PNG entry. So a PNG icon cannot be loaded back by the very framework that draws this
    /// app's windows, and could not be verified by anything using System.Drawing either. DIB costs a few
    /// hundred kilobytes against a 62 MB installer and is understood by everything.</para>
    /// </summary>
    public static byte[] BuildIcoFile(params int[] sizes)
    {
        var images = sizes.Select(size =>
        {
            using var bitmap = Render(size);

            return EncodeDib(bitmap);
        }).ToList();

        using var file = new MemoryStream();
        using var writer = new BinaryWriter(file);

        // ICONDIR
        writer.Write((ushort)0);              // reserved
        writer.Write((ushort)1);              // type: 1 = icon
        writer.Write((ushort)images.Count);

        // Directory entries are fixed width, so the first image starts after all of them.
        var offset = 6 + (images.Count * 16);

        for (var i = 0; i < images.Count; i++)
        {
            // 256 is stored as 0 in a single byte, which is the format's way of encoding it.
            writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            writer.Write((byte)0);            // palette entries: 0 for true colour
            writer.Write((byte)0);            // reserved
            writer.Write((ushort)1);          // colour planes
            writer.Write((ushort)32);         // bits per pixel
            writer.Write(images[i].Length);
            writer.Write(offset);

            offset += images[i].Length;
        }

        foreach (var image in images)
        {
            writer.Write(image);
        }

        writer.Flush();

        return file.ToArray();
    }

    /// <summary>
    /// One icon entry as a bottom-up 32-bit DIB: <c>BITMAPINFOHEADER</c>, then BGRA pixels, then the AND mask.
    ///
    /// <para>Three details the format insists on, each of which produces a silently broken icon if missed.
    /// <b>Height is doubled</b> in the header, because it describes the colour image and the mask together.
    /// <b>Rows run bottom-up.</b> And <b>the mask must be present even at 32bpp</b> where alpha already
    /// carries transparency — it is all zeros, but omitting it leaves the entry short and Windows renders
    /// nothing.</para>
    /// </summary>
    private static byte[] EncodeDib(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;

        // 1bpp mask rows are padded to a 4-byte boundary, like every other DIB row.
        var maskStride = ((width + 31) / 32) * 4;

        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);

        writer.Write(40);                       // biSize
        writer.Write(width);                    // biWidth
        writer.Write(height * 2);               // biHeight — colour + mask
        writer.Write((ushort)1);                // biPlanes
        writer.Write((ushort)32);               // biBitCount
        writer.Write(0);                        // biCompression: BI_RGB
        writer.Write((width * height * 4) + (maskStride * height));
        writer.Write(0);                        // biXPelsPerMeter
        writer.Write(0);                        // biYPelsPerMeter
        writer.Write(0);                        // biClrUsed
        writer.Write(0);                        // biClrImportant

        for (var y = height - 1; y >= 0; y--)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);

                writer.Write(pixel.B);
                writer.Write(pixel.G);
                writer.Write(pixel.R);
                writer.Write(pixel.A);
            }
        }

        writer.Write(new byte[maskStride * height]);
        writer.Flush();

        return buffer.ToArray();
    }
}
