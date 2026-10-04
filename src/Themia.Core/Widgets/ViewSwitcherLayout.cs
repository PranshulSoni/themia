using Themia.Core.Geometry;

namespace Themia.Core.Widgets;

/// <summary>[S-1653] What one switcher button shows, poorest first.</summary>
public enum SwitcherFace
{
    /// <summary>The view's icon or initials alone.</summary>
    Mark,

    /// <summary>Icon or initials, and the name beside them.</summary>
    Named,

    /// <summary>The name, and a caption under it: "3 widgets".</summary>
    Detailed,

    /// <summary>A view card: a sketch of the view's layout, the name and the caption.</summary>
    Card,
}

/// <summary>[S-1656] Where a view card puts its sketch: over the name, or beside it.</summary>
public enum CardShape
{
    Above,
    Beside,
}

/// <summary>
/// [S-1653] How the switcher lays out its buttons: rows of <paramref name="Columns"/> cells, the last
/// row holding what is left, every cell of the same <paramref name="Face"/>.
/// </summary>
/// <param name="Columns">Cells in every row but the last.</param>
/// <param name="Rows">How many rows.</param>
/// <param name="Cells">How many cells are drawn, the overflow cell included.</param>
/// <param name="Face">What every cell shows.</param>
/// <param name="Overflow">[S-1655] The last cell is "+N", a menu of the views with no cell of their own.</param>
public readonly record struct SwitcherLayout(int Columns, int Rows, int Cells, SwitcherFace Face, bool Overflow)
{
    /// <summary>How many views have a cell of their own.</summary>
    public int Views => Overflow ? Math.Max(0, Cells - 1) : Cells;

    /// <summary>One cell per row.</summary>
    public bool Vertical => Columns == 1 && Rows > 1;

    public bool ShowNames => Face >= SwitcherFace.Named;

    /// <summary>The row a cell sits in.</summary>
    public int RowOf(int cell) => Columns <= 0 ? 0 : cell / Columns;

    /// <summary>How many cells a row holds: <see cref="Columns"/>, or what is left on the last row.</summary>
    public int CellsInRow(int row) => row < Rows - 1 ? Columns : Math.Max(0, Cells - (Columns * (Rows - 1)));
}

/// <summary>A cell's size in DIPs.</summary>
public readonly record struct CellSize(double Width, double Height)
{
    public double Aspect => Height > 0 ? Width / Height : 0;
}

/// <summary>Where the keyboard cursor is after a key, and whether the key meant anything to the switcher.</summary>
public readonly record struct SwitcherCursor(int? Index, bool Used);

/// <summary>One widget in a view's sketch, in DIPs from the sketch's top-left.</summary>
public readonly record struct SketchTile(double X, double Y, double Width, double Height);

/// <summary>[S-1656] A view's layout drawn small: the screen's box and its widgets inside it.</summary>
public sealed record ViewSketch(double Width, double Height, IReadOnlyList<SketchTile> Tiles)
{
    public static ViewSketch Empty { get; } = new(0, 0, []);
}

/// <summary>
/// [S-1653] The view switcher's ladder: how many cells, in what grid, showing how much. Pure, so the
/// choice at every size is tested without a window.
/// <para>
/// The original only ever drew one row. A ladder of faces — initials, names, a "3 widgets" caption,
/// view cards with a sketch of the layout — is what lets the same widget be a strip, a menu and,
/// at L and XL, a page of views, instead of three buttons stretched across empty space.
/// </para>
/// </summary>
public static class ViewSwitcherLayout
{
    /// <summary>The padding round the cells, and the gap between them (Fluent's Space1).</summary>
    public const double Inset = 4;

    public const double Gap = 4;

    /// <summary>The smallest cell anything is drawn in: an icon or two letters, and room to hit it.</summary>
    public const double MinCell = 32;

    /// <summary>The narrowest cell that carries a name beside its icon.</summary>
    public const double NamedMinWidth = 88;

    /// <summary>The narrowest cell that carries a name over a caption.</summary>
    public const double DetailedMinWidth = 120;

