namespace Themia.Core.Widgets;

/// <summary>Where the power tile's status strip goes.</summary>
public enum PowerStripPlacement
{
    None = 0,

    /// <summary>Lines under the buttons.</summary>
    Below = 1,

    /// <summary>A column of lines to the right of the buttons (a wide strip of a tile).</summary>
    Beside = 2,

    /// <summary>[S-1249] A side panel of fact rows and the power plans (the largest tiles).</summary>
    Panel = 3,
}

/// <summary>The button grid: how many buttons, in how many columns and rows, at what cell size.</summary>
/// <param name="Shown">Buttons drawn, from the start of the list. Fewer than asked only when not even icons fit.</param>
/// <param name="Columns">Grid columns.</param>
/// <param name="Rows">Grid rows.</param>
/// <param name="Labels">Whether names go under the icons.</param>
/// <param name="CellWidth">One cell's width.</param>
/// <param name="CellHeight">One cell's height.</param>
public readonly record struct PowerGrid(int Shown, int Columns, int Rows, bool Labels, double CellWidth, double CellHeight)
{
    public static PowerGrid Empty { get; } = new(0, 0, 0, false, 0, 0);
}

/// <summary>The whole tile: the grid, and the strip beside or under it.</summary>
/// <param name="Grid">The buttons.</param>
/// <param name="GridWidth">Width the grid occupies.</param>
/// <param name="GridHeight">Height the grid occupies.</param>
/// <param name="Strip">Where the status goes.</param>
/// <param name="StripLines">How many status lines (for <see cref="PowerStripPlacement.Below"/>/<see cref="PowerStripPlacement.Beside"/>).</param>
/// <param name="StripWidth">Width of a side strip or panel; 0 below.</param>
public readonly record struct PowerTilePlan(
    PowerGrid Grid,
    double GridWidth,
    double GridHeight,
    PowerStripPlacement Strip,
    int StripLines,
    double StripWidth);

/// <summary>
/// [S-1248] The power tile's disclosure ladder, as arithmetic.
/// <para>
/// The buttons come first: every chosen action with its name, then every action as an icon, then as
/// many icons as fit. The status strip only takes room the buttons do not need at that rung — it
/// never costs a label or a button. A wide strip of a tile puts the status beside the buttons; the
/// largest tiles turn it into a side panel. The grid itself picks the column count whose cells are
/// squarest (the original's <c>auto-fit</c> grid, minus the reflow surprises).
/// </para>
/// </summary>
public static class PowerTileLayout
{
    /// <summary>The original's <c>minmax(58px, 1fr)</c> labelled cell, widened so "Shut down" and "Display off" fit in Caption untrimmed.</summary>
    public const double LabelledMinWidth = 74;

    /// <summary>Icon (20) + gap + Caption line (16) + padding.</summary>
    public const double LabelledMinHeight = 56;

    /// <summary>An icon-only cell: a 16-20px glyph with a pointer target around it.</summary>
    public const double IconMinWidth = 32;

    public const double IconMinHeight = 28;

    /// <summary>
    /// Past this height extra cell height no longer counts toward squareness when choosing the
    /// column count. Cells still stretch to fill the box: empty space on a big tile is a defect.
    /// </summary>
    public const double MaxCellHeight = 132;

    /// <summary>One status line.</summary>
    public const double StripLineHeight = 20;

    /// <summary>A side strip's narrowest and widest.</summary>
    public const double StripMinWidth = 120;

    public const double StripMaxWidth = 220;

    /// <summary>A side panel's narrowest and widest.</summary>
    public const double PanelMinWidth = 240;

    public const double PanelMaxWidth = 340;

    /// <summary>
    /// Fit <paramref name="count"/> buttons into a box. Labelled if wanted and every button fits
    /// labelled; else icons; else as many icons as fit.
    /// </summary>
    public static PowerGrid FitGrid(int count, double width, double height, bool labelsWanted, double gap)
    {
        if (count <= 0 || !(width > 0) || !(height > 0))
        {
            return PowerGrid.Empty;
        }

        if (labelsWanted && TryGrid(count, width, height, labels: true, gap) is { } labelled)
        {
            return labelled;
        }

        for (var shown = count; shown >= 1; shown--)
        {
            if (TryGrid(shown, width, height, labels: false, gap) is { } icons)
            {
                return icons;
            }
        }

        return PowerGrid.Empty;
    }

