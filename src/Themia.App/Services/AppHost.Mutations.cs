using Microsoft.UI.Dispatching;
using Themia.App.Interop;
using Themia.App.Shell;
using Themia.App.Shell.Config;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Storage;

namespace Themia.App.Services;

/// <summary>
/// Mutations — the single write path — and the commands built on it: edit mode, views, import,
/// and opening the settings window.
/// </summary>
internal sealed partial class AppHost
{
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
            state.EnsureViewPerDisplay(DisplayViewKeys());
        });

        // The imported file carries its own per-display switches ([S-845]).
        ReconcileDisplays();

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
        Mutate(state => state.EnsureViewPerDisplay(DisplayViewKeys()));
    }
}
