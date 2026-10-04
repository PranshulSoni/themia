namespace Themia.Core.Geometry;

/// <summary>
/// [S-1584]..[S-1586] Geometry for several widgets that move as one: a dragged selection, and a
/// placement payload of one or many widgets.
/// <para>
/// A block moves by a single translation. Its bounding box is moved, its top-left corner snapped
/// onto the grid ([S-06]) and the box slid into the display's allowed area ([S-841]); the same
/// delta then applies to every rect, so the widgets keep their offsets to each other exactly.
/// Snapping or clamping each widget on its own would pull a carefully spaced row apart as soon as
/// one of them touched an edge.
/// </para>
/// <para>
/// A block is never resized: a move that would need a smaller box keeps the box's size and puts
/// its top-left at the allowed area's, and only then is each rect clamped on its own so none of them
/// is ever left off the screen ([S-09]).
/// </para>
/// </summary>
public static class BlockGeometry
{
    /// <summary>The bounding box of some rects; a zero rect for none.</summary>
    public static FracRect Bounds(IReadOnlyList<FracRect> rects)
    {
        ArgumentNullException.ThrowIfNull(rects);

        var left = double.PositiveInfinity;
        var top = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var bottom = double.NegativeInfinity;

        foreach (var r in rects)
        {
            if (!r.IsFinite)
            {
                continue;
            }

            left = Math.Min(left, r.X);
            top = Math.Min(top, r.Y);
            right = Math.Max(right, r.Right);
            bottom = Math.Max(bottom, r.Bottom);
        }

        return double.IsFinite(left) ? FracRect.FromEdges(left, top, right, bottom) : default;
    }

    /// <summary>
    /// [S-1584] The translation a drag of <paramref name="dxFrac"/>, <paramref name="dyFrac"/> really
    /// applies to a block whose rects started at <paramref name="start"/>: the box's corner snapped,
    /// then the box kept inside the allowed area.
    /// </summary>
    public static Delta Move(IReadOnlyList<FracRect> start, double dxFrac, double dyFrac, ScreenMetrics screen, bool snapEnabled)
        => Settle(Bounds(start), dxFrac, dyFrac, screen, snapEnabled);

    /// <summary>
    /// [S-1586] The translation that puts the centre of <paramref name="anchor"/> on a point, then
    /// settles the block as <see cref="Move"/> does. The anchor is the whole block's box for a paste
    /// (the block is centred on the pointer, [S-74]) and the grabbed widget for a drag handed to
    /// another display ([S-77]).
    /// </summary>
    public static Delta PlaceAt(IReadOnlyList<FracRect> rects, FracRect anchor, double xFrac, double yFrac, ScreenMetrics screen, bool snapEnabled)
    {
        var dx = double.IsFinite(xFrac) ? xFrac - (anchor.X + (anchor.W / 2)) : 0;
        var dy = double.IsFinite(yFrac) ? yFrac - (anchor.Y + (anchor.H / 2)) : 0;
        return Settle(Bounds(rects), dx, dy, screen, snapEnabled);
    }

    /// <summary>One rect of the block, moved by the block's delta and kept on the screen ([S-09]).</summary>
    public static FracRect Apply(FracRect rect, Delta delta, ScreenMetrics screen)
        => GridGeometry.Clamp(rect with { X = rect.X + delta.Dx, Y = rect.Y + delta.Dy }, screen);

    private static Delta Settle(FracRect box, double dx, double dy, ScreenMetrics screen, bool snapEnabled)
    {
        if (!box.IsFinite || !double.IsFinite(dx) || !double.IsFinite(dy))
        {
            return default;
        }

        var x = box.X + dx;
        var y = box.Y + dy;

        if (snapEnabled)
        {
            var (cols, rows) = GridGeometry.GridDims(screen);
            x = Math.Round(x * cols) / cols;
            y = Math.Round(y * rows) / rows;
        }

        // Slide the box, never shrink it: a box wider than the allowed area sits at its left edge.
        var a = screen.Allowed;
        x = Math.Clamp(x, a.X, Math.Max(a.X, a.Right - box.W));
        y = Math.Clamp(y, a.Y, Math.Max(a.Y, a.Bottom - box.H));

        return new Delta(x - box.X, y - box.Y);
    }

    /// <summary>A translation, in fractions of the screen.</summary>
    public readonly record struct Delta(double Dx, double Dy);
}