    /// <summary>
    /// Plan the tile. <paramref name="stripWanted"/> is how many status lines there are to show (0
    /// for none); <paramref name="allowStrip"/>, <paramref name="allowBeside"/> and
    /// <paramref name="allowPanel"/> are the size-class gates the view derives from its area.
    /// </summary>
    public static PowerTilePlan Plan(
        int count,
        double width,
        double height,
        bool labelsWanted,
        int stripWanted,
        bool allowStrip,
        bool allowBeside,
        bool allowPanel,
        double gap)
    {
        var alone = FitGrid(count, width, height, labelsWanted, gap);
        var bare = Placed(alone, width, height, gap, PowerStripPlacement.None, 0, 0);

        if (alone.Shown == 0 || !allowStrip || stripWanted <= 0)
        {
            return bare;
        }

        // [S-1249] The largest tiles: buttons left, a panel of facts and plans right.
        if (allowPanel)
        {
            var panel = Math.Clamp(width * 0.42, PanelMinWidth, PanelMaxWidth);
            var left = width - panel - gap;
            var grid = FitGrid(count, left, height, labelsWanted, gap);

            if (Same(grid, alone))
            {
                return Placed(grid, left, height, gap, PowerStripPlacement.Panel, stripWanted, panel);
            }
        }

        // A wide strip of a tile: status in a column beside the buttons, as many lines as stand.
        if (allowBeside)
        {
            var strip = Math.Clamp(width * 0.28, StripMinWidth, StripMaxWidth);
            var left = width - strip - gap;
            var lines = Math.Min(stripWanted, Disclosure.WholeRows(height, StripLineHeight));
            var grid = FitGrid(count, left, height, labelsWanted, gap);

            if (lines > 0 && Same(grid, alone))
            {
                return Placed(grid, left, height, gap, PowerStripPlacement.Beside, lines, strip);
            }
        }

        // Under the buttons: the most lines that leave the buttons as they were.
        for (var lines = stripWanted; lines >= 1; lines--)
        {
            var room = height - (lines * StripLineHeight) - gap;
            var grid = FitGrid(count, width, room, labelsWanted, gap);

            if (Same(grid, alone))
            {
                return Placed(grid, width, room, gap, PowerStripPlacement.Below, lines, 0);
            }
        }

        return bare;
    }

    /// <summary>Same rung: the same buttons, labelled the same way. Cell size may shrink.</summary>
    private static bool Same(PowerGrid candidate, PowerGrid alone)
        => candidate.Shown == alone.Shown && candidate.Labels == alone.Labels;

    private static PowerTilePlan Placed(PowerGrid grid, double width, double height, double gap, PowerStripPlacement strip, int lines, double stripWidth)
    {
        var gridHeight = grid.Rows == 0 ? 0 : Math.Min(height, (grid.Rows * grid.CellHeight) + ((grid.Rows - 1) * gap));
        return new PowerTilePlan(grid, width, gridHeight, strip, lines, stripWidth);
    }

    /// <summary>
    /// The best column count for <paramref name="shown"/> buttons, or null when none fits: the one
    /// whose cells are squarest (largest of <c>min(width, height × 1.25)</c>, the height capped for that comparison only),
    /// ties to fewer columns.
    /// </summary>
    private static PowerGrid? TryGrid(int shown, double width, double height, bool labels, double gap)
    {
        var minWidth = labels ? LabelledMinWidth : IconMinWidth;
        var minHeight = labels ? LabelledMinHeight : IconMinHeight;

        PowerGrid? best = null;
        var bestScore = double.MinValue;

        for (var columns = 1; columns <= shown; columns++)
        {
            var rows = (shown + columns - 1) / columns;

            var cellWidth = (width - ((columns - 1) * gap)) / columns;
            var cellHeight = (height - ((rows - 1) * gap)) / rows;

            if (cellWidth + Disclosure.Tolerance < minWidth || cellHeight + Disclosure.Tolerance < minHeight)
            {
                continue;
            }

            var capped = Math.Min(cellHeight, MaxCellHeight);
            var score = Math.Min(cellWidth, capped * 1.25);

            if (score > bestScore + Disclosure.Tolerance)
            {
                bestScore = score;
                best = new PowerGrid(shown, columns, rows, labels, cellWidth, cellHeight);
            }
        }

        return best;
    }
}
