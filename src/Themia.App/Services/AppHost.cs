using Microsoft.UI.Dispatching;
using Themia.App.Interop;
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

    /// <summary>[S-458] The screen-time tracker, from <see cref="Start"/> to <see cref="Dispose"/>.</summary>
    private ScreenTimeService? _screenTime;

    private ConfigWindow? _config;
    private readonly Mail.MailService _mail;
    private readonly Developer.GitHubService _gitHub;
    private readonly Developer.ClaudeUsageService _claudeUsage;
    private bool _editMode;
    private bool _disposed;

    public AppHost(DispatcherQueue dispatcher, AppStateStore store, AppState state)
    {
        _dispatcher = dispatcher;
        _store = store;
        State = state;
        Ticker = new Ticker(dispatcher);

        // Beside the state file, so a portable or test state path takes its accounts with it.
        Accounts = new Accounts.AccountService(dispatcher, Themia.Core.Accounts.AccountStore.PathBeside(store.Path));
        AppServices.Accounts = Accounts;

        // [S-546] One mail service for every Mail widget, on the shared ticker.
        _mail = new Mail.MailService(Accounts, Ticker);
        AppServices.Mail = _mail;
        AppServices.ShowAccounts = () => ShowConfig(new ConfigTarget.App(AppPage.Accounts));

        // [S-591]/[S-607] One task service and one drive service for every widget. The task lists
        // kept on this PC live beside the state file, as the accounts do.
        AppServices.Tasks = new Tasks.TaskService(
            new Tasks.AccountTaskRemote(Accounts),
            new Tasks.LocalTaskPool(new Themia.Core.Tasks.LocalTaskStore(Themia.Core.Tasks.LocalTaskStore.PathBeside(store.Path))));
        AppServices.Drive = new Files.DriveService(new Files.AccountDriveRemote(Accounts));

        // [S-624] One GitHub client (one cache, one rate-limit gate per account) and one Claude
        // usage reader for every developer widget, on the shared ticker.
        _gitHub = new Developer.GitHubService(Accounts, Ticker);
        AppServices.GitHub = _gitHub;
        _claudeUsage = new Developer.ClaudeUsageService(Accounts, Ticker);
        AppServices.ClaudeUsage = _claudeUsage;
    }

    public AppState State { get; }

    public Ticker Ticker { get; }

    /// <summary>Themia's own accounts, shared by every widget that reads from one.</summary>
    public Accounts.AccountService Accounts { get; }

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

        // [S-459] Screen time is recorded whether or not a screen-time widget is on the desktop, so
        // the history is there the day one is added. Only the app starts it — never the gallery.
        _screenTime = ScreenTimeService.Start(
            _dispatcher,
            Path.Combine(Path.GetDirectoryName(_store.Path) ?? string.Empty, Themia.Core.Storage.ScreenTimeStore.FileName));
        AppServices.ScreenTime = _screenTime;

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

        // A ghost has nowhere to land outside edit mode: the surface stops taking clicks.
        if (!enabled)
        {
            CancelPlacement();
        }

        EditModeChanged?.Invoke(enabled);

        if (!enabled)
        {
            _store.SaveSoon(State);
        }

        Log.Info(Scope, enabled ? "entered edit mode" : "left edit mode");
    }

    /// <summary>[S-19] Show a view on one display. See <see cref="AppState.ShowViewOn"/>.</summary>
    public void ShowView(string monitorId, string viewId)
        => Mutate(state => state.ShowViewOn(monitorId, viewId));

    /// <summary>Where the state file lives, for the About page to show and open.</summary>
    public string StatePath => _store.Path;

    /// <summary>
    /// [S-497] Replace the whole state with an imported one, keeping what is there now beside the
    /// state file first. Returns null on success, otherwise why nothing was changed.
    /// <para>
    /// The backup is the in-memory state, not a copy of the file: a debounced save may not have
    /// landed yet, and "what you have now" is what is on screen. If the backup cannot be written the
    /// import does not happen — replacing the user's layout with nothing to go back to is exactly the
    /// loss the backup exists to prevent.
    /// </para>
    /// </summary>
    public string? ImportState(AppState incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        var backup = SettingsTransfer.BackupPath(_store.Path, DateTime.Now);
        if (SettingsTransfer.WriteFile(State, backup) is { } problem)
        {
            Log.Error(Scope, $"import cancelled: could not keep the current settings at {backup}");
            return $"The settings you have now couldn't be kept, so nothing was imported. {problem}";
        }

        Select(null);
        Clipboard.Clear();

        Mutate(state =>
        {
            SettingsTransfer.Apply(state, incoming);

            // Displays named in the file that are not connected keep their assignment ([S-18]);
            // the ones that are connected and unnamed get a view of their own, as at startup.
            state.EnsureViewPerDisplay([.. _windows.Select(w => w.Monitor.DeviceName)]);
        });

        Log.Info(Scope, $"imported settings; the previous ones are at {backup}");
        return null;
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

    // -----------------------------------------------------------------------------------------
    // Placing a new widget
    // -----------------------------------------------------------------------------------------

    /// <summary>The kind being placed, while the user is choosing where it goes; otherwise null.</summary>
    public WidgetKind? PlacingKind { get; private set; }

    /// <summary>Raised when placement starts, ends or is cancelled.</summary>
    public event Action? PlacementChanged;

    /// <summary>
    /// [S-421] Start placing a widget. Nothing is added yet: a ghost follows the pointer across every
    /// display and the widget exists only once the user clicks somewhere.
    /// <para>
    /// The settings window is deliberately not opened. Adding a widget and configuring it are two
    /// decisions, and a window appearing over the very spot the user was about to click is the
    /// fastest way to lose the first one.
    /// </para>
    /// </summary>
    public void BeginPlacement(WidgetKind kind)
    {
        SetEditMode(true);
        Select(null);
        PlacingKind = kind;
        PlacementChanged?.Invoke();
        Log.Info(Scope, $"placing a {kind}");
    }

    /// <summary>Abandon placement without adding anything. Safe to call when nothing is being placed.</summary>
    public void CancelPlacement()
    {
        if (PlacingKind is null)
        {
            return;
        }

        PlacingKind = null;
        PlacementChanged?.Invoke();
    }

    /// <summary>
    /// [S-422] Put the widget being placed into the view of the display it was dropped on, at the
    /// rect its ghost showed. Returns the new widget's id, or null when nothing was being placed.
    /// </summary>
    public string? CompletePlacement(DesktopWindow surface, Themia.Core.Geometry.FracRect rect)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (PlacingKind is not { } kind)
        {
            return null;
        }

        var widget = WidgetInstance.Create(kind, rect);
        var viewId = surface.CurrentViewId;

        // Placement ends before the mutation lands, so the relayout it triggers already sees the
        // surface back in its ordinary edit state rather than drawing a ghost over the new card.
        PlacingKind = null;
        PlacementChanged?.Invoke();

        Mutate(state => (state.FindView(viewId) ?? state.ActiveView()).Widgets.Add(widget));
        Select(widget.Id);
        Log.Info(Scope, $"placed a {kind} on {surface.Monitor.DeviceName}");
        return widget.Id;
    }

    // -----------------------------------------------------------------------------------------
    // Dragging between displays
    // -----------------------------------------------------------------------------------------

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

                // [S-603] A task ticked on a list kept on this PC a moment before exit.
                if (AppServices.Tasks is { } tasks)
                {
                    await tasks.Local.FlushAsync().ConfigureAwait(true);
                }
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
        AppServices.Mail = null;
        AppServices.ShowAccounts = null;
        _mail.Dispose();
        AppServices.Tasks?.Dispose();
        AppServices.Tasks = null;
        AppServices.Drive?.Dispose();
        AppServices.Drive = null;
        AppServices.GitHub = null;
        _gitHub.Dispose();
        AppServices.ClaudeUsage = null;
        _claudeUsage.Dispose();
        AppServices.Accounts = null;
        Accounts.Dispose();

        // [S-459] Accrues up to now and writes the history synchronously: the process is ending.
        AppServices.ScreenTime = null;
        _screenTime?.Dispose();
        _screenTime = null;

        CloseSurfaces();

        _windows.Clear();

        _config?.Close();
        Ticker.Dispose();
        _command.Dispose();
    }
}
