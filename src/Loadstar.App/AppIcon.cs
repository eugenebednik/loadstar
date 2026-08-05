namespace Loadstar.App;

/// <summary>
/// The application icon, drawn rather than shipped as an asset so the app stays a single file.
///
/// <para>Shared, and that is the point: this was previously built privately inside
/// <see cref="TrayApplication"/> for the tray only, so every actual <b>window</b> fell back to the
/// stock WinForms icon — the settings dialog and the taskbar button both showed a generic
/// placeholder while the tray showed the real mark.</para>
///
/// <para>Built once and cached for the process lifetime. <c>Bitmap.GetHicon</c> hands back an
/// unmanaged handle that would normally want <c>DestroyIcon</c>, and reaching for that would mean
/// adding a <c>user32</c> P/Invoke — which the anti-cheat posture test would flag, correctly, since
/// it cannot tell a housekeeping call from a dangerous one. One handle held for the lifetime of the
/// process is the cheaper answer than widening that allowlist.</para>
/// </summary>
internal static class AppIcon
{
    private static Icon? _shared;

    public static Icon Shared => _shared ??= Build();

    private static Icon Build()
    {
        using var bitmap = new Bitmap(32, 32);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            g.FillEllipse(new SolidBrush(Color.FromArgb(32, 40, 56)), 1, 1, 30, 30);

            // A four-point star: "loadstar".
            var star = new[]
            {
                new PointF(16, 3), new PointF(19.5f, 12.5f), new PointF(29, 16), new PointF(19.5f, 19.5f),
                new PointF(16, 29), new PointF(12.5f, 19.5f), new PointF(3, 16), new PointF(12.5f, 12.5f),
            };

            g.FillPolygon(new SolidBrush(Color.FromArgb(255, 214, 102)), star);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }
}
