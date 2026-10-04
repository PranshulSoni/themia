#if DEBUG
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-1489] The gallery's second look at four desk widgets whose default page shows only one of
/// their faces: Clock as an analog world clock (Tokyo, with the offset from local time, so the label,
/// the offset line and the dial beside its text on a strip all show); Sun &amp; moon at a southern
/// city, so the mirrored moon and the other hemisphere's day are seen; Quote with author and the
/// period caption; Scratchpad with a drawing on it. Debug builds only; reached through
/// <see cref="LayoutFixtures"/>.
/// </summary>
internal static class DeskFixtures
{
    public static bool Has(WidgetKind kind)
        => kind is WidgetKind.Clock or WidgetKind.SunMoon or WidgetKind.Scratchpad;

    public static WidgetConfig? Create(WidgetKind kind) => kind switch
    {
        WidgetKind.Clock => new ClockConfig
        {
            Face = ClockFace.Analog,
            TimeZoneId = "Asia/Tokyo",
            OffsetMode = ClockOffsetMode.Local,
            ShowSeconds = true,
        },
        WidgetKind.SunMoon => new SunMoonConfig
        {
            Place = "Buenos Aires",
            Latitude = -34.61,
            Longitude = -58.38,
        },
        WidgetKind.Scratchpad => Drawing(),
        _ => null,
    };

    /// <summary>A sun, a horizon and a wave in three of the palette's inks, in the pad's unit square.</summary>
    private static ScratchpadConfig Drawing()
    {
        var pad = new ScratchpadConfig();

        var sun = new List<InkPoint>();
        for (var i = 0; i <= 40; i++)
        {
            var angle = i / 40.0 * Math.PI * 2;
            sun.Add(new InkPoint(0.72 + (Math.Cos(angle) * 0.1), 0.3 + (Math.Sin(angle) * 0.14)));
        }

        var wave = new List<InkPoint>();
        for (var i = 0; i <= 60; i++)
        {
            var x = 0.06 + (i / 60.0 * 0.88);
            wave.Add(new InkPoint(x, 0.78 + (Math.Sin(x * 18) * 0.05)));
        }

        pad.Strokes.Add(new InkStroke { Color = "#F7630C", Width = 4, Points = InkGeometry.Pack(sun) });
        pad.Strokes.Add(new InkStroke { Color = "#0078D4", Width = 3, Points = InkGeometry.Pack(wave) });
        pad.Strokes.Add(new InkStroke
        {
            Color = "#10893E",
            Width = 3,
            Points = InkGeometry.Pack([new(0.1, 0.62), new(0.22, 0.42), new(0.34, 0.62), new(0.46, 0.5), new(0.58, 0.62)]),
        });

        return pad;
    }
}
#endif