    /// <summary>The narrowest card with the sketch over the name.</summary>
    public const double CardMinWidth = 140;

    /// <summary>The narrowest card with the sketch beside the name (sketch, gap, a name's room).</summary>
    public const double BesideMinWidth = 240;

    /// <summary>The smallest sketch worth drawing: below this a layout is a smudge.</summary>
    public const double SketchMinHeight = 64;

    /// <summary>The padding inside a detailed button or a card, top and bottom.</summary>
    public const double CellPad = 8;

    /// <summary>The shapes each face reads best at, width over height (compared on a log scale).</summary>
    public const double MarkAspect = 1;

    public const double ButtonAspect = 3;

    public const double CardAboveAspect = 1.3;

    public const double CardBesideAspect = 3;

    /// <summary>A name over a caption, inside the cell's padding.</summary>
    public static double DetailedMinHeight => (2 * CellPad) + FluentType.Body.LineHeight + FluentType.Caption.LineHeight;

    /// <summary>A sketch, a gap, a name and a caption, inside the cell's padding.</summary>
    public static double CardAboveMinHeight => DetailedMinHeight + SketchMinHeight + Gap;

    /// <summary>A sketch the height of the cell, inside its padding.</summary>
    public static double CardBesideMinHeight => (2 * CellPad) + SketchMinHeight;

    /// <summary>
    /// [S-1653] Arrange <paramref name="count"/> views in <paramref name="area"/> (the view's whole
    /// slot): every grid from one row to one column is tried, and the one whose cells carry the
    /// richest face wins; between equals, the one whose cells are closest to that face's shape.
    /// The last row holds what is left and its cells widen to fill it, so a grid never leaves an
    /// empty cell. When not even <see cref="MinCell"/> squares fit every view, [S-1655] the cells
    /// that fit are filled in order and the last becomes "+N".
    /// </summary>
    public static SwitcherLayout Plan(WidgetArea area, int count, bool iconOnly)
    {
        if (count <= 0 || !area.IsMeasured)
        {
            return new SwitcherLayout(1, 0, 0, SwitcherFace.Mark, Overflow: false);
        }

        SwitcherLayout? best = null;
        var bestCost = double.MaxValue;

        for (var columns = 1; columns <= count; columns++)
        {
            var rows = (count + columns - 1) / columns;
            var plan = new SwitcherLayout(columns, rows, count, SwitcherFace.Card, Overflow: false);

            if (FaceOf(area, plan, iconOnly) is not { } face)
            {
                continue;
            }

            plan = plan with { Face = face };
            var cost = Cost(area, plan);

            if (best is not { } current || face > current.Face || (face == current.Face && cost < bestCost - 1e-9))
            {
                best = plan;
                bestCost = cost;
            }
        }

        return best ?? Overflowing(area, count);
    }

    /// <summary>[S-1655] As many <see cref="MinCell"/> squares as fit, the last one "+N"; one at the least.</summary>
    private static SwitcherLayout Overflowing(WidgetArea area, int count)
    {
        var inner = area.Deflate(Inset, Inset);
        var columns = Math.Max(1, Disclosure.WholeRows(inner.Width, MinCell, Gap));
        var rows = Math.Max(1, Disclosure.WholeRows(inner.Height, MinCell, Gap));
        var capacity = columns * rows;

        if (capacity >= count)
        {
            // Only reachable through rounding at the tolerance: the cells fit after all.
            columns = Math.Min(columns, count);
            return new SwitcherLayout(columns, (count + columns - 1) / columns, count, SwitcherFace.Mark, Overflow: false);
        }

        return new SwitcherLayout(columns, rows, capacity, SwitcherFace.Mark, Overflow: true);
    }

    /// <summary>The size of the cells in <paramref name="row"/> of <paramref name="plan"/>, inside the inset.</summary>
    public static CellSize Cell(WidgetArea area, SwitcherLayout plan, int row)
    {
        var inner = area.Deflate(Inset, Inset);
        var across = Math.Max(1, plan.CellsInRow(row));
        var rows = Math.Max(1, plan.Rows);

        return new CellSize(
            Math.Max(0, (inner.Width - (Gap * (across - 1))) / across),
            Math.Max(0, (inner.Height - (Gap * (rows - 1))) / rows));
    }

