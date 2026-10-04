namespace Themia.Core.Widgets;

/// <summary>
/// One way to draw the per-core strip: its grid, how tall each bar is, whether each bar has its
/// figure under it and whether the strip has its caption ("24 logical processors") above.
/// </summary>
public readonly record struct CellStripForm(CellGrid Shape, double BarHeight, bool Labels, bool Title)
{
    public bool IsEmpty => Shape.IsEmpty;

    /// <summary>The strip's height: caption and its gap, then the rows of bars (and figures) with the gap between rows.</summary>
    public double Height => IsEmpty
        ? 0
        : (Title ? FluentType.Caption.LineHeight + MeterCellForms.TitleGap : 0)
          + (Shape.Rows * (BarHeight + (Labels ? FluentType.Caption.LineHeight : 0)))
          + ((Shape.Rows - 1) * MeterCellForms.Gap);
}

/// <summary>
/// [S-1622] The per-core strip offered in several sizes, richest first, so a tile that cannot hold
/// the full strip still shows one rather than dropping it for the process list. The full form
/// (tall bars, a figure under each on a tile Expanded in height) is <see cref="MeterCellGrid"/>'s
/// [S-1150]; below it come short bars with the caption, then short bars alone — the bars are the
/// information, the caption only names them.
/// </summary>
public static class MeterCellForms
{
    /// <summary>The narrowest a bar may get and still read as a bar.</summary>
    public const double MinCellWidth = 4;

    /// <summary>The gap between bars, across and down.</summary>
    public const double Gap = 2;

    /// <summary>The gap between the caption and the bars (Fluent's Space1).</summary>
    public const double TitleGap = 4;

    /// <summary>A bar on a tile Expanded in height, one row (Fluent's Space6 + Space2).</summary>
    public const double TallBar = 32;

    /// <summary>A bar in one row on a smaller tile (Space6).</summary>
    public const double Bar = 24;

    /// <summary>A bar in a strip of several rows, and the short fallback form (Space4).</summary>
    public const double ShortBar = 16;

    /// <summary>
    /// The forms for <paramref name="count"/> bars across <paramref name="width"/>, richest first and
    /// each strictly shorter than the one before. Empty when there is nothing to draw.
    /// </summary>
    /// <param name="tall">The tile is Expanded in height: taller bars, and figures where they fit.</param>
    /// <param name="labelWidth">How wide a bar must be to carry its figure; 0 never labels.</param>
    public static IReadOnlyList<CellStripForm> Ladder(int count, double width, bool tall, double labelWidth)
    {
        var shape = MeterCellGrid.Fit(count, width, MinCellWidth, Gap, labelWidth);

        if (shape.IsEmpty)
        {
            return [];
        }

        var full = new CellStripForm(shape, shape.Rows > 1 ? ShortBar : tall ? TallBar : Bar, shape.Labels && tall, Title: true);
        var forms = new List<CellStripForm> { full };

        foreach (var form in new[] { new CellStripForm(shape, ShortBar, false, Title: true), new CellStripForm(shape, ShortBar, false, Title: false) })
        {
            if (form.Height < forms[^1].Height)
            {
                forms.Add(form);
            }
        }

        return forms;
    }
}
