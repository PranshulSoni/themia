namespace Themia.Core.Widgets;

/// <summary>How a palette is drawn at a size.</summary>
public enum SwatchArrangement
{
    /// <summary>Nothing to draw: the palette is empty.</summary>
    Empty,

    /// <summary>A grid of colour chips with no text: a tile too narrow for a name beside a chip.</summary>
    Chips,

    /// <summary>Windows list rows — chip, name, value — in one or more columns.</summary>
    Rows,

    /// <summary>A grid of large colour blocks, each with its name and value under it.</summary>
    Tiles,
}

/// <summary>A palette's layout for one size.</summary>
/// <param name="Arrangement">Which of the three shapes.</param>
/// <param name="Columns">Columns of chips, rows or tiles.</param>
/// <param name="Cell">Rows: the row height. Chips: the chip's side. Tiles: the tile's height (block + caption).</param>
/// <param name="ShowValue">Whether the value fits beside (rows) or under (tiles) the name.</param>
public readonly record struct SwatchPlan(SwatchArrangement Arrangement, int Columns, double Cell, bool ShowValue);

/// <summary>
/// [S-1506] The palette's disclosure ladder: chips only on a narrow tile, list rows that gain a
/// value and then columns as it widens and grow taller (up to <see cref="RowMax"/>) to fill the
/// height, and — once even tall rows would leave much of the tile empty — tiles: big colour blocks
/// with the name and value under each. More room shows the colours larger, never a half-empty tile.
/// <para>
/// Pure, so the boundaries are tested here; the view only builds what the plan says.
/// </para>
/// </summary>
public static class SwatchLayout
{
    /// <summary>A narrower slot has no room for a name beside a chip.</summary>
    public const double ChipsBelowWidth = 180;

    /// <summary>A column narrower than this cannot hold a chip, a name and a value.</summary>
    public const double MinColumnWidth = 210;

    /// <summary>Four columns of rows is already a lot to scan.</summary>
    public const int MaxColumns = 4;

    /// <summary>Under this column width the value no longer fits beside the name.</summary>
    public const double ValueHidesBelow = 150;

    /// <summary>The standard Fluent row.</summary>
    public const double RowMin = 40;

    /// <summary>Rows grow to fill the height up to this; past it they read as bands, and tiles take over.</summary>
    public const double RowMax = 56;

    /// <summary>The smallest chip in the chip grid; more colours than fit at it scroll.</summary>
    public const double MinChip = 16;

    /// <summary>A tile's block must be at least this on its short side, or rows are the better shape.</summary>
    public const double MinTileBlock = 64;

    /// <summary>The gap between chips and between tiles.</summary>
    public const double Gap = 8;

    /// <param name="width">The list's width (the slot: rows run edge to edge).</param>
    /// <param name="height">The list's height.</param>
    /// <param name="count">How many colours.</param>
    /// <param name="showValues">The user's "show values" setting.</param>
    /// <param name="caption">Height of a tile's caption (name + value lines) below its block.</param>
    public static SwatchPlan Plan(double width, double height, int count, bool showValues, double caption)
    {
        if (count <= 0)
        {
            return new SwatchPlan(SwatchArrangement.Empty, 0, 0, false);
        }

        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            // Not measured yet: one column of standard rows, which the first real size replaces.
            return new SwatchPlan(SwatchArrangement.Rows, 1, RowMin, false);
        }

        if (width < ChipsBelowWidth)
        {
            var (columns, side) = ChipGrid(width, height, count);
            return new SwatchPlan(SwatchArrangement.Chips, columns, side, false);
        }

        var rowColumns = Math.Clamp((int)(width / MinColumnWidth), 1, MaxColumns);
        var perColumn = (int)Math.Ceiling(count / (double)rowColumns);
        var showValue = showValues && (width / rowColumns) >= ValueHidesBelow;

        // Every colour fits even at the tallest row, and rows that tall still leave over a fifth
        // of the tile empty: big tiles use it, where rows would leave it blank.
        if (perColumn * RowMax < height * 0.8 && TileGrid(width, height, count, caption) is { } tiles)
        {
            return new SwatchPlan(SwatchArrangement.Tiles, tiles.Columns, tiles.Height, showValues);
        }

        // Rows fill the height when the palette is short enough to, rather than stopping at 40 and
        // leaving the rest of the tile empty; a long palette keeps 40 and scrolls.
        var row = Math.Clamp(Math.Floor(height / perColumn), RowMin, RowMax);
        return new SwatchPlan(SwatchArrangement.Rows, rowColumns, row, showValue);
    }

    /// <summary>
    /// The chip grid: the column count that gives the largest square chips while showing every
    /// colour, floored at <see cref="MinChip"/> (past which the grid scrolls).
    /// </summary>
    public static (int Columns, double Side) ChipGrid(double width, double height, int count)
    {
        var best = (Columns: 1, Side: 0.0);

        for (var columns = 1; columns <= count; columns++)
        {
            var rows = (int)Math.Ceiling(count / (double)columns);
            var side = Math.Floor(Math.Min(
                (width - (Gap * (columns - 1))) / columns,
                (height - (Gap * (rows - 1))) / rows));

            if (side > best.Side)
            {
                best = (columns, side);
            }
        }

        if (best.Side >= MinChip)
        {
            return best;
        }

        // Too many to show at once: as many MinChip columns as the width holds, and scroll.
        var fit = Math.Max(1, (int)((width + Gap) / (MinChip + Gap)));
        return (Math.Min(fit, count), MinChip);
    }

    /// <summary>
    /// The tile grid: the column count whose blocks have the largest short side, or null when no
    /// count gives a block of at least <see cref="MinTileBlock"/>.
    /// </summary>
    public static (int Columns, double Height)? TileGrid(double width, double height, int count, double caption)
    {
        (int Columns, double Height, double Short)? best = null;

        for (var columns = 1; columns <= count; columns++)
        {
            var rows = (int)Math.Ceiling(count / (double)columns);
            var cellWidth = (width - (Gap * (columns - 1))) / columns;
            var cellHeight = Math.Floor((height - (Gap * (rows - 1))) / rows);
            var shortSide = Math.Min(cellWidth, cellHeight - caption);

            if (shortSide >= MinTileBlock && (best is null || shortSide > best.Value.Short))
            {
                best = (columns, cellHeight, shortSide);
            }
        }

        return best is { } found ? (found.Columns, found.Height) : null;
    }
}
