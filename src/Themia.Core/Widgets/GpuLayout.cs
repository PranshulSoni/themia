namespace Themia.Core.Widgets;

/// <summary>
/// [S-1207] How several adapters' panels share the GPU tile, decided from the room alone; each panel
/// then lays itself out as a meter does (<see cref="MeterFace"/>).
/// </summary>
public static class GpuLayout
{
    /// <summary>A panel narrower than this cannot set its name beside its number without cutting one of them.</summary>
    public const double ComfortableWidth = 240;

    /// <summary>A panel this tall has its name, its number and a strip of history.</summary>
    public const double ComfortableHeight = 90;

    /// <summary>
    /// [S-1207] How <paramref name="count"/> adapters' panels tile the area: the column count whose
    /// panels come closest to a comfortable size in their tighter direction, fewer columns on a tie.
    /// A strip puts two side by side (a stack of two 54 px slivers says nothing), a tall column stacks
    /// them. Every panel always gets a share: nothing scrolls.
    /// </summary>
    public static (int Columns, int Rows) Grid(WidgetArea area, int count, double gap)
    {
        if (count <= 1 || !area.IsMeasured)
        {
            return (1, Math.Max(1, count));
        }

        var best = (Columns: 1, Rows: count);
        var bestScore = double.NegativeInfinity;

        for (var columns = 1; columns <= count; columns++)
        {
            var rows = (count + columns - 1) / columns;
            var cell = Cell(area, columns, rows, gap);
            var score = Math.Min(cell.Width / ComfortableWidth, cell.Height / ComfortableHeight);

            if (score > bestScore + 1e-9)
            {
                bestScore = score;
                best = (columns, rows);
            }
        }

        return best;
    }

    /// <summary>The area of one panel in a grid of <paramref name="columns"/> × <paramref name="rows"/>, <paramref name="gap"/> apart.</summary>
    public static WidgetArea Cell(WidgetArea area, int columns, int rows, double gap)
    {
        var c = Math.Max(1, columns);
        var r = Math.Max(1, rows);
        var g = Math.Max(0, gap);

        return new WidgetArea(
            Math.Max(0, (area.Width - (g * (c - 1))) / c),
            Math.Max(0, (area.Height - (g * (r - 1))) / r));
    }
}
