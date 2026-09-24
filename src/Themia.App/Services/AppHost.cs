using Microsoft.UI.Dispatching;
using Themia.App.Interop;
using Themia.App.Shell;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Storage;

namespace Themia.App.Services;

/// <summary>
/// Owns everything with a lifetime: the persisted state, the widget windows, the shared ticker and
/// the tray icon. There is exactly one, created on the UI thread, and it is the only writer of
/// <see cref="State"/> — widgets and windows mutate through the methods here so every change goes
/// through one save path.
/// </summary>
internal sealed class AppHost : IDisposable
{
    private const string Scope = "host";

    private const uint CmdEditLayout = 1;
    private const uint CmdSettings = 2;
    private const uint CmdAddWidget = 3;
    private const uint CmdExit = 4;
    private const uint CmdViewBase = 100;

    private readonly DispatcherQueue _dispatcher;
    private readonly AppStateStore _store;
    private readonly List<DesktopWindow> _windows = [];
    private readonly TrayIcon _tray = new();

    private SettingsWindow? _settings;
    private bool _editMode;
    private bool _disposed;

    public AppHost(DispatcherQueue dispatcher, AppStateStore store, AppState state)
    {
        _dispatcher = dispatcher;
        _store = store;
        State = state;
        Ticker = new Ticker(dispatcher);
    }

    public AppState State { get; }

    public Ticker Ticker { get; }

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
        Mutate(_ => view.Widgets.RemoveAll(w => w.Id == id));
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
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "themia.ico");
        _tray.MenuProvider = BuildTrayMenu;
        _tray.CommandInvoked += OnTrayCommand;
        _tray.Activated += () => SetEditMode(!_editMode);

        if (!_tray.Create("Themia", iconPath))
        {
            // The tray icon is the only way in and out of edit mode, so losing it is not cosmetic.
            Log.Error(Scope, "tray icon unavailable; edit mode can still be reached by double-clicking a widget");
        }

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

        foreach (var window in _windows)
        {
            window.CloseSurface();
        }

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

    public void ShowSettings()
    {
        if (_settings is not null)
        {
            _settings.BringToFront();
            return;
        }

        _settings = new SettingsWindow(this);
        _settings.Closed += () => _settings = null;
        _settings.Activate();
    }

    /// <summary>Drop a new widget onto the monitor the pointer is on, centred.</summary>
    public void AddWidget(WidgetKind kind)
    {
        var target = _windows.FirstOrDefault(w => w.IsUnderCursor) ?? _windows.FirstOrDefault();
        if (target is null)
        {
            Log.Warn(Scope, "no widget surface to add to");
            return;
        }

        target.AddWidgetCentred(kind);
        SetEditMode(true);
    }

    // -----------------------------------------------------------------------------------------
    // Tray
    // -----------------------------------------------------------------------------------------

    private IReadOnlyList<TrayMenuItem> BuildTrayMenu()
    {
        var items = new List<TrayMenuItem>
        {
            new(CmdEditLayout, _editMode ? "Done editing" : "Edit layout", Checked: _editMode),
            new(CmdAddWidget, "Add widget…"),
            TrayMenuItem.Separator,
        };

        if (State.Views.Count > 1)
        {
            var activeId = State.ActiveView().Id;
            for (var i = 0; i < State.Views.Count; i++)
            {
                var view = State.Views[i];
                items.Add(new TrayMenuItem(CmdViewBase + (uint)i, view.Name, Checked: view.Id == activeId));
            }

            items.Add(TrayMenuItem.Separator);
        }

        items.Add(new TrayMenuItem(CmdSettings, "Settings…"));
        items.Add(TrayMenuItem.Separator);
        items.Add(new TrayMenuItem(CmdExit, "Exit Themia"));

        return items;
    }

    private void OnTrayCommand(uint id)
    {
        switch (id)
        {
            case CmdEditLayout:
                SetEditMode(!_editMode);
                break;

            case CmdAddWidget:
                AddWidget(WidgetKind.Clock);
                break;

            case CmdSettings:
                ShowSettings();
                break;

            case CmdExit:
                RequestExit();
                break;

            default:
                if (id >= CmdViewBase)
                {
                    var index = (int)(id - CmdViewBase);
                    if (index >= 0 && index < State.Views.Count)
                    {
                        SwitchView(State.Views[index].Id);
                    }
                }

                break;
        }
    }

    private void RequestExit()
    {
        // Flush before tearing anything down: the user asked to quit, so a debounced layout change
        // from a second ago must still reach disk.
        _ = _dispatcher.TryEnqueue(async () =>
        {
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Clipboard.Clear();

        foreach (var window in _windows)
        {
            window.CloseSurface();
        }

        _windows.Clear();

        _settings?.Close();
        Ticker.Dispose();
        _tray.Dispose();
    }
}
