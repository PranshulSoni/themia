using Themia.Core.Files;
using Windows.Foundation;

namespace Themia.App.Widgets;

/// <summary>
/// [S-1400]/[S-1020] Implemented by a view that takes things dropped onto it from Explorer, a browser or the
/// Start menu (the folder and Shortcuts widgets).
/// <para>
/// There is <b>one</b> drop path in the app: the desktop surface registers the only OLE drop target
/// (<c>DragDropInterop.RegisterDropTarget</c> on its windows, [S-1021]), hit-tests the cursor to the
/// innermost card, and forwards here with the point in this view's own DIPs (<c>DesktopWindow.Drop.cs</c>).
/// A view never registers a target of its own and never uses XAML's <c>AllowDrop</c>. Never called
/// in edit mode; a view that does not implement this refuses drops ([S-1020]).
/// </para>
/// <para>
/// The member names are UIElement's event names, so a view implements them <b>explicitly</b>
/// (<c>DropEffect IWidgetDropTarget.DragOver(...)</c>) rather than hiding the events.
/// </para>
/// <para>
/// UI thread. Never throws into the caller (OLE is on the stack): a view logs and answers None.
/// </para>
/// </summary>
internal interface IWidgetDropTarget
{
    /// <summary>
    /// The drag entered or moved over this view. Show where it would land, name the place for the
    /// caption (<see cref="WidgetDrag.Place"/>), and return what it would do (None: refused here; the
    /// surface does not pass it on to the desktop behind the widget).
    /// </summary>
    DropEffect DragOver(WidgetDrag drag, Point local);

    /// <summary>The drag left this view, or was cancelled: take any highlight down.</summary>
    void DragLeave();

    /// <summary>The drop. Returns what was done (None when nothing was taken).</summary>
    DropEffect Drop(WidgetDrag drag, Point local);
}
