namespace Themia.Core.Widgets;

/// <summary>How a strip of per-part bars is laid out: columns × rows, and whether each bar has room for its figure.</summary>
public readonly record struct CellGrid(int Columns, int Rows, bool Labels)
{
    public bool IsEmpty => Columns == 0 || Rows == 0;
}

/// <summary>
/// [S-1150] The per-core strip's arithmetic: as many bars per row as the width holds at their
/// narrowest, rows balanced so the last is not a stub, and a figure under each bar only where every
/// bar is wide enough for one. Eight cores on a 168-px column is one row; 64 on it is two.
/// </summary>
public static class MeterCellGrid
{
    public static CellGrid Fit(int count, double width, double minCell, double gap, double labelWidth)
    {
        if (count <= 0 || !double.IsFinite(width) || width <= 0 || minCell <= 0)
        {
            return default;
        }

        gap = Math.Max(0, gap);

        var perRow = Math.Max(1, (int)Math.Floor((width + gap + Disclosure.Tolerance) / (minCell + gap)));
        var rows = (count + perRow - 1) / perRow;

        // Balanced: 20 bars that fit 16 a row are two rows of 10, not 16 and a stub of 4.
        var columns = (count + rows - 1) / rows;
        var cell = (width - ((columns - 1) * gap)) / columns;

        return new CellGrid(columns, rows, labelWidth > 0 && cell + Disclosure.Tolerance >= labelWidth);
    }
}
