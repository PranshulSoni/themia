using Themia.Core.Geometry;
using Themia.Core.Localization;

namespace Themia.Core.Model;

/// <summary>
/// Moving widgets between views — by pasting, or by dragging onto another display.
/// <para>
/// These live here, on the state, rather than in the window that triggers them: they are the only
/// operations that touch <b>two</b> views at once, so getting them wrong duplicates or loses a
/// widget. Keeping them next to the model means they can be tested without a display attached.
/// </para>
/// </summary>
public static class WidgetTransfer
{
    /// <summary>
    /// Whether a drag that ended over another display should move the widget there.
    /// <para>
    /// Only when the destination shows a <i>different</i> view: when both displays show the same
    /// view the widget already appears on both, so "moving" it would be a no-op that nonetheless
    /// rewrote its position against the wrong screen.
    /// </para>
    /// </summary>
    public static bool ShouldHandOff(bool droppedOnThisDisplay, string sourceViewId, string? targetViewId)
        => !droppedOnThisDisplay
            && targetViewId is not null
            && !string.Equals(targetViewId, sourceViewId, StringComparison.Ordinal);

    /// <summary>
    /// [S-75]/[S-1586] Land a placement: for a move, take the originals out of whatever view they are
    /// in (each with everything it holds), then add the planned widgets to the target view — all in
    /// one go, so a move across views can neither duplicate a widget nor drop it, and a move back
    /// into its own view leaves exactly one of each.
    /// <para>
    /// A moved widget that still exists lands as <b>itself</b>: the live instance, with whatever its
    /// config became since the snapshot, takes the planned rect and parent. Only an original deleted
    /// meanwhile is put back from the snapshot. Anything the removal took that the plan does not carry
    /// (a member added to a moved container after the snapshot) goes along unchanged, so a move never
    /// loses a widget.
    /// </para>
    /// </summary>
    public static void ApplyPlacement(this AppState state, PastePlan plan, string targetViewId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(plan);

        var removed = new List<WidgetInstance>();
        foreach (var rootId in plan.RemoveIds)
        {
            foreach (var view in state.Views)
            {
                if (view.Find(rootId) is null)
                {
                    continue;
                }

                var block = view.Widgets.FindAll(w => view.IsSelfOrDescendant(rootId, w.Id));
                view.Widgets.RemoveAll(block.Contains);
                removed.AddRange(block);
                break;
            }
        }

        var target = state.FindView(targetViewId) ?? state.ActiveView();

        foreach (var planned in plan.Added)
        {
            var original = removed.Find(w => string.Equals(w.Id, planned.Id, StringComparison.Ordinal));
            if (original is null)
            {
                target.Widgets.Add(planned);
                continue;
            }

            removed.Remove(original);
            original.Rect = planned.Rect;
            original.ParentId = planned.ParentId;
            target.Widgets.Add(original);
        }

        target.Widgets.AddRange(removed);
    }

    /// <summary>
    /// The ids a move would take out of the state: each root that exists, with everything it holds,
    /// in the first view that has it.
    /// </summary>
    public static HashSet<string> RemovalBlock(AppState state, IEnumerable<string> rootIds)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rootIds);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rootId in rootIds)
        {
            if (state.Views.Find(v => v.Find(rootId) is not null) is { } view)
            {
                foreach (var w in view.Widgets.Where(w => view.IsSelfOrDescendant(rootId, w.Id)))
                {
                    ids.Add(w.Id);
                }
            }
        }

        return ids;
    }

    /// <summary>
    /// Give each display a view of its own, once.
    /// <para>
    /// Without this every display falls back to the <i>active</i> view, so the same widgets are
    /// drawn on all of them — and dragging one to another screen is then a no-op by definition
    /// ([S-76]), because it is already there. Per-display views are also what makes "show a
    /// different layout on the second monitor" possible at all.
    /// </para>
    /// <para>
    /// Idempotent and stable: a display keeps whatever it was assigned, existing views are handed
    /// out before new ones are made, and a view already claimed by another display is never taken.
    /// Returns the number of views it had to create.
    /// </para>
    /// <para>
    /// [S-847] When the user chose a view for new displays (<see cref="AppState.DefaultViewForNewDisplays"/>),
    /// a display seen for the first time shows that view instead — shared, not claimed, because
    /// choosing it is asking for the same widgets on every new screen.
    /// </para>
    /// <para>
    /// [S-1065] <paramref name="maxViews"/> is the licence's view cap: once the profile holds that
    /// many views, a display that would need a new one shows the active view instead (shared, not
    /// claimed), so plugging in a monitor is never a way past the free limit.
    /// </para>
    /// </summary>
    public static int EnsureViewPerDisplay(this AppState state, IReadOnlyList<string> displayIds, int maxViews = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(displayIds);

        if (displayIds.Count == 0)
        {
            return 0;
        }

        // Only assignments naming a view that still exists count as taken; a dangling one is free
        // to be replaced.
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (display, viewId) in state.MonitorViews)
        {
            if (displayIds.Contains(display) && state.FindView(viewId) is not null)
            {
                taken.Add(viewId);
            }
        }

        var created = 0;

        foreach (var display in displayIds)
        {
            if (state.MonitorViews.TryGetValue(display, out var existing) && state.FindView(existing) is not null)
            {
                continue;
            }

            if (state.FindView(state.DefaultViewForNewDisplays) is { } chosen)
            {
                state.MonitorViews[display] = chosen.Id;
                continue;
            }

            var free = state.Views.FirstOrDefault(v => !taken.Contains(v.Id));

            if (free is null && state.Views.Count >= maxViews)
            {
                state.MonitorViews[display] = state.ActiveView().Id;
                continue;
            }

            if (free is null)
            {
                free = ViewState.Create(Str.App_MonitorLabel.Format(("n", state.Views.Count + 1)));
                state.Views.Add(free);
                created++;
            }

            state.MonitorViews[display] = free.Id;
            taken.Add(free.Id);
        }

        return created;
    }
}
