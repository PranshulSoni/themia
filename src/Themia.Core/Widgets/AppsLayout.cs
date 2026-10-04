using Themia.Core.Model;

namespace Themia.Core.Widgets;

/// <summary>
/// [S-1527] The Apps widget's disclosure ladder: what it shows for the room it has. Pure, so every
/// tier is pinned by a test.
/// <para>
/// The items always come first — even XS shows the first app icons — and the parts around them are
/// rungs: the filter box (from a Regular height), the folder headings (from a Regular height, in
/// tiles and rows), the "N apps" line (L) and the folder chips (XL). The tiles and rows themselves
/// come from the same planner the Shortcuts widget uses, so a wide tile shows more columns, a large
/// one steps the tiles up once, and a two-column list appears on the widest tiles: one tiling rule
/// for both launchers.
/// </para>
/// </summary>
public static class AppsLayout
{
    /// <summary>The tile and row size the planner starts from; L and XL step it up on their own.</summary>
    public const ShortcutItemSize BaseSize = ShortcutItemSize.Medium;

    /// <summary>A Medium list row without a second line (the planner's own Medium height).</summary>
    public const double MediumRow = 40;

    /// <summary>Below this slot width compact rows drop the folder column: the name is what a launcher is for.</summary>
    public const double CategoryColumnFrom = 320;

    /// <summary>[S-1527] The filter box: from a Regular height (M, Tall, L, XL), never on a strip.</summary>
    public static bool Filter(WidgetArea area) => area.HeightClass >= SizeClass.Regular;

    /// <summary>[S-1527] The "N apps · N hidden" line under the list: L and XL.</summary>
    public static bool Footer(WidgetArea area) => area.AtLeast(SizeClass.Expanded, SizeClass.Expanded);

    /// <summary>[S-1527] A row of folder chips that narrows the list to one folder: XL only.</summary>
    public static bool Chips(WidgetArea area) => area.AtLeast(SizeClass.Huge, SizeClass.Huge);

    /// <summary>
    /// [S-1521] Folder headings: in tiles and rows, with more than one section, from a Regular
    /// height. A strip or a small tile spends its few rows on apps, not on titles.
    /// </summary>
    public static bool Headers(WidgetArea area, AppsViewMode view, int sections)
        => view != AppsViewMode.Compact && sections > 1 && area.HeightClass >= SizeClass.Regular;

    /// <summary>[S-1520] Tile <paramref name="count"/> apps into the box left for them.</summary>
    public static ShortcutTilePlan Tiles(WidgetArea area, double width, double height, int count)
        => ShortcutLayout.Grid(width, height, count, ShortcutLayout.GridSize(BaseSize, area), iconOnly: false, stretch: false, detail: false);

    /// <summary>
    /// [S-1520] Rows for the list and compact views. A list row carries the folder as a caption line
    /// on the large tiles — unless the headings already say it, when a wide list runs in two columns
    /// instead. Compact rows are the small size with no icon and no second line.
    /// </summary>
    public static ShortcutRowPlan Rows(WidgetArea area, AppsViewMode view, bool headers)
    {
        if (view == AppsViewMode.Compact)
        {
            var compact = ShortcutLayout.Rows(area, ShortcutItemSize.Small, details: false);
            return compact with { Icon = 0, SecondLine = false };
        }

        var plan = ShortcutLayout.Rows(area, BaseSize, details: false);

        if (!headers)
        {
            return plan;
        }

        // The headings say the folder, so the row drops its second line (and the height it took),
        // and a list of short names runs in two columns from an Expanded width rather than leaving
        // the right half of a large tile empty.
        return plan with
        {
            SecondLine = false,
            RowHeight = plan.SecondLine ? MediumRow : plan.RowHeight,
            Columns = area.WidthClass >= SizeClass.Expanded ? 2 : plan.Columns,
        };
    }

    /// <summary>
    /// [S-1521] The folder as a right-hand column: compact rows, and list rows that have neither
    /// headings nor a second line to say it — where the slot is wide enough for it.
    /// </summary>
    public static bool CategoryColumn(WidgetArea area, AppsViewMode view, bool headers, ShortcutRowPlan rows)
        => view != AppsViewMode.Grid
            && !headers
            && !rows.SecondLine
            && rows.Columns == 1
            && area.Width >= CategoryColumnFrom;
}
