using Microsoft.UI.Dispatching;
using Themia.App.Interop;
using Themia.App.Services.Hearth;
using Themia.App.Shell;
using Themia.App.Shell.Config;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Storage;

namespace Themia.App.Services;

/// <summary>
/// Owns everything with a lifetime: the persisted state, the widget windows, the shared ticker and
/// the hidden command window. There is exactly one, created on the UI thread, and it is the only writer of
/// <see cref="State"/> — widgets and windows mutate through the methods here so every change goes
/// through one save path.
/// </summary>
internal sealed class AppHost : IDisposable
{
    private const string Scope = "host";

    private readonly DispatcherQueue _dispatcher;
    private readonly AppStateStore _store;
    private readonly List<DesktopWindow> _windows = [];
    private readonly CommandWindow _command = new();

    private ConfigWindow? _config;
    private bool _editMode;
    private bool _disposed;

    public AppHost(DispatcherQueue dispatcher, AppStateStore store, AppState state)
    {
        _dispatcher = dispatcher;
        _store = store;
        State = state;
        Ticker = new Ticker(dispatcher);
        Hub = new HearthHub(dispatcher);
        AppServices.Hub = Hub;
        AppServices.Data = new HearthDataService(Hub);
    }

    public AppState State { get; }

    public Ticker Ticker { get; }

    /// <summary>The app's one connection to the hearth hub, shared by every widget that reads from it.</summary>
    public HearthHub Hub { get; }

    /// <summary>Raised when anything that affects rendering changed. Windows re-read <see cref="State"/>.</summary>
    public event Action? StateChanged;

    /// <summary>Raised when edit mode is entered or left.</summary>
    public event Action<bool>? EditModeChanged;

    public bool EditMode => _editMode;

    /// <summary>The live widget surfaces, one per display. Used by Settings to offer a per-display view.</summary>
    public IReadOnlyList<DesktopWindow> Surfaces => _windows;

    /// <summary>Copy / cut / paste buffer, shared by every surface so widgets move between displays.</summary>
    public WidgetClipboard Clipboard { get; } = new();

    /// <summary>The widget the keyboard commands act on, or null.</summary>
    public string? SelectedWidgetId { get; private set; }

    public event Action? SelectionChanged;

    public void Select(string? widgetId)
    {
        if (SelectedWidgetId == widgetId)
        {
            return;
        }

        SelectedWidgetId = widgetId;
        SelectionChanged?.Invoke();
    }

    /// <summary>Locate a widget by id across every view. Returns null when it no longer exists.</summary>
    public (ViewState View, WidgetInstance Widget)? Find(string? widgetId)
    {
        if (string.IsNullOrEmpty(widgetId))
        {
            return null;
        }

        foreach (var view in State.Views)
        {
            foreach (var widget in view.Widgets)
            {
                if (widget.Id == widgetId)
                {
                    return (view, widget);
                }
            }
        }

        return null;
    }

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

    // -----------------------------------------------------------------------------------------
    // Clipboard commands
    // -----------------------------------------------------------------------------------------

    public void CopySelected() => CaptureSelected(cut: false);

    public void CutSelected() => CaptureSelected(cut: true);

