namespace Themia.Core.Widgets;

/// <summary>How a stack lays out its rows.</summary>
public enum StackArrangement
{
    /// <summary>The accordion: one row per child, down the tile, the open child under its row.</summary>
    Rows,

    /// <summary>A wide strip: the rows side by side along the top, the open child under them all.</summary>
    Strip,
}

/// <summary>[S-865] One stack layout: the arrangement, whether rows carry icons, and the open child's room.</summary>
/// <param name="Arrangement">Rows down the tile, or a strip across it.</param>
/// <param name="Icons">Whether the rows show the child's icon beside its name.</param>
/// <param name="BodyShown">Whether the open child is drawn (a sliver too small to read is not).</param>
/// <param name="BodyHeight">The open child's height when shown.</param>
public readonly record struct StackPlan(StackArrangement Arrangement, bool Icons, bool BodyShown, double BodyHeight);

/// <summary>How the tabs of a tab strip are labelled.</summary>
public enum TabLabels
{
    /// <summary>The child's name.</summary>
    Text,

    /// <summary>The child's icon and name: a wide tile with room for both.</summary>
    IconAndText,

    /// <summary>The child's icon alone: a strip too narrow for the names.</summary>
    Icon,
}

/// <summary>
/// [S-865]/[S-869] The containers' disclosure ladders. A container's content is its chrome — rows,
/// tabs — and the room it leaves its children, so the ladder decides that chrome from the slot.
/// </summary>
public static class ContainerLayout
{
    /// <summary>[S-865] Below this width a stack row is the name alone (XS).</summary>
    public const double StackIconMinWidth = 160;

    /// <summary>[S-865] An open child gets at least this much under its row, or the stack looks for a better arrangement.</summary>
    public const double StackComfortableBody = 100;

    /// <summary>[S-865] Less than this is a sliver, not a widget: the open child is not drawn.</summary>
    public const double StackMinBody = 40;

    /// <summary>
    /// [S-865] The stack's ladder. Rows always: names at XS, icon and name from S. The open child
    /// takes what the rows leave; when that is under <see cref="StackComfortableBody"/> on a wide
    /// tile the rows turn into a strip along the top so the child gets the height; when it is under
    /// <see cref="StackMinBody"/> anyway the child is not drawn (its row still says it is open).
    /// </summary>
    public static StackPlan Plan(WidgetArea area, int count, bool anyOpen, double rowHeight)
    {
        var icons = area.Width >= StackIconMinWidth;
        if (!anyOpen || count <= 0)
        {
            return new StackPlan(StackArrangement.Rows, icons, false, 0);
        }

        var under = area.Height - (count * rowHeight);
        if (under >= StackComfortableBody)
        {
            return new StackPlan(StackArrangement.Rows, icons, true, under);
        }

        var strip = area.Height - rowHeight;
        if (area.Shape == AreaShape.Wide && strip > under && strip >= StackMinBody)
        {
            return new StackPlan(StackArrangement.Strip, icons, true, strip);
        }

        return under >= StackMinBody
            ? new StackPlan(StackArrangement.Rows, icons, true, under)
            : new StackPlan(StackArrangement.Rows, icons, false, 0);
    }

    /// <summary>
    /// [S-866] Where a dragged row lands: the number of the <i>other</i> rows whose centre (measured
    /// before the drag began) lies before the pointer. Dropped back over itself it stays put.
    /// </summary>
    public static int DropSlot(IReadOnlyList<double> centres, int from, double at)
    {
        ArgumentNullException.ThrowIfNull(centres);

        var slot = 0;
        for (var i = 0; i < centres.Count; i++)
        {
            if (i != from && centres[i] < at)
            {
                slot++;
            }
        }

        return slot;
    }

    /// <summary>
    /// [S-869] How the tabs are labelled: icon and name on a wide tile (W ≥ Expanded) where they all
    /// fit, names where they fit, icons where only those fit; a strip of icons that still does not
    /// fit scrolls.
    /// </summary>
    /// <param name="width">The strip's width.</param>
    /// <param name="widthClass">The slot's width class.</param>
    /// <param name="textWidths">Each tab's name, measured.</param>
    /// <param name="icon">An icon's width.</param>
    /// <param name="chrome">A tab's padding plus the spacing to the next (and the icon's gap to its name).</param>
    public static TabLabels Tabs(double width, SizeClass widthClass, IReadOnlyList<double> textWidths, double icon, double chrome)
    {
        ArgumentNullException.ThrowIfNull(textWidths);

        var text = 0.0;
        foreach (var w in textWidths)
        {
            text += w + chrome;
        }

        var both = text + (textWidths.Count * (icon + chrome));
        if (widthClass >= SizeClass.Expanded && both <= width)
        {
            return TabLabels.IconAndText;
        }

        if (text <= width)
        {
            return TabLabels.Text;
        }

        return TabLabels.Icon;
    }
}
