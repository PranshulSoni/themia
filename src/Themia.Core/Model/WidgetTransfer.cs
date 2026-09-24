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

        // Remove before adding: the same instance must never be reachable from two views, or the
        // next save writes it twice and the load-time duplicate-id repair silently renames one.
        from.Widgets.Remove(widget);
        widget.Rect = rect;
        to.Widgets.Add(widget);
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

        if (plan.RemoveFromViewId is { } fromViewId && plan.RemoveWidgetId is { } removeId)
        {
            state.FindView(fromViewId)?.Widgets.RemoveAll(w => w.Id == removeId);
        }

        var target = state.FindView(targetViewId) ?? state.ActiveView();
        target.Widgets.Add(plan.Widget);
    }
}
