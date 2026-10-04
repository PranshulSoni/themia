namespace Themia.Core.Model;

/// <summary>
/// Which widgets belong to which container, and what may be put inside what.
/// <para>
/// Membership is a <see cref="WidgetInstance.ParentId"/> on the child, never a list of children on
/// the container, so a widget cannot be in two containers at once and a container cannot name a
/// child that has been deleted. Every question about the resulting tree is answered here, as a pure
/// function over a view, because the interesting cases — a cycle, a container deleted with children
/// still in it, a parent id left behind by a widget that moved to another display — are exactly the
/// ones that are painful to reproduce through the UI and easy to pin in a test.
/// </para>
/// </summary>
public static class WidgetTree
{
    /// <summary>
    /// How deep containers may nest.
    /// <para>
    /// A limit rather than none: every level costs a layout pass, and a desktop widget nested five
    /// deep is not a layout anyone reasoned about. Cycles are refused outright, so this only bounds
    /// legitimate nesting.
    /// </para>
    /// </summary>
    public const int MaxDepth = 3;

    /// <summary>What kind of container a widget is, if any.</summary>
    public static ContainerKind ContainerOf(WidgetKind kind) => kind switch
    {
        WidgetKind.Group => ContainerKind.Group,
        WidgetKind.Stack => ContainerKind.Stack,
        WidgetKind.Tabs => ContainerKind.Tabs,
        _ => ContainerKind.None,
    };

    /// <summary>Whether a widget holds other widgets.</summary>
    public static bool IsContainer(WidgetKind kind) => ContainerOf(kind) != ContainerKind.None;

    /// <summary>
    /// Whether the container lays its children out itself, rather than letting them keep their own
    /// place on the grid. A stack and a tab strip own their children's geometry; a group does not.
    /// </summary>
    public static bool OwnsChildGeometry(ContainerKind kind)
        => kind is ContainerKind.Stack or ContainerKind.Tabs;

    /// <summary>The widgets sitting directly on the desktop, in view order.</summary>
    public static List<WidgetInstance> Roots(this ViewState view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return view.Widgets.FindAll(w => w.ParentId is null || Find(view, w.ParentId) is null);
    }

    /// <summary>
    /// The direct children of a container, in view order.
    /// <para>
    /// View order, not an order of their own: the list is the single source of ordering for a stack
    /// and a tab strip, so reordering a child is a move within the view rather than a second index
    /// that could disagree with it.
    /// </para>
    /// </summary>
    public static List<WidgetInstance> ChildrenOf(this ViewState view, string containerId)
    {
        ArgumentNullException.ThrowIfNull(view);
        return string.IsNullOrEmpty(containerId)
            ? []
            : view.Widgets.FindAll(w => string.Equals(w.ParentId, containerId, StringComparison.Ordinal));
    }

    /// <summary>Find a widget in a view by id.</summary>
    public static WidgetInstance? Find(this ViewState view, string? id)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        foreach (var widget in view.Widgets)
        {
            if (string.Equals(widget.Id, id, StringComparison.Ordinal))
            {
                return widget;
            }
        }

