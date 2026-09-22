namespace Themia.Core.Geometry;

/// <summary>
/// Pure layout maths: snapping, clamping, resize and placement. No UI types and no I/O, so every
/// rule that decides where a widget ends up is testable without a window.
/// </summary>
public static class GridGeometry
{
    /// <summary>[S-08] Minimum on-screen widget size, in device-independent pixels.</summary>
    public const double MinWidgetPx = 64;

    /// <summary>Target size of one snap cell, in device-independent pixels.</summary>
    public const double MinCellPx = 40;

    /// <summary>
    /// [S-06] Snap-grid granularity for a screen. The grid is an <b>aid</b>, never storage: it only
    /// rounds the values chosen during an interaction.
    /// </summary>
    public static (int Cols, int Rows) GridDims(ScreenMetrics screen)
    {
        if (!screen.IsUsable)
        {
            return (1, 1);
        }

        var cols = Math.Max(1, (int)Math.Round(screen.Width / MinCellPx));
        var rows = Math.Max(1, (int)Math.Round(screen.Height / MinCellPx));
        return (cols, rows);
    }

    /// <summary>[S-08] The pixel floor expressed as a fraction of the given screen.</summary>
    public static (double MinW, double MinH) MinFraction(ScreenMetrics screen)
    {
        if (!screen.IsUsable)
        {
            return (0.01, 0.01);
        }

        return (Math.Min(1, MinWidgetPx / screen.Width), Math.Min(1, MinWidgetPx / screen.Height));
    }

    /// <summary>
    /// [S-09] Clamp a rect fully on-screen and above the [S-08] size floor. Total: a non-finite or
    /// inverted input collapses to a valid rect instead of propagating NaN into storage.
    /// </summary>
    public static FracRect Clamp(FracRect r, ScreenMetrics screen)
    {
        var (minW, minH) = MinFraction(screen);

        var w = r.IsFinite ? Math.Clamp(r.W, minW, 1) : minW;
        var h = r.IsFinite ? Math.Clamp(r.H, minH, 1) : minH;
        var x = r.IsFinite ? Math.Clamp(r.X, 0, 1 - w) : 0;
        var y = r.IsFinite ? Math.Clamp(r.Y, 0, 1 - h) : 0;

        return new FracRect(x, y, w, h);
    }

    /// <summary>[S-06] Round a rect onto the snap grid. Storage stays the raw fraction.</summary>
    public static FracRect Snap(FracRect r, ScreenMetrics screen)
    {
        if (!r.IsFinite)
        {
            return r;
        }

        var (cols, rows) = GridDims(screen);
        var (minW, minH) = MinFraction(screen);

        var w = Math.Max(minW, Math.Round(r.W * cols) / cols);
        var h = Math.Max(minH, Math.Round(r.H * rows) / rows);
        var x = Math.Round(r.X * cols) / cols;
        var y = Math.Round(r.Y * rows) / rows;

        return new FracRect(x, y, w, h);
    }

    /// <summary>Snap when the aid is enabled (Alt suppresses it mid-drag), then always clamp.</summary>
    public static FracRect Settle(FracRect r, ScreenMetrics screen, bool snapEnabled)
        => Clamp(snapEnabled ? Snap(r, screen) : r, screen);

    /// <summary>Move a rect by a fractional delta. Size is untouched.</summary>
    public static FracRect Move(FracRect start, double dxFrac, double dyFrac)
        => start with { X = start.X + dxFrac, Y = start.Y + dyFrac };

    /// <summary>
    /// [S-31] Resize by dragging one handle. Every handle anchors the edges it does not move, so a
    /// side handle never shifts the opposite edge or the other axis. Edges that would cross stop at
    /// the [S-08] floor instead of inverting the rect.
    /// </summary>
    public static FracRect Resize(
        FracRect start,
        ResizeHandle handle,
        double dxFrac,
        double dyFrac,
        ScreenMetrics screen)
    {
        var (minW, minH) = MinFraction(screen);

        double left = start.X, top = start.Y, right = start.Right, bottom = start.Bottom;

        if (MovesLeft(handle))
        {
            left = Math.Min(left + dxFrac, right - minW);
        }

        if (MovesRight(handle))
        {
            right = Math.Max(right + dxFrac, left + minW);
        }

        if (MovesTop(handle))
        {
            top = Math.Min(top + dyFrac, bottom - minH);
        }

        if (MovesBottom(handle))
        {
            bottom = Math.Max(bottom + dyFrac, top + minH);
        }

        return FracRect.FromEdges(left, top, right, bottom);
    }

    public static bool MovesLeft(ResizeHandle h)
        => h is ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft;

    public static bool MovesRight(ResizeHandle h)
        => h is ResizeHandle.TopRight or ResizeHandle.Right or ResizeHandle.BottomRight;

    public static bool MovesTop(ResizeHandle h)
        => h is ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight;

    public static bool MovesBottom(ResizeHandle h)
        => h is ResizeHandle.BottomLeft or ResizeHandle.Bottom or ResizeHandle.BottomRight;

    /// <summary>[S-11] Placement: centre a default-sized widget on the drop point, then snap and clamp.</summary>
    public static FracRect PlaceAt(
        double xFrac,
        double yFrac,
        double defaultWidthPx,
        double defaultHeightPx,
        ScreenMetrics screen,
        bool snapEnabled)
    {
        var size = FracRect.FromPixels(0, 0, defaultWidthPx, defaultHeightPx, screen);
        var raw = new FracRect(xFrac - (size.W / 2), yFrac - (size.H / 2), size.W, size.H);
        return Settle(raw, screen, snapEnabled);
    }

    /// <summary>
    /// [S-04] Resolve a stored fraction to a pixel rect, inset on every side by half the widget gap
    /// so neighbours show a gap without changing their logical rect. Logical edges are rounded to a
    /// shared whole-pixel boundary <i>first</i>, so adjacent widgets still meet exactly.
    /// </summary>
    public static PixelRect ToPixels(FracRect r, ScreenMetrics screen, double gapPx)
    {
        var left = Math.Round(r.X * screen.Width);
        var top = Math.Round(r.Y * screen.Height);
        var right = Math.Round(r.Right * screen.Width);
        var bottom = Math.Round(r.Bottom * screen.Height);

        var half = gapPx / 2;
        left += half;
        top += half;
        right -= half;
        bottom -= half;

        return new PixelRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }
}

public readonly record struct PixelRect(double X, double Y, double Width, double Height);
