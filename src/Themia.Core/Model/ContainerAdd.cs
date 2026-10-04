using Themia.Core.Geometry;

namespace Themia.Core.Model;

/// <summary>
/// [S-951..S-953] Creating a widget directly inside a container, with no click-to-place step.
/// <para>
/// One write: the widget is created with its <see cref="WidgetInstance.ParentId"/> already set, never
/// added to the desktop and then reparented. Two steps would leave a stray widget on the desktop
/// whenever the second one was refused.
/// </para>
/// </summary>
public static class ContainerAdd
{
    /// <summary>Inset from the area's edges, as a share of the area, for a new member of a group.</summary>
    public const double AreaPad = 0.03;

    /// <summary>How far each further new member steps down and right, as a share of the area.</summary>
    public const double CascadeStep = 0.06;

    /// <summary>After this many steps the cascade starts over rather than march off the area.</summary>
    public const int CascadeSteps = 5;

    /// <summary>
    /// [S-951] Whether a new widget may be created inside <paramref name="containerId"/>: the target
    /// exists in this view, is a container, and one more level still fits under
    /// <see cref="WidgetTree.MaxDepth"/>. The new widget has nothing inside it yet, so the rule is
    /// the same for every kind, a container included, exactly as for moving an empty widget in.
    /// </summary>
    public static bool CanAddInto(this ViewState view, string? containerId)
    {
        ArgumentNullException.ThrowIfNull(view);

        var container = view.Find(containerId);
        return container is not null
            && WidgetTree.IsContainer(container.Kind)
            && view.DepthOf(containerId) + 1 <= WidgetTree.MaxDepth;
    }

    /// <summary>
    /// [S-952] Where a new member of a group goes: its default size, inset from the area's top-left
    /// (below <paramref name="topInset"/>, the room the group's own header takes), stepped down and
    /// right by how many members the group already has so a second add does not land exactly on the
    /// first, and always held inside the area. Ported from the original's <c>areaChildRect</c>.
    /// </summary>
    /// <param name="area">The group's rect.</param>
    /// <param name="width">The new widget's default width, as a fraction of the screen.</param>
    /// <param name="height">The new widget's default height, as a fraction of the screen.</param>
    /// <param name="index">How many members the group already has.</param>
    /// <param name="topInset">Room kept free at the top of the area, as a fraction of the screen.</param>
    public static FracRect AreaChildRect(FracRect area, double width, double height, int index, double topInset = 0)
    {
        var padX = area.W * AreaPad;
        var padY = area.H * AreaPad;
        var top = area.Y + Math.Clamp(Finite(topInset), 0, area.H) + padY;
        var bottom = area.Bottom - padY;

        var w = Math.Max(0, Math.Min(Finite(width), area.W - (2 * padX)));
        var h = Math.Max(0, Math.Min(Finite(height), bottom - top));

        var step = Math.Max(0, index) % CascadeSteps;
        var x = Math.Min(area.X + padX + (area.W * CascadeStep * step), area.Right - padX - w);
        var y = Math.Min(top + (area.H * CascadeStep * step), bottom - h);

        return Clamp01(new FracRect(Math.Max(area.X, x), Math.Max(area.Y, y), w, h));
    }

    /// <summary>
    /// [S-953] Create a widget of <paramref name="kind"/> inside a container and return it, or null
    /// (with nothing changed) when <see cref="CanAddInto"/> refuses.
    /// <para>
    /// In a stack or a tab strip the new widget's stored rect is the container's own: the container
    /// lays it out, so the rect is only where it returns to if it is ever taken out, and the
    /// container's footprint is a better place for that than the screen's corner. In a group it gets
    /// a real rect inside the area (<see cref="AreaChildRect"/>), because a group's members keep
    /// their own place on the grid. It goes last in view order, which is the end of a stack or the
    /// last tab.
    /// </para>
    /// </summary>
    /// <param name="width">The kind's default width, as a fraction of the screen.</param>
    /// <param name="height">The kind's default height, as a fraction of the screen.</param>
    /// <param name="topInset">Room a group keeps for its header, as a fraction of the screen.</param>
    public static WidgetInstance? AddInto(
        this ViewState view,
        string containerId,
        WidgetKind kind,
        double width,
        double height,
        double topInset = 0)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (!view.CanAddInto(containerId) || view.Find(containerId) is not { } container)
        {
            return null;
        }

        var rect = WidgetTree.OwnsChildGeometry(WidgetTree.ContainerOf(container.Kind))
            ? container.Rect
            : AreaChildRect(container.Rect, width, height, view.ChildrenOf(container.Id).Count, topInset);

        var widget = WidgetInstance.Create(kind, rect);
        widget.ParentId = container.Id;
        view.Widgets.Add(widget);
        return widget;
    }

    private static double Finite(double value) => double.IsFinite(value) ? value : 0;

    private static FracRect Clamp01(FracRect r)
    {
        var x = Math.Clamp(Finite(r.X), 0, 1);
        var y = Math.Clamp(Finite(r.Y), 0, 1);
        return new FracRect(x, y, Math.Clamp(Finite(r.W), 0, 1 - x), Math.Clamp(Finite(r.H), 0, 1 - y));
    }
}
