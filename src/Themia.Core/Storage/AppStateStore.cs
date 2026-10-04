using System.Text.Json;
using Themia.Core.Diagnostics;
using Themia.Core.Geometry;
using Themia.Core.Model;

namespace Themia.Core.Storage;

/// <summary>How a load ended. "Absent" and "corrupt" are deliberately different outcomes.</summary>
public enum LoadOutcome
{
    /// <summary>No file yet — a fresh profile. Seeded defaults, nothing was lost.</summary>
    Fresh,

    /// <summary>Read and validated.</summary>
    Loaded,

    /// <summary>
    /// A file existed but could not be trusted. Defaults are returned and the original is moved
    /// aside (never deleted) so the user can recover it.
    /// </summary>
    Recovered,
}

public readonly record struct LoadResult(AppState State, LoadOutcome Outcome, string? Detail);

/// <summary>
/// Single-writer, atomic persistence for <see cref="AppState"/>.
/// <para>
/// Writes go to a sibling temp file and are then moved over the target, so a crash mid-write can
/// never leave a truncated document. Saves are serialised through one gate and coalesced, so a
/// drag that mutates geometry sixty times a second still produces one write.
/// </para>
/// </summary>
public sealed class AppStateStore : IAsyncDisposable
{
    private const string Scope = "store";

    private readonly string _path;
    private readonly TimeSpan _debounce;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Lock _pendingGate = new();

    private AppState? _pending;
    private CancellationTokenSource? _debounceCts;
    private bool _disposed;

    public AppStateStore(string path, TimeSpan? debounce = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(400);
    }

    public string Path => _path;