    /// <summary>The richest face one cell can carry, or null when it is smaller than <see cref="MinCell"/>.</summary>
    public static SwitcherFace? FaceFor(CellSize cell, bool iconOnly)
    {
        if (!Fits(cell.Width, MinCell) || !Fits(cell.Height, MinCell))
        {
            return null;
        }

        if (iconOnly)
        {
            return SwitcherFace.Mark;
        }

        if (CanCardAbove(cell) || CanCardBeside(cell))
        {
            return SwitcherFace.Card;
        }

        if (Fits(cell.Width, DetailedMinWidth) && Fits(cell.Height, DetailedMinHeight))
        {
            return SwitcherFace.Detailed;
        }

        return Fits(cell.Width, NamedMinWidth) ? SwitcherFace.Named : SwitcherFace.Mark;
    }

    /// <summary>
    /// [S-1656] Sketch over the name in a squarish card, beside it in a wide one — whichever the
    /// card's shape is closer to; a card that can hold only one shape gets that one.
    /// </summary>
    public static CardShape CardShapeFor(CellSize cell)
    {
        var above = CanCardAbove(cell);
        var beside = CanCardBeside(cell);

        if (beside && (!above || Off(cell.Aspect, CardBesideAspect) < Off(cell.Aspect, CardAboveAspect)))
        {
            return CardShape.Beside;
        }

        return CardShape.Above;
    }

    private static bool CanCardAbove(CellSize cell) => Fits(cell.Width, CardMinWidth) && Fits(cell.Height, CardAboveMinHeight);

    private static bool CanCardBeside(CellSize cell) => Fits(cell.Width, BesideMinWidth) && Fits(cell.Height, CardBesideMinHeight);

    private static bool Fits(double length, double needed) => double.IsFinite(length) && length + Disclosure.Tolerance >= needed;

    /// <summary>The poorest face any row of the plan can carry: every cell shows the same.</summary>
    private static SwitcherFace? FaceOf(WidgetArea area, SwitcherLayout plan, bool iconOnly)
    {
        SwitcherFace? face = null;

        for (var row = 0; row < plan.Rows; row++)
        {
            if (FaceFor(Cell(area, plan, row), iconOnly) is not { } rowFace)
            {
                return null;
            }

            face = face is { } so && so < rowFace ? so : rowFace;
        }

        return face;
    }

    /// <summary>How far, on average per cell, the cells are from the face's own shape.</summary>
    private static double Cost(WidgetArea area, SwitcherLayout plan)
    {
        var total = 0.0;

        for (var row = 0; row < plan.Rows; row++)
        {
            var cell = Cell(area, plan, row);
            var off = plan.Face switch
            {
                SwitcherFace.Mark => Off(cell.Aspect, MarkAspect),
                SwitcherFace.Card => Math.Min(
                    CanCardAbove(cell) ? Off(cell.Aspect, CardAboveAspect) : double.MaxValue,
                    CanCardBeside(cell) ? Off(cell.Aspect, CardBesideAspect) : double.MaxValue),
                _ => Off(cell.Aspect, ButtonAspect),
            };

            total += off * plan.CellsInRow(row);
        }

        return plan.Cells > 0 ? total / plan.Cells : 0;
    }

    /// <summary>Distance between two shapes on a log scale, so "twice too wide" and "twice too tall" count the same.</summary>
    private static double Off(double aspect, double ideal)
        => aspect > 0 && double.IsFinite(aspect) ? Math.Abs(Math.Log(aspect / ideal)) : double.MaxValue;

