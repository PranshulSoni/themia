using Themia.App.Services;
using Themia.App.Widgets;
using Themia.Core.Widgets;

namespace Themia.App.Shell;

/// <summary>
/// One display's surface, as a widget may see it: the views, the one shown here, and a way to show
/// another. [S-487]
/// <para>
/// Read live on every call rather than captured, because the views can be renamed, added and
/// deleted from the settings window while the widget is on screen, and the monitor behind a surface
/// can be re-resolved after a display change.
/// </para>
/// </summary>
internal sealed class SurfaceDisplay(AppHost host, DesktopWindow surface) : IWidgetDisplay
{
    public IReadOnlyList<ViewSummary> Views
        => [.. host.State.Views.Select(v => new ViewSummary(v.Id, v.Name))];

    public string ShownViewId => surface.CurrentViewId;

    /// <summary>
    /// [S-19] This display only. Through the host, so it is the same mutation the toolbar's View
    /// menu makes and the same save.
    /// </summary>
    public void ShowView(string viewId) => host.ShowView(surface.Monitor.DeviceName, viewId);
}