    /// <summary>The default on-disk location: %APPDATA%\Themia.WinUI\state.json.</summary>
    public static string DefaultPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return System.IO.Path.Combine(root, "Themia.WinUI", "state.json");
    }

    /// <summary>
    /// Read and validate. Never throws for bad data: a corrupt document is set aside and defaults
    /// are returned, loudly. The caller can tell that apart from a fresh profile via the outcome.
    /// </summary>
    public LoadResult Load()
    {
        string json;
        try
        {
            if (!File.Exists(_path))
            {
                Log.Info(Scope, $"no state at {_path}; seeding defaults");
                return new LoadResult(AppState.CreateDefault(), LoadOutcome.Fresh, null);
            }

            json = File.ReadAllText(_path);
        }
        catch (IOException ex)
        {
            return Recover("state file could not be read", ex, setAside: false);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Recover("state file could not be read", ex, setAside: false);
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            // A zero-byte file is the classic result of a crash during a non-atomic write. It is
            // corrupt, not absent.
            return Recover("state file is empty", null, setAside: true);
        }

        AppState? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(json, AppStateJsonContext.Default.AppState);
        }
        catch (JsonException ex)
        {
            return Recover("state file is not valid JSON", ex, setAside: true);
        }
        catch (NotSupportedException ex)
        {
            return Recover("state file holds an unsupported shape", ex, setAside: true);
        }

        if (parsed is null)
        {
            return Recover("state file deserialised to null", null, setAside: true);
        }

        if (parsed.SchemaVersion > AppState.CurrentSchemaVersion)
        {
            return Recover(
                $"state schema v{parsed.SchemaVersion} is newer than this build (v{AppState.CurrentSchemaVersion})",
                null,
                setAside: true);
        }

        var repairs = Validate(parsed);
        if (repairs.Count > 0)
        {
            Log.Warn(Scope, $"state loaded with {repairs.Count} repair(s): {string.Join("; ", repairs)}");
        }

        return new LoadResult(parsed, LoadOutcome.Loaded, repairs.Count > 0 ? string.Join("; ", repairs) : null);
    }

    /// <summary>
    /// Bring a deserialised document back inside its invariants, reporting what had to change.
    /// Pure and total: it repairs rather than rejects, because a half-valid layout is still the
    /// user's layout.
    /// </summary>
    public static List<string> Validate(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var repairs = new List<string>();

        state.Design ??= new DesignState();
        state.Views ??= [];
        state.MonitorViews ??= [];

        var design = state.Design;
        design.WidgetGapPx = Math.Clamp(Sanitise(design.WidgetGapPx, 8), 0, 64);
        design.CornerRadiusPx = Math.Clamp(Sanitise(design.CornerRadiusPx, 8), 0, 32);
        design.SurfaceOpacity = Math.Clamp(Sanitise(design.SurfaceOpacity, 1), 0.2, 1.0);
        design.UiScale = Math.Clamp(Sanitise(design.UiScale, 1), 0.5, 3.0);
        design.FontScale = Math.Clamp(Sanitise(design.FontScale, 1), 0.5, 2.0);
        design.BorderWidthPx = Math.Clamp(Sanitise(design.BorderWidthPx, 1), 0, 8);
        design.BorderOpacity = Math.Clamp(Sanitise(design.BorderOpacity, 1), 0, 1);
        design.IntroAnimationMs = Math.Clamp(Sanitise(design.IntroAnimationMs, 500), 0, 3000);

        // A malformed colour becomes empty, which means "use the theme's own" - the one value that
        // is always renderable. Keeping the bad string would leave the widget with no fill at all.
        design.WidgetBackgroundColor = Widgets.HexColor.Normalise(design.WidgetBackgroundColor);
        design.PrimaryColor = Widgets.HexColor.Normalise(design.PrimaryColor);
        design.BorderColor = Widgets.HexColor.Normalise(design.BorderColor);

        if (string.IsNullOrWhiteSpace(design.FontFamily))
        {
            design.FontFamily = "system";
        }

        if (!Enum.IsDefined(design.BorderStyle))
        {
            design.BorderStyle = Model.WidgetBorderStyle.Solid;
        }

        if (!Enum.IsDefined(design.IntroAnimation))
        {
            design.IntroAnimation = Model.IntroAnimation.Scale;
        }

        if (!Enum.IsDefined(design.Theme))
        {
            design.Theme = Model.AppTheme.System;
        }

        if (state.Views.Count == 0)
        {
            state.Views.Add(ViewState.Create("Desktop"));
            repairs.Add("no views; seeded one");
        }

        var seenViewIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var view in state.Views)
        {
            if (string.IsNullOrWhiteSpace(view.Id) || !seenViewIds.Add(view.Id))
            {
                view.Id = Guid.NewGuid().ToString("n");
                seenViewIds.Add(view.Id);
                repairs.Add("view had a missing or duplicate id");
            }

            if (string.IsNullOrWhiteSpace(view.Name))
            {
                view.Name = "View";
                repairs.Add("view had no name");
            }

            view.Widgets ??= [];
            repairs.AddRange(ValidateWidgets(view));
        }

        if (state.FindView(state.ActiveViewId) is null)
        {
            state.ActiveViewId = state.Views[0].Id;
            repairs.Add("active view id was dangling");
        }

        foreach (var monitorId in state.MonitorViews.Keys.ToList())
        {
            if (state.FindView(state.MonitorViews[monitorId]) is null)
            {
                state.MonitorViews.Remove(monitorId);
                repairs.Add($"monitor '{monitorId}' pointed at a deleted view");
            }
        }

        state.SchemaVersion = AppState.CurrentSchemaVersion;
        return repairs;
    }

    private static List<string> ValidateWidgets(ViewState view)
    {
        var repairs = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Geometry is validated against a unit screen: the stored model is fractional, so the
        // invariants (on-screen, non-inverted, finite) hold independently of any real monitor.
        var unit = new ScreenMetrics(1920, 1080);

        for (var i = view.Widgets.Count - 1; i >= 0; i--)
        {
            var widget = view.Widgets[i];

            if (!Enum.IsDefined(widget.Kind))
            {
                // An unknown kind is data this build cannot render at all. Dropping the instance is
                // the only honest option, and it is reported rather than silently swallowed.
                view.Widgets.RemoveAt(i);
                repairs.Add($"dropped widget of unknown kind '{(int)widget.Kind}'");
                continue;
            }

            if (string.IsNullOrWhiteSpace(widget.Id) || !seen.Add(widget.Id))
            {
                widget.Id = Guid.NewGuid().ToString("n");
                seen.Add(widget.Id);
                repairs.Add("widget had a missing or duplicate id");
            }

            if (widget.Config is null || widget.Config.Kind != widget.Kind)
            {
                widget.Config = WidgetConfig.CreateDefault(widget.Kind);
                repairs.Add($"widget '{widget.Id}' had a missing or mismatched config");
            }

            if (!Enum.IsDefined(widget.Surface))
            {
                widget.Surface = WidgetSurface.Card;
                repairs.Add($"widget '{widget.Id}' had an unknown surface");
            }

            if (!Enum.IsDefined(widget.Header))
            {
                widget.Header = HeaderPlacement.Top;
                repairs.Add($"widget '{widget.Id}' had an unknown header placement");
            }

            var settled = GridGeometry.Clamp(widget.Rect, unit);
            if (settled != widget.Rect)
            {
                widget.Rect = settled;
                repairs.Add($"widget '{widget.Id}' was off-screen or below the size floor");
            }
        }

        // Membership last, and only once every widget above is known good: the tree is walked by
        // id, so it has to be walked over ids that exist and are unique. A hand-edited file can
        // name a parent that is gone, a parent that is not a container, or a cycle - none of
        // which the UI can produce and all of which would otherwise reach a layout pass.
        var reparented = view.RepairTree();
        if (reparented > 0)
        {
            repairs.Add($"{reparented} widget(s) named a container that could not hold them");
        }

        return repairs;
    }

    private static double Sanitise(double value, double fallback)
        => double.IsFinite(value) ? value : fallback;

    private LoadResult Recover(string reason, Exception? error, bool setAside)
    {
        Log.Error(Scope, $"{reason}; falling back to defaults", error);

        if (setAside)
        {
            // Never delete the user's data on a recoverable error — move it where they can find it.
            var aside = $"{_path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            try
            {
                File.Move(_path, aside, overwrite: true);
                Log.Warn(Scope, $"previous state preserved at {aside}");
            }
            catch (IOException ex)
            {
                Log.Error(Scope, "could not set aside the corrupt state file", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Error(Scope, "could not set aside the corrupt state file", ex);
            }
        }

        return new LoadResult(AppState.CreateDefault(), LoadOutcome.Recovered, reason);
    }

    /// <summary>
    /// Queue a save. Repeated calls inside the debounce window collapse into one write of the
    /// latest snapshot, so a live drag does not hammer the disk.
    /// </summary>
    public void SaveSoon(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ObjectDisposedException.ThrowIf(_disposed, this);

        CancellationTokenSource cts;
        lock (_pendingGate)
        {
            _pending = state;
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            cts = _debounceCts;
        }

        _ = DebouncedSaveAsync(cts.Token);
    }

    private async Task DebouncedSaveAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_debounce, token).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            return; // superseded by a newer SaveSoon; that call owns the write
        }

        try
        {
            await FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // This runs detached from any caller, so an escaping exception would be lost entirely.
            // WriteAtomicAsync already handles the expected I/O failures; anything reaching here is
            // unexpected and must still be visible rather than vanish into an unobserved task.
            Log.Error(Scope, "background save failed", ex);
        }
    }

    /// <summary>
    /// Write any queued snapshot now and wait for it. Safe to call when nothing is pending.
    /// <para>
    /// The write gate is taken <i>before</i> the pending snapshot is read, so a flush that finds
    /// nothing queued still waits for an in-flight write to land. Reading the snapshot first would
    /// let this return while a debounced save was mid-write, i.e. "flushed" without durability —
    /// exactly the window a shutdown flush exists to close.
    /// </para>
    /// </summary>
    public async Task FlushAsync()
    {
        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            AppState? snapshot;
            lock (_pendingGate)
            {
                snapshot = _pending;
                _pending = null;
            }

            if (snapshot is null)
            {
                return;
            }

            await WriteAtomicAsync(snapshot).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task WriteAtomicAsync(AppState state)
    {
        var temp = $"{_path}.tmp";
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(state, AppStateJsonContext.Default.AppState);
            await File.WriteAllTextAsync(temp, json).ConfigureAwait(false);

            // Atomic swap: readers see either the old document or the new one, never a partial write.
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            Log.Error(Scope, $"could not save state to {_path}", ex);

            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch (IOException cleanupEx)
            {
                Log.Warn(Scope, "could not remove the temp state file", cleanupEx);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_pendingGate)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
        }

        await FlushAsync().ConfigureAwait(false);
        _writeGate.Dispose();
    }
}
