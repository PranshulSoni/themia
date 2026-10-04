namespace Themia.Core.Model;

/// <summary>
/// [S-874] Which widget paints over which, and the two commands that change it.
/// <para>
/// The order is the view's widget list: later paints over earlier. It is persisted because the list
/// is, so "bring to front" survives a restart without a second index that could disagree with it.
/// A widget moves together with everything inside it, so a container never ends up separated from
/// its members by an unrelated widget.
/// </para>
/// </summary>
public static class WidgetZOrder
{
    /// <summary>Whether <see cref="BringToFront"/> would change anything.</summary>
    public static bool CanBringToFront(this ViewState view, string id)
        => Block(view, id) is { Count: > 0 } block && !IsRun(view, block, atEnd: true);

    /// <summary>Whether <see cref="SendToBack"/> would change anything.</summary>
    public static bool CanSendToBack(this ViewState view, string id)
        => Block(view, id) is { Count: > 0 } block && !IsRun(view, block, atEnd: false);

    /// <summary>Paint the widget (and what it holds) over every other widget. False when nothing changed.</summary>
    public static bool BringToFront(this ViewState view, string id)
    {
        if (!view.CanBringToFront(id))
        {
            return false;
        }

        var block = Block(view, id);
        view.Widgets.RemoveAll(block.Contains);
        view.Widgets.AddRange(block);
        return true;
    }

    /// <summary>Paint the widget (and what it holds) under every other widget. False when nothing changed.</summary>
    public static bool SendToBack(this ViewState view, string id)
    {
        if (!view.CanSendToBack(id))
        {
            return false;
        }

        var block = Block(view, id);
        view.Widgets.RemoveAll(block.Contains);
        view.Widgets.InsertRange(0, block);
        return true;
    }

    /// <summary>
    /// [S-874] The paint order of the widgets the grid places (bottom first). View order, except that
    /// a group paints directly under the lowest of its members ([S-415]): an area drawn over the
    /// widgets standing in it would hide them. Nested groups paint outermost first. Widgets a stack
    /// or tab strip draws are not in it — their container is.
    /// </summary>
    public static List<WidgetInstance> PaintOrder(this ViewState view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var index = new Dictionary<WidgetInstance, int>(view.Widgets.Count);
        for (var i = 0; i < view.Widgets.Count; i++)
        {
            index[view.Widgets[i]] = i;
        }

        var placed = view.Widgets.FindAll(w => !DrawnByContainer(view, w));

        int Key(WidgetInstance widget)
        {
            var key = index[widget];
            if (WidgetTree.ContainerOf(widget.Kind) == ContainerKind.Group)
            {
                foreach (var member in Descendants(view, widget.Id))
                {
                    key = Math.Min(key, index[member]);
                }
            }

            return key;
        }

        return [.. placed
            .OrderBy(Key)
            .ThenBy(w => WidgetTree.ContainerOf(w.Kind) == ContainerKind.Group ? view.DepthOf(w.Id) : int.MaxValue)
            .ThenBy(w => index[w])];
    }

    private static bool DrawnByContainer(ViewState view, WidgetInstance widget)
        => view.Find(widget.ParentId) is { } parent && WidgetTree.OwnsChildGeometry(WidgetTree.ContainerOf(parent.Kind));

    /// <summary>The widget and everything below it, in view order; empty when the id is unknown.</summary>
    private static List<WidgetInstance> Block(ViewState view, string id)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (view.Find(id) is null)
        {
            return [];
        }

        return view.Widgets.FindAll(w => view.IsSelfOrDescendant(id, w.Id));
    }

    private static IEnumerable<WidgetInstance> Descendants(ViewState view, string id)
        => view.Widgets.Where(w => !string.Equals(w.Id, id, StringComparison.Ordinal) && view.IsSelfOrDescendant(id, w.Id));

    /// <summary>Whether the block already sits, in order, at the end (or the start) of the list.</summary>
    private static bool IsRun(ViewState view, List<WidgetInstance> block, bool atEnd)
    {
        var offset = atEnd ? view.Widgets.Count - block.Count : 0;
        for (var i = 0; i < block.Count; i++)
        {
            if (!ReferenceEquals(view.Widgets[offset + i], block[i]))
            {
                return false;
            }
        }

        return true;
    }
}
