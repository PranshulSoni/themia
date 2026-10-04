using Microsoft.UI.Dispatching;
using Themia.App.Interop;
using Themia.App.Shell;
using Themia.App.Shell.Config;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Storage;

namespace Themia.App.Services;

/// <summary>
/// The displays: one widget surface per monitor, finding the surface at a point, and dragging a
/// widget from one display to another.
/// </summary>
internal sealed partial class AppHost
{
    /// <summary>The surface whose display contains a point, in physical screen pixels.</summary>
    public DesktopWindow? SurfaceAt(int x, int y)
    {
        foreach (var window in _windows)
        {
            var area = window.Monitor.WorkArea;
            if (x >= area.Left && x < area.Right && y >= area.Top && y < area.Bottom)
            {
                return window;
            }
        }

        return null;
    }

    public DesktopWindow? SurfaceUnderCursor()
        => Win32.GetCursorPos(out var cursor) ? SurfaceAt(cursor.X, cursor.Y) : null;

    /// <summary>Create one widget window per display, dropping any whose monitor disappeared.</summary>
    public void RebuildWindows()
    {
        var monitors = Monitors.All();
        if (monitors.Count == 0)
        {
            Log.Warn(Scope, "no displays reported; keeping the existing windows");
            return;
        }

        // Give each display a view of its own before any surface is built. Without this every
        // display falls back to the active view, so the same widgets are drawn on all of them and
        // dragging one to another screen is a no-op by definition - it is already there.
        var created = State.EnsureViewPerDisplay([.. monitors.Select(m => m.DeviceName)]);
        if (created > 0)
        {
            Log.Info(Scope, $"created {created} view(s) so every display has one of its own");
            _store.SaveSoon(State);
        }

        CloseSurfaces();

        _windows.Clear();

        foreach (var monitor in monitors)
        {
            try
            {
                var window = new DesktopWindow(this, monitor);
                _windows.Add(window);
                window.ShowSurface();
            }
            catch (Exception ex)
            {
                // One bad display must not cost the user every other monitor's widgets.
                Log.Error(Scope, $"could not create the widget surface for {monitor.DeviceName}", ex);
            }
        }

        Log.Info(Scope, $"widget surface up on {_windows.Count} display(s)");
    }

    /// <summary>
    /// [S-78] While a widget is dragged, show where it would land if released now.
    /// <para>
    /// The dragged card cannot follow the pointer onto another display — its rect is clamped to its
    /// own screen ([S-09]) — so without this the widget sticks to the edge of the first screen while
    /// the pointer is already on the second, and the drop is a surprise. The ghost appears on the
    /// display that would receive it, and only when it really would ([S-76]).
    /// </para>
    /// </summary>
    /// <returns>True when the drop would hand the widget to another display.</returns>
    public bool PreviewHandOff(DesktopWindow source, WidgetInstance widget, Themia.Core.Geometry.FracRect size)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(widget);

        DesktopWindow? target = null;
        var rect = default(Themia.Core.Geometry.FracRect);

        if (Win32.GetCursorPos(out var cursor)
            && SurfaceAt(cursor.X, cursor.Y) is { } under
            && !ReferenceEquals(under, source)
            && under.CurrentViewId != source.CurrentViewId)
        {
            target = under;
            rect = under.RectForDrop(cursor, size);
        }

        foreach (var window in _windows)
        {
            if (ReferenceEquals(window, target))
            {
                window.ShowGhost(rect, widget.Kind);
            }
            else
            {
                window.HideGhost();
            }
        }

        return target is not null;
    }

    /// <summary>Take down every drag ghost. Called when a drag ends, however it ends.</summary>
    public void EndHandOffPreview()
    {
        foreach (var window in _windows)
        {
            window.HideGhost();
        }
    }
}