        return null;
    }

    /// <summary>
    /// How deep a widget sits. A root is 0. A parent id that leads nowhere is treated as a root,
    /// because an orphan is on the desktop, not in limbo.
    /// </summary>
    public static int DepthOf(this ViewState view, string? id)
    {
        ArgumentNullException.ThrowIfNull(view);

        var depth = 0;
        var current = view.Find(id);

        // Bounded by the widget count: a cycle that got onto disk must not spin here, and this runs
        // on a layout pass.
        var guard = view.Widgets.Count + 1;

        while (current?.ParentId is { } parentId && guard-- > 0)
        {
            var parent = view.Find(parentId);
            if (parent is null)
            {
                break;
            }

            depth++;
            current = parent;
        }

        return depth;
    }

    /// <summary>Whether <paramref name="candidateId"/> is the widget itself or anywhere below it.</summary>
    public static bool IsSelfOrDescendant(this ViewState view, string widgetId, string? candidateId)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (string.IsNullOrEmpty(candidateId))
        {
            return false;
        }

        var current = view.Find(candidateId);
        var guard = view.Widgets.Count + 1;

        while (current is not null && guard-- > 0)
        {
            if (string.Equals(current.Id, widgetId, StringComparison.Ordinal))
            {
                return true;
            }

            current = view.Find(current.ParentId);
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="childId"/> may be put into <paramref name="containerId"/>.
    /// <para>
    /// Refused when the target is not a container, when it is the widget itself or something already
    /// inside it (which would detach a whole branch from the tree into a ring nothing can reach), or
    /// when it would nest deeper than <see cref="MaxDepth"/>.
    /// </para>
    /// </summary>
    public static bool CanReparent(this ViewState view, string childId, string? containerId)
    {
        ArgumentNullException.ThrowIfNull(view);

        var child = view.Find(childId);
        if (child is null)
        {
            return false;
        }

        // Moving to the desktop is always allowed: there is nothing to cycle through.
        if (string.IsNullOrEmpty(containerId))
        {
            return true;
        }

        var container = view.Find(containerId);
        if (container is null || !IsContainer(container.Kind))
        {
            return false;
        }

        if (view.IsSelfOrDescendant(childId, containerId))
        {
            return false;
        }

        // The deepest thing inside the child comes along with it, so the whole branch has to fit.
        return view.DepthOf(containerId) + 1 + view.HeightOf(childId) <= MaxDepth;
    }

    /// <summary>How many levels of container sit below a widget. A leaf is 0.</summary>
    public static int HeightOf(this ViewState view, string widgetId)
    {
        ArgumentNullException.ThrowIfNull(view);

        var tallest = 0;

        foreach (var child in view.ChildrenOf(widgetId))
        {
            var height = 1 + view.HeightOf(child.Id);
            if (height > tallest)
            {
                tallest = height;
            }
        }

        return tallest;
    }

    /// <summary>
    /// Put a widget into a container, or back on the desktop with a null container. Returns false and
    /// changes nothing when <see cref="CanReparent"/> refuses.
    /// </summary>
    public static bool Reparent(this ViewState view, string childId, string? containerId)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (!view.CanReparent(childId, containerId))
        {
            return false;
        }

        var child = view.Find(childId);
        if (child is null)
        {
            return false;
        }

        child.ParentId = string.IsNullOrEmpty(containerId) ? null : containerId;
        return true;
    }

    /// <summary>
    /// Remove a container and hand its children to whatever held it.
    /// <para>
    /// Children are <b>kept</b>, never deleted with the container. Deleting a group would otherwise
    /// take a dozen widgets the user spent an afternoon placing, on one click, on a surface with no
    /// undo — and "I removed the box, not the things in it" is what anyone would expect.
    /// </para>
    /// </summary>
    public static void Dissolve(this ViewState view, string containerId)
    {
        ArgumentNullException.ThrowIfNull(view);

        var container = view.Find(containerId);
        var promoteTo = container?.ParentId;

        foreach (var child in view.ChildrenOf(containerId))
        {
            child.ParentId = promoteTo;
        }
    }

    /// <summary>
    /// Repair the membership of a loaded view.
    /// <para>
    /// The state file is a trust boundary: it can name a parent that does not exist, a parent that is
    /// not a container, or a cycle — none of which the UI can produce but all of which a hand edit,
    /// a partial write or a widget moved between displays can leave behind. Every one becomes a
    /// widget on the desktop, which is a state the layout can always draw. Returns how many were
    /// repaired, so the loader can say so rather than repairing silently.
    /// </para>
    /// </summary>
    public static int RepairTree(this ViewState view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var repaired = 0;

        foreach (var widget in view.Widgets)
        {
            if (widget.ParentId is null)
            {
                continue;
            }

            var parent = view.Find(widget.ParentId);

            if (parent is null || !IsContainer(parent.Kind) || view.IsSelfOrDescendant(widget.Id, widget.ParentId))
            {
                widget.ParentId = null;
                repaired++;
            }
        }

        // Depth is checked after the cycles are gone, or walking it could not terminate.
        foreach (var widget in view.Widgets)
        {
            if (widget.ParentId is not null && view.DepthOf(widget.Id) > MaxDepth)
            {
                widget.ParentId = null;
                repaired++;
            }
        }

        return repaired;
    }
}