    /// <summary>
    /// [S-1652] The keyboard cursor after <paramref name="key"/>. Left/Right step through the cells
    /// in reading order; Up/Down step a row in a grid and a cell in a single row or column; all
    /// clamped (the switcher is not a carousel). Home/End jump to the ends; Escape clears. With no
    /// cursor yet, an arrow lands on <paramref name="home"/> — the cell of the view this display
    /// shows — so the first press starts from where the user is. Enter, Space and the Menu key keep
    /// the cursor and are used only when there is one: the caller acts on that cell.
    /// </summary>
    public static SwitcherCursor Step(SwitcherLayout plan, int? current, int? home, WidgetKey key)
    {
        var cells = plan.Cells;
        if (cells <= 0)
        {
            return new SwitcherCursor(null, false);
        }

        int? at = current is { } c && c >= 0 && c < cells ? c : null;

        switch (key)
        {
            case WidgetKey.Escape:
                return new SwitcherCursor(null, at is not null);

            case WidgetKey.Enter or WidgetKey.Space or WidgetKey.Menu:
                return new SwitcherCursor(at, at is not null);

            case WidgetKey.Home:
                return new SwitcherCursor(0, true);

            case WidgetKey.End:
                return new SwitcherCursor(cells - 1, true);

            case WidgetKey.Left or WidgetKey.Right or WidgetKey.Up or WidgetKey.Down:
                break;

            default:
                return new SwitcherCursor(at, false);
        }

        if (at is not { } i)
        {
            var forward = key is WidgetKey.Right or WidgetKey.Down;
            return new SwitcherCursor(home is { } h && h >= 0 && h < cells ? h : forward ? 0 : cells - 1, true);
        }

        var grid = plan.Columns > 1 && plan.Rows > 1;

        var next = key switch
        {
            WidgetKey.Left => Math.Max(0, i - 1),
            WidgetKey.Right => Math.Min(cells - 1, i + 1),
            WidgetKey.Up when grid => i - plan.Columns >= 0 ? i - plan.Columns : i,
            WidgetKey.Down when grid => i + plan.Columns < cells ? i + plan.Columns : plan.RowOf(i) < plan.Rows - 1 ? cells - 1 : i,
            WidgetKey.Up => Math.Max(0, i - 1),
            _ => Math.Min(cells - 1, i + 1),
        };

        return new SwitcherCursor(next, true);
    }

    /// <summary>
    /// [S-1656] A view's layout drawn into a box: the screen at <paramref name="aspect"/> (width over
    /// height; an unusable one is 16:9), as large as fits, and each widget's rect inside it. Rects
    /// are fractions of the screen ([S-01]) and are cut to it; one with no area left, or not finite,
    /// is left out rather than drawn as a sliver.
    /// </summary>
    public static ViewSketch Sketch(IReadOnlyList<FracRect> tiles, double boxWidth, double boxHeight, double aspect)
    {
        ArgumentNullException.ThrowIfNull(tiles);

        if (!double.IsFinite(boxWidth) || !double.IsFinite(boxHeight) || boxWidth <= 0 || boxHeight <= 0)
        {
            return ViewSketch.Empty;
        }

        var shape = double.IsFinite(aspect) && aspect > 0 ? Math.Clamp(aspect, 0.25, 4) : 16.0 / 9.0;
        var width = boxWidth;
        var height = width / shape;

        if (height > boxHeight)
        {
            height = boxHeight;
            width = height * shape;
        }

        var drawn = new List<SketchTile>(tiles.Count);

        foreach (var tile in tiles)
        {
            if (!double.IsFinite(tile.X) || !double.IsFinite(tile.Y) || !double.IsFinite(tile.W) || !double.IsFinite(tile.H))
            {
                continue;
            }

            var left = Math.Clamp(tile.X, 0, 1);
            var top = Math.Clamp(tile.Y, 0, 1);
            var right = Math.Clamp(tile.X + tile.W, 0, 1);
            var bottom = Math.Clamp(tile.Y + tile.H, 0, 1);

            if (right <= left || bottom <= top)
            {
                continue;
            }

            drawn.Add(new SketchTile(left * width, top * height, (right - left) * width, (bottom - top) * height));
        }

        return new ViewSketch(width, height, drawn);
    }
}