    private void CaptureSelected(bool cut)
    {
        if (Find(SelectedWidgetId) is not var (view, widget))
        {
            return;
        }

        if (cut)
        {
            Clipboard.Cut(widget, view.Id);
        }
        else
        {
            Clipboard.Copy(widget, view.Id);
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

    // -----------------------------------------------------------------------------------------
    // Lifetime
    // -----------------------------------------------------------------------------------------

    public void Start()
    {
        _command.EditModeToggleRequested += () => SetEditMode(!_editMode);

        if (!_command.Create())
        {
            // Without it the hotkey and the second-launch route are both gone, and edit mode is
            // only reachable by double-clicking a widget the user may not have.
            Log.Error(Scope, "no command window; edit mode can only be reached by double-clicking a widget");
        }

        // Show Desktop raises the desktop window above everything, including anything pinned below
        // it, and hides the rest by cloaking them. Without the controller the widgets vanish at the
        // one moment the user asked to see their desktop.
        DesktopZOrder.Start(_dispatcher);

        RebuildWindows();

        // Seed the file on a fresh profile so the starting layout survives a restart even if the
        // user never touches anything.
        _store.SaveSoon(State);
    }

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

    // -----------------------------------------------------------------------------------------
    // Mutations — the single write path
    // -----------------------------------------------------------------------------------------

    /// <summary>Apply a change and persist it. Every mutation goes through here.</summary>
    public void Mutate(Action<AppState> change, bool relayout = true)
    {
        ArgumentNullException.ThrowIfNull(change);

        try
        {
            change(State);
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "a state mutation threw; state may be partially applied", ex);
        }

        _store.SaveSoon(State);

        if (relayout)
        {
            StateChanged?.Invoke();
        }
    }

    /// <summary>
    /// Persist geometry mid-drag without re-running layout: the dragged card is already following
    /// the pointer, so raising <see cref="StateChanged"/> here would fight it.
    /// </summary>
    public void SaveGeometryOnly() => _store.SaveSoon(State);

    public void SetEditMode(bool enabled)
    {
        if (_editMode == enabled)
        {
            return;
        }

        _editMode = enabled;
        EditModeChanged?.Invoke(enabled);

        if (!enabled)
        {
            _store.SaveSoon(State);
        }

        Log.Info(Scope, enabled ? "entered edit mode" : "left edit mode");
    }

    public void SwitchView(string viewId)
    {
        Mutate(state =>
        {
            if (state.FindView(viewId) is not null)
            {
                state.ActiveViewId = viewId;
            }
        });
    }

    /// <summary>
    /// Open the settings window on a particular page, reusing the one window if it is already up.
    /// A second settings window would let the same widget be edited in two places at once.
    /// </summary>
    public void ShowConfig(ConfigTarget target)
    {
        if (_config is null)
        {
            _config = new ConfigWindow(this);
            _config.Closed += () => _config = null;
            _config.Activate();
        }

        _config.Navigate(target);
    }

    public void ShowSettings() => ShowConfig(new ConfigTarget.App());

    /// <summary>Delete a view and everything in it, repairing anything that pointed at it.</summary>
    public void DeleteView(string viewId)
    {
        if (State.Views.Count <= 1)
        {
            return; // the last view cannot go: a desktop with no view has nowhere to put a widget
        }

        Mutate(state =>
        {
            state.Views.RemoveAll(v => v.Id == viewId);

            foreach (var monitor in state.MonitorViews.Where(kv => kv.Value == viewId).Select(kv => kv.Key).ToList())
            {
                state.MonitorViews.Remove(monitor);
            }

            if (state.ActiveViewId == viewId)
            {
                state.ActiveViewId = state.Views[0].Id;
            }
        });

        // A display left without an assignment gets a fresh one rather than silently falling back
        // to whatever view happens to be active.
        Mutate(state => state.EnsureViewPerDisplay([.. _windows.Select(w => w.Monitor.DeviceName)]));
    }

    /// <summary>
    /// Drop a new widget, centred. It lands in <paramref name="intoViewId"/> when given, otherwise on
    /// whichever display the pointer is over. Returns the new widget's id.
    /// </summary>
    public string? AddWidget(WidgetKind kind, string? intoViewId = null)
    {
        var surface = _windows.FirstOrDefault(w => w.IsUnderCursor) ?? _windows.FirstOrDefault();
        if (surface is null)
        {
            Log.Warn(Scope, "no widget surface to add to");
            return null;
        }

        // Prefer the surface already showing the target view, so the new widget appears where the
        // user is looking rather than on a display that does not render it.
        if (intoViewId is not null)
        {
            surface = _windows.FirstOrDefault(w => w.CurrentViewId == intoViewId) ?? surface;
        }

        var id = surface.AddWidgetCentred(kind, intoViewId);
        SetEditMode(true);
        Select(id);
        return id;
    }

    // -----------------------------------------------------------------------------------------
    // Lifetime, continued
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Close everything down and quit. Reachable from the edit toolbar, which is the only place it
    /// is offered: an app with no tray entry has no other chrome to hang it from.
    /// </summary>
    public void RequestExit()
    {
        _ = _dispatcher.TryEnqueue(async () =>
        {
            // Close the surfaces first, then flush. A widget that edits its own config — a note —
            // writes its last, unflushed keystrokes into the config on detach, so flushing before
            // tearing down persisted everything except the sentence the user had just typed.
            CloseSurfaces();

            try
            {
                await _store.FlushAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log.Error(Scope, "final save failed", ex);
            }

            Dispose();
            Microsoft.UI.Xaml.Application.Current.Exit();
        });
    }

    /// <summary>
    /// Detach every widget, so anything a widget was holding is written into the state before the
    /// state is written to disk. Safe to call twice: <see cref="Dispose"/> calls it again.
    /// </summary>
    private void CloseSurfaces()
    {
        foreach (var window in _windows)
        {
            window.CloseSurface();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DesktopZOrder.Stop();
        Clipboard.Clear();
        AppServices.Data = null;
        AppServices.Hub = null;
        _ = Hub.DisposeAsync();

        CloseSurfaces();

        _windows.Clear();

        _config?.Close();
        Ticker.Dispose();
        _command.Dispose();
    }
}
