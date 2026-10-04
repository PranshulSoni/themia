using Themia.Core.Geometry;
using Themia.Core.Model;

namespace Themia.Core.Motion;

/// <summary>Which axis a sheet intro slides along.</summary>
public enum SheetAxis
{
    /// <summary>Left and right: <see cref="IntroAnimation.Sides"/>.</summary>
    X,

    /// <summary>Top and bottom: <see cref="IntroAnimation.TopBottom"/>.</summary>
    Y,
}

/// <summary>The two sheets' start offsets in DIPs: negative off the near (left/top) edge, positive off the far one.</summary>
public readonly record struct SheetOffsets(double Near, double Far);

/// <summary>
/// [S-1004] The sheet intros: a view slides in from two opposite screen edges as two <b>rigid</b>
/// sheets — Sides along x, Top and bottom along y. One algorithm, parametrised by the axis.
/// <para>
/// A widget joins the near sheet when its centre is in the first half of the axis, otherwise the far
/// one (a dead-centre widget goes far). Every member of a sheet starts from the <b>same</b> offset,
/// sized so the deepest member clears the screen plus a margin: per-widget offsets would give each
/// tile its own speed, and the sheet would arrive smeared out behind itself.
/// </para>
/// </summary>
public static class IntroSheets
{
    /// <summary>Travel past the screen edge, so borders and shadows are off-screen on the first frame.</summary>
    public const double MarginPx = 24;

    /// <summary>The axis a style slides along, or null when it is not a sheet style. The one place that decides it.</summary>
    public static SheetAxis? AxisFor(IntroAnimation style) => style switch
    {
        IntroAnimation.Sides => SheetAxis.X,
        IntroAnimation.TopBottom => SheetAxis.Y,
        _ => null,
    };

    /// <summary>-1 for the near sheet (left / top), +1 for the far one (right / bottom), by the widget's centre.</summary>
    public static int Direction(FracRect rect, SheetAxis axis)
    {
        var (start, size) = Span(rect, axis);
        return start + (size / 2) < 0.5 ? -1 : 1;
    }

    /// <summary>
    /// Each sheet's one start offset, given every grid-placed widget on the view and the surface's
    /// extent along the axis in DIPs. A sheet with no members gets 0; nothing reads it.
    /// </summary>
    public static SheetOffsets Offsets(IEnumerable<FracRect> rects, SheetAxis axis, double extent)
    {
        ArgumentNullException.ThrowIfNull(rects);
        var e = double.IsFinite(extent) ? Math.Max(0, extent) : 0;
        var nearReach = double.NegativeInfinity; // the largest far edge among near-sheet widgets
        var farReach = double.PositiveInfinity;  // the smallest near edge among far-sheet widgets

        foreach (var rect in rects)
        {
            var (start, size) = Span(rect, axis);
            if (Direction(rect, axis) < 0)
            {
                nearReach = Math.Max(nearReach, start + size);
            }
            else
            {
                farReach = Math.Min(farReach, start);
            }
        }

        return new SheetOffsets(
            double.IsNegativeInfinity(nearReach) ? 0 : -Math.Round((nearReach * e) + MarginPx),
            double.IsPositiveInfinity(farReach) ? 0 : Math.Round(((1 - farReach) * e) + MarginPx));
    }

    /// <summary>Where one widget starts, as a translation in DIPs: its sheet's offset along the axis.</summary>
    public static (double X, double Y) StartFor(FracRect rect, SheetAxis axis, SheetOffsets sheets)
    {
        var offset = Direction(rect, axis) < 0 ? sheets.Near : sheets.Far;
        return axis == SheetAxis.X ? (offset, 0) : (0, offset);
    }

    private static (double Start, double Size) Span(FracRect rect, SheetAxis axis)
        => axis == SheetAxis.X ? (rect.X, rect.W) : (rect.Y, rect.H);
}
