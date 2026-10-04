namespace Themia.Core.Model;

/// <summary>What a container's row (or tab, or settings row) can do to one of its children.</summary>
public enum ContainerChildAction
{
    /// <summary>Open the child's own settings.</summary>
    Configure,

    /// <summary>One place earlier among its siblings.</summary>
    MoveUp,

    /// <summary>One place later among its siblings.</summary>
    MoveDown,

    /// <summary>Back onto the desktop, at the rect it had before it went in ([S-411a]).</summary>
    TakeOut,
}

/// <summary>
/// [S-864]..[S-866] Which child of a stack is open, which tab is shown, and moving a child among its
/// siblings. Pure functions over ids and a view, so the rules the original pinned in
/// <c>stackModel</c> / <c>tabsModel</c> are pinned here too.
/// </summary>
public static class ContainerRules
{
    /// <summary>
    /// [S-864] The open child of a stack, or -1 for none. Every child starts closed; an id that names
    /// no child (it was taken out or deleted) is "nothing open" too, and is never rewritten, so it
    /// applies again if that child comes back.
    /// </summary>
    public static int OpenIndex(IReadOnlyList<string> childIds, string? openId)
    {
        ArgumentNullException.ThrowIfNull(childIds);
        return openId is null ? -1 : IndexOf(childIds, openId);
    }

    /// <summary>
    /// [S-864] The open id after a click on row <paramref name="clicked"/>: that child opens and
    /// whichever was open closes; a click on the open child's row closes it. A click outside the
    /// rows changes nothing.
    /// </summary>
    public static string? Toggle(IReadOnlyList<string> childIds, string? openId, int clicked)
    {
        ArgumentNullException.ThrowIfNull(childIds);

        if (clicked < 0 || clicked >= childIds.Count)
        {
            return openId;
        }

        return OpenIndex(childIds, openId) == clicked ? null : childIds[clicked];
    }

    /// <summary>
    /// [S-868] The shown tab: the selected child when it is one, otherwise the first; -1 only for an
    /// empty strip. A stale id is ignored, not rewritten.
    /// </summary>
    public static int SelectedIndex(IReadOnlyList<string> childIds, string? selectedId)
    {
        ArgumentNullException.ThrowIfNull(childIds);

        if (childIds.Count == 0)
        {
            return -1;
        }

        var index = selectedId is null ? -1 : IndexOf(childIds, selectedId);
        return index >= 0 ? index : 0;
    }

    /// <summary>[S-866] Whether a child can move <paramref name="delta"/> places among its siblings.</summary>
    public static bool CanMoveChild(this ViewState view, string childId, int delta)
        => SwapTarget(view, childId, delta) is not null;

    /// <summary>
    /// [S-866] Move a child one place among its siblings. The view's widget list is the one order a
    /// stack and a tab strip have ([S-410]), so this swaps the child with its neighbour <i>in that
    /// list</i>; nothing else moves. Returns false at either end.
    /// </summary>
    public static bool MoveChild(this ViewState view, string childId, int delta)
    {
        if (SwapTarget(view, childId, delta) is not var (from, to))
        {
            return false;
        }

        (view.Widgets[from], view.Widgets[to]) = (view.Widgets[to], view.Widgets[from]);
        return true;
    }

    /// <summary>
    /// [S-866] Put a child at <paramref name="index"/> among its siblings (clamped), one swap at a
    /// time, so every step is the same move <see cref="MoveChild"/> makes. Returns whether it moved.
    /// </summary>
    public static bool MoveChildTo(this ViewState view, string childId, int index)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (view.Find(childId) is not { ParentId: { } parentId } child)
        {
            return false;
        }

        var siblings = view.ChildrenOf(parentId);
        var target = Math.Clamp(index, 0, siblings.Count - 1);
        var at = siblings.IndexOf(child);
        var moved = false;

        while (at != target && view.MoveChild(childId, Math.Sign(target - at)))
        {
            at += Math.Sign(target - at);
            moved = true;
        }

        return moved;
    }

    /// <summary>
    /// [S-875] What dropping a dragged widget at (<paramref name="x"/>, <paramref name="y"/>) — the
    /// pointer, as a fraction of the screen — does to its membership: it joins the topmost container
    /// under the pointer that will take it (not itself, nothing inside it, not a collapsed group whose
    /// area cannot be seen), or, dropped outside the group it was in, it leaves that group. Returns
    /// null for "no change", otherwise the new parent (an empty string for the desktop).
    /// </summary>
    public static string? DropTarget(this ViewState view, string draggedId, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (view.Find(draggedId) is not { } dragged)
        {
            return null;
        }

        var painted = view.PaintOrder();
        for (var i = painted.Count - 1; i >= 0; i--)
        {
            var candidate = painted[i];
            if (!WidgetTree.IsContainer(candidate.Kind)
                || candidate.Config is GroupConfig { Collapsed: true }
                || !Contains(candidate, x, y)
                || !view.CanReparent(draggedId, candidate.Id))
            {
                continue;
            }

            return string.Equals(candidate.Id, dragged.ParentId, StringComparison.Ordinal) ? null : candidate.Id;
        }

        return view.Find(dragged.ParentId) is { Kind: WidgetKind.Group } group && !Contains(group, x, y)
            ? string.Empty
            : null;
    }

    private static bool Contains(WidgetInstance widget, double x, double y)
        => x >= widget.X && x < widget.X + widget.W && y >= widget.Y && y < widget.Y + widget.H;

    private static (int From, int To)? SwapTarget(ViewState view, string childId, int delta)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (delta is not (1 or -1) || view.Find(childId) is not { ParentId: { } parentId } child)
        {
            return null;
        }

        var siblings = view.ChildrenOf(parentId);
        var at = siblings.IndexOf(child);
        var next = at + delta;
        if (at < 0 || next < 0 || next >= siblings.Count)
        {
            return null;
        }

        return (view.Widgets.IndexOf(child), view.Widgets.IndexOf(siblings[next]));
    }

    private static int IndexOf(IReadOnlyList<string> ids, string id)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            if (string.Equals(ids[i], id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
