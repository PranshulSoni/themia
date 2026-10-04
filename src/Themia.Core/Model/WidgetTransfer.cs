using Themia.Core.Geometry;

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
    /// <summary>A unit screen to clamp moved members against: the stored model is fractional.</summary>
    private static readonly ScreenMetrics Unit = new(1920, 1080);

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
    /// Move a widget from one view to another, keeping its identity, and place it at
    /// <paramref name="rect"/>. Returns false if either view or the widget is gone.
    /// </summary>
    public static bool MoveWidget(this AppState state, string widgetId, string fromViewId, string toViewId, FracRect rect)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (string.Equals(fromViewId, toViewId, StringComparison.Ordinal))
        {
            return false;
        }

        var from = state.FindView(fromViewId);
        var to = state.FindView(toViewId);
        if (from is null || to is null)
        {
            return false;
        }

        var widget = from.Widgets.FirstOrDefault(w => w.Id == widgetId);
        if (widget is null)
        {
            return false;
        }

        // [S-876] A container moves with everything inside it, its members shifted as far as it
        // moved; the widget itself leaves whatever held it, which stays behind on the old view.
        var members = from.Widgets.FindAll(w => !ReferenceEquals(w, widget) && from.IsSelfOrDescendant(widgetId, w.Id));
        var dx = rect.X - widget.X;
        var dy = rect.Y - widget.Y;

        // Remove before adding: the same instance must never be reachable from two views, or the
        // next save writes it twice and the load-time duplicate-id repair silently renames one.
        from.Widgets.Remove(widget);
        from.Widgets.RemoveAll(members.Contains);
        widget.Rect = rect;
        widget.ParentId = null;
        to.Widgets.Add(widget);

        foreach (var member in members)
        {
            member.Rect = GridGeometry.Clamp(member.Rect with { X = member.X + dx, Y = member.Y + dy }, Unit);
            to.Widgets.Add(member);
        }

        return true;
    }

    /// <summary>
    /// Apply a paste: add the new widget to the target view and, for a cut, remove the original from
    /// wherever it came from. Both halves happen together, so a cut across views can neither
    /// duplicate the widget nor drop it.
    /// </summary>
    public static void ApplyPaste(this AppState state, PastePlan plan, string targetViewId)
    {
        ArgumentNullException.ThrowIfNull(state);

        // [S-876] A cut container goes with everything inside it.
        if (plan.RemoveFromViewId is { } fromViewId && plan.RemoveWidgetId is { } removeId
            && state.FindView(fromViewId) is { } from)
        {
            var block = from.Widgets.FindAll(w => from.IsSelfOrDescendant(removeId, w.Id));
            from.Widgets.RemoveAll(block.Contains);
        }

        var target = state.FindView(targetViewId) ?? state.ActiveView();
        target.Widgets.Add(plan.Widget);
        target.Widgets.AddRange(plan.Members ?? []);
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
                free = ViewState.Create($"Display {state.Views.Count + 1}");
                state.Views.Add(free);
                created++;
            }

            state.MonitorViews[display] = free.Id;
            taken.Add(free.Id);
        }

        return created;
    }
}
