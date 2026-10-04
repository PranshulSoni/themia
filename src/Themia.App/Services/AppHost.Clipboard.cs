using Microsoft.UI.Dispatching;
using Themia.App.Interop;
using Themia.App.Shell;
using Themia.App.Shell.Config;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Storage;

namespace Themia.App.Services;

/// <summary>Clipboard commands: copy, cut, paste and delete of the selected widget.</summary>
internal sealed partial class AppHost
{
    public void CopySelected() => CaptureSelected(cut: false);

    public void CutSelected() => CaptureSelected(cut: true);

    private void CaptureSelected(bool cut)
    {
        if (Find(SelectedWidgetId) is not var (view, widget))
        {
            return;
        }

        // [S-876] A container is copied with everything inside it.
        var members = view.Widgets.Where(w => !ReferenceEquals(w, widget) && view.IsSelfOrDescendant(widget.Id, w.Id)).ToList();

        if (cut)
        {
            Clipboard.Cut(widget, view.Id, members);
        }
        else
        {
            Clipboard.Copy(widget, view.Id, members);
        }
    }

    public void DeleteSelected()
    {
        if (Find(SelectedWidgetId) is not var (view, widget))
        {
            return;
        }

        var id = widget.Id;

        Mutate(_ =>
        {
            // A container gives up its children rather than taking them with it. See
            // WidgetTree.Dissolve.
            view.Dissolve(id);
            view.Widgets.RemoveAll(w => w.Id == id);
        });
        Select(null);
    }

    /// <summary>Paste into the surface under the cursor, at the cursor.</summary>
    public void PasteAtCursor()
    {
        var target = SurfaceUnderCursor() ?? _windows.FirstOrDefault();
        target?.PasteAtCursor();
    }

    /// <summary>
    /// Apply a paste plan: add the new widget to <paramref name="targetViewId"/> and, for a cut,
    /// remove the original from wherever it came from. Both halves happen in one mutation so a cut
    /// across views can never duplicate or lose the widget.
    /// </summary>
    public void ApplyPaste(PastePlan plan, string targetViewId)
    {
        Mutate(state => state.ApplyPaste(plan, targetViewId));
        Clipboard.OnPasted();
        Select(plan.Widget.Id);
    }

    /// <summary>
    /// Move a widget from one view to another, keeping its identity. Used when a drag ends over a
    /// display showing a different view.
    /// </summary>
    public void MoveWidgetToView(string widgetId, string fromViewId, string toViewId, Themia.Core.Geometry.FracRect rect)
        => Mutate(state => state.MoveWidget(widgetId, fromViewId, toViewId, rect));
}
