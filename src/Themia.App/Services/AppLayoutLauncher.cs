using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Themia.App.Interop;
using Themia.App.Services.Files;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Services;

/// <summary>
/// [S-1407]..[S-1409] Opening an app layout: every assigned pane's app, file, folder or web address,
/// each window put in its pane on the display the layout was clicked on (original
/// <c>launch_app_layout</c>).
/// <para>
/// <b>Launching</b> goes through <see cref="ShellLauncher.Open(string?, out int)"/>, the app's one
/// allow-listed shell call: a pane's target is checked like any shortcut's, and nothing is ever a
/// command line (the original's per-pane arguments are not ported for that reason).
/// </para>
/// <para>
/// <b>Waiting for the window is event-driven.</b> Before anything is launched, WinEvent hooks are set
/// for a window being shown, renamed (a splash becoming the document window) or brought to the front
/// (a single-instance app handing the launch to its running copy); each event is judged by the pure
/// <see cref="LayoutWindows.Judge"/>. A window that moves itself off its pane right after it was put
/// there (apps that restore their last position) is put back on its own location-change event, a
/// few times at most. The only clock is the give-up: after <see cref="WaitLimit"/> a launch that only
/// produced a splash gets that, one that produced nothing is logged, and the hooks come off. No
/// polling, no sleeps.
/// </para>
/// <para>
/// UI thread only: the hooks are out-of-context, so their callbacks arrive through this thread's
/// message loop, and no callback lets an exception reach Windows.
/// </para>
/// </summary>
internal static unsafe partial class AppLayoutLauncher
{
    private const string Scope = "app-layouts";

    /// <summary>How long a launch may take to show its window before the layout stops waiting for it.</summary>
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    /// <summary>How many times a window that moves itself off its pane is put back.</summary>
    private const int MaxReapply = 3;

    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectShow = 0x8002;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint EventObjectNameChange = 0x800C;
    private const int ObjIdWindow = 0;
    private const uint GaRoot = 2;
    private const uint GwOwner = 4;
    private const int SwRestore = 9;
    private const int DwmwaExtendedFrameBounds = 9;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint AsfwAny = unchecked((uint)-1);

    private static readonly List<Session> Sessions = [];
    private static readonly List<nint> Hooks = [];

    /// <summary>
    /// [S-1407] Open <paramref name="layout"/> on the display <paramref name="anchor"/> (the widget's
    /// window) is on; the primary display when there is none. An empty layout does nothing (S-53).
    /// </summary>
    public static void Launch(LayoutNode? layout, nint anchor)
    {
        try
        {
            var entries = LayoutPlan.Walk(layout);

            if (entries.Count == 0)
            {
                Log.Info(Scope, "the layout has no apps; nothing to open");
                return;
            }

            if (DispatcherQueue.GetForCurrentThread() is not { } dispatcher)
            {
                Log.Warn(Scope, "no dispatcher on this thread; the layout cannot wait for its windows");
                return;
            }

            var work = WorkArea(anchor);
            var session = new Session(dispatcher);
            Sessions.Add(session);
            EnsureHooks();

            // Granted before the launches so the apps may bring their windows forward: this surface
            // never takes the foreground itself.
            AllowSetForegroundWindow(AsfwAny);

            foreach (var entry in entries)
            {
                session.Start(entry, LayoutPlan.Place(entry.Area, work));
            }

            session.Arm(WaitLimit);
            Log.Info(Scope, $"opening a layout of {entries.Count} on {work.Width}x{work.Height} at {work.X},{work.Y}");
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "could not open the layout", ex);
        }
    }

    // ---- one launch of a layout -------------------------------------------------------------------

    private sealed class Pending
    {
        public required LayoutEntry Entry { get; init; }

        public required ScreenRect Pane { get; init; }

        public required WindowWatch Watch { get; set; }

        public nint Placed { get; set; }

        public nint Fallback { get; set; }

        public int Reapplied { get; set; }
    }

    private sealed class Session(DispatcherQueue dispatcher)
    {
        private readonly List<Pending> _pending = [];
        private readonly HashSet<nint> _claimed = [];
        private readonly Dictionary<int, string> _exes = [];
        private DispatcherQueueTimer? _limit;

        public void Start(LayoutEntry entry, ScreenRect pane)
        {
            var own = Environment.ProcessId;
            var windows = TopLevelWindows(this);
            var hint = ExeHint(entry);

            // [S-1409] An app that is already open is moved rather than started again.
            if (LayoutWindows.Running(windows, entry.Source, hint, own) is { } running && _claimed.Add(running.Handle))
            {
                Place(running.Handle, pane);

                if (entry.ReuseIfRunning)
                {
                    Log.Info(Scope, $"moved the running {running.ExeName} into its pane");
                    return;
                }
            }

            var before = windows.Select(window => window.Handle).ToHashSet();
            var outcome = ShellLauncher.Open(entry.Target, out var processId);

            if (outcome != LaunchOutcome.Opened)
            {
                Log.Warn(Scope, $"a pane did not open ({outcome})");
                return;
            }

            _pending.Add(new Pending
            {
                Entry = entry,
                Pane = pane,
                Watch = new WindowWatch(entry.Source, processId, hint, before, own),
            });
        }

        public void Arm(TimeSpan limit)
        {
            if (_pending.Count == 0)
            {
                End();
                return;
            }

            _limit = dispatcher.CreateTimer();
            _limit.IsRepeating = false;
            _limit.Interval = limit;
            _limit.Tick += (_, _) => GiveUp();
            _limit.Start();
        }

        /// <summary>A window event that may concern this launch.</summary>
        public void OnWindow(nint hwnd, uint kind)
        {
            if (kind == EventObjectLocationChange && _claimed.Contains(hwnd))
            {
                // [S-1408] One of ours moved: put it back if it left its pane (our own move lands
                // inside the slack and stops here).
                foreach (var pending in _pending)
                {
                    if (pending.Placed == hwnd && pending.Reapplied < MaxReapply && VisibleRect(hwnd) is { } visible
                        && LayoutPlan.Drifted(pending.Pane, visible))
                    {
                        pending.Reapplied++;
                        Place(hwnd, pending.Pane);
                    }
                }

                return;
            }

            // A move or a resize can also be what makes a launch's window its main one (a splash
            // growing into the document window), so it is judged like a show.
            if (_claimed.Contains(hwnd) || Facts(hwnd, this) is not { } facts)
            {
                return;
            }

            var toFront = kind == EventSystemForeground;

            foreach (var pending in _pending)
            {
                if (pending.Placed != 0)
                {
                    continue;
                }

                switch (LayoutWindows.Judge(pending.Watch, facts, toFront))
                {
                    case WindowMatch.Main:
                        _claimed.Add(hwnd);
                        pending.Placed = hwnd;
                        Place(hwnd, pending.Pane);
                        Log.Info(Scope, $"put {facts.ExeName} in its pane");
                        return;

                    case WindowMatch.Fallback when pending.Fallback == 0:
                        pending.Fallback = hwnd;
                        return;
                }
            }
        }

        /// <summary>
        /// The wait is over: a launch that only showed a splash or an untitled window gets that one;
        /// one that showed nothing is logged. Then the hooks come off.
        /// </summary>
        private void GiveUp()
        {
            foreach (var pending in _pending)
            {
                if (pending.Placed != 0)
                {
                    continue;
                }

                if (pending.Fallback != 0 && Win32.IsWindow(pending.Fallback))
                {
                    Place(pending.Fallback, pending.Pane);
                }
                else
                {
                    Log.Warn(Scope, $"no window appeared for a {pending.Entry.Source} pane within {WaitLimit.TotalSeconds:0} s");
                }
            }

            End();
        }

        private void End()
        {
            _limit?.Stop();
            _limit = null;
            Sessions.Remove(this);

            if (Sessions.Count == 0)
            {
                RemoveHooks();
            }
        }

        public string ExeOf(int processId)
        {
            if (!_exes.TryGetValue(processId, out var exe))
            {
                exe = ProcessExe(processId);
                _exes[processId] = exe;
            }

            return exe;
        }
    }

    // ---- hooks ------------------------------------------------------------------------------------

    private static void EnsureHooks()
    {
        if (Hooks.Count > 0)
        {
            return;
        }

        var callback = (nint)(delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void>)&OnWinEvent;

        foreach (var kind in (uint[])[EventSystemForeground, EventObjectShow, EventObjectNameChange, EventObjectLocationChange])
        {
            var hook = Win32.SetWinEventHook(kind, kind, 0, callback, 0, 0, Win32.WinEventOutOfContext | Win32.WinEventSkipOwnProcess);

            if (hook == 0)
            {
                Log.Warn(Scope, $"could not watch window event 0x{kind:X4}; some windows may not reach their panes");
                continue;
            }

            Hooks.Add(hook);
        }
    }

    private static void RemoveHooks()
    {
        foreach (var hook in Hooks)
        {
            if (!Win32.UnhookWinEvent(hook))
            {
                Log.Warn(Scope, "could not remove a window-event hook");
            }
        }

        Hooks.Clear();
    }

    [UnmanagedCallersOnly]
    private static void OnWinEvent(nint hook, uint kind, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            if (hwnd == 0 || idObject != ObjIdWindow || idChild != 0 || GetAncestor(hwnd, GaRoot) != hwnd)
            {
                return;
            }

            // A copy: a session that ends inside the loop removes itself.
            foreach (var session in Sessions.ToArray())
            {
                session.OnWindow(hwnd, kind);
            }
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "a window event could not be handled", ex);
        }
    }

    // ---- windows ----------------------------------------------------------------------------------

    /// <summary>
    /// [S-1408] Put a window in its pane: out of maximised or minimised first, then the rectangle,
    /// widened by its invisible resize border so the visible frame fills the pane. Never activates
    /// it and never changes its z-order.
    /// </summary>
    private static void Place(nint hwnd, ScreenRect pane)
    {
        if (IsZoomed(hwnd) || Win32.IsIconic(hwnd))
        {
            Win32.ShowWindow(hwnd, SwRestore);
        }

        const uint flags = Win32.SwpNoZOrder | Win32.SwpNoActivate;

        if (!Win32.SetWindowPos(hwnd, 0, pane.X, pane.Y, pane.Width, pane.Height, flags))
        {
            Log.Warn(Scope, $"could not move a window (error {Marshal.GetLastPInvokeError()})");
            return;
        }

        if (Win32.GetWindowRect(hwnd, out var window) && VisibleRect(hwnd) is { } frame)
        {
            var target = LayoutPlan.WithInvisibleBorders(
                pane,
                frame.X - window.Left,
                frame.Y - window.Top,
                window.Right - frame.Right,
                window.Bottom - frame.Bottom);

            if (target != pane)
            {
                Win32.SetWindowPos(hwnd, 0, target.X, target.Y, target.Width, target.Height, flags);
            }
        }
    }

    /// <summary>The visible frame (DWM's extended frame bounds), or null when DWM will not say.</summary>
    private static ScreenRect? VisibleRect(nint hwnd)
    {
        Win32.Rect frame;

        if (DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, &frame, sizeof(Win32.Rect)) < 0)
        {
            return null;
        }

        return new ScreenRect(frame.Left, frame.Top, frame.Width, frame.Height);
    }

    /// <summary>The top-level windows in z-order, top first.</summary>
    private static List<WindowFacts> TopLevelWindows(Session session)
    {
        var handles = new List<nint>();
        var handle = GCHandle.Alloc(handles);

        try
        {
            Win32.EnumWindows((nint)(delegate* unmanaged<nint, nint, int>)&OnEnumWindow, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        var windows = new List<WindowFacts>(handles.Count);

        foreach (var hwnd in handles)
        {
            if (Facts(hwnd, session) is { } facts)
            {
                windows.Add(facts);
            }
        }

        return windows;
    }

    [UnmanagedCallersOnly]
    private static int OnEnumWindow(nint hwnd, nint state)
    {
        try
        {
            if (GCHandle.FromIntPtr(state).Target is List<nint> into && Win32.IsWindowVisible(hwnd))
            {
                into.Add(hwnd);
            }
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "could not list a window", ex);
        }

        return 1;
    }

    /// <summary>What a layout needs to know about a window; null when it is gone.</summary>
    private static WindowFacts? Facts(nint hwnd, Session session)
    {
        if (!Win32.IsWindow(hwnd) || !Win32.GetWindowRect(hwnd, out var rect))
        {
            return null;
        }

        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        var exStyle = (long)Win32.GetWindowLongPtr(hwnd, Win32.GwlExStyle);
        var className = stackalloc char[64];
        var classLength = Win32.GetClassName(hwnd, className, 64);

        return new WindowFacts(
            hwnd,
            (int)pid,
            session.ExeOf((int)pid),
            classLength > 0 ? new string(className, 0, classLength) : string.Empty,
            Win32.IsWindowVisible(hwnd),
            Win32.GetWindow(hwnd, GwOwner) != 0,
            (exStyle & Win32.WsExToolWindow) != 0,
            Win32.GetWindowTextLength(hwnd),
            rect.Width,
            rect.Height);
    }

    /// <summary>A process's exe file name, or empty when it cannot be read (elevated, gone).</summary>
    private static string ProcessExe(int processId)
    {
        if (processId == 0)
        {
            return string.Empty;
        }

        var process = OpenProcess(ProcessQueryLimitedInformation, 0, (uint)processId);

        if (process == 0)
        {
            return string.Empty;
        }

        try
        {
            var buffer = stackalloc char[1024];
            var length = 1024u;
            return QueryFullProcessImageName(process, 0, buffer, &length) != 0
                ? Path.GetFileName(new string(buffer, 0, (int)length))
                : string.Empty;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    // ---- what a pane's window belongs to ------------------------------------------------------------

    /// <summary>
    /// [S-1409] The exe whose window a pane opens: an app's link target, an <c>.exe</c> itself, the
    /// program a file type opens with, the default browser for a web address (original S-70); none
    /// for a folder, which is matched by window class.
    /// </summary>
    private static string? ExeHint(LayoutEntry entry)
    {
        try
        {
            switch (entry.Source)
            {
                case LayoutSource.Folder:
                    return null;

                case LayoutSource.Url:
                    return Associated("https", protocol: true);
            }

            var target = entry.Target;

            if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && LaunchTargets.MayProbe(LaunchTargets.Classify(target)))
            {
                target = ShellFileOps.ResolveShortcut(target) ?? target;
            }

            var extension = Path.GetExtension(target);

            if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileName(target);
            }

            return extension.Length > 1 ? Associated(extension, protocol: false) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            Log.Debug(Scope, $"could not tell which program a pane opens: {ex.Message}");
            return null;
        }
    }

    /// <summary>The exe that opens a file type or a protocol (<c>AssocQueryStringW</c>), or null.</summary>
    private static string? Associated(string association, bool protocol)
    {
        const uint assocFIsProtocol = 0x00001000;
        const int assocStrExecutable = 2;

        var buffer = stackalloc char[1024];
        var length = 1024u;

        fixed (char* assoc = association)
        {
            if (AssocQueryString(protocol ? assocFIsProtocol : 0, assocStrExecutable, assoc, null, buffer, &length) < 0)
            {
                return null;
            }
        }

        var exe = new string(buffer);
        return exe.Length == 0 ? null : Path.GetFileName(exe);
    }

    /// <summary>The work area of the display <paramref name="anchor"/> is on (the primary's for none), in physical pixels.</summary>
    private static ScreenRect WorkArea(nint anchor)
    {
        const uint defaultToNearest = 2;
        const uint defaultToPrimary = 1;

        var monitor = MonitorFromWindow(anchor, anchor == 0 ? defaultToPrimary : defaultToNearest);
        var info = new Win32.MonitorInfoEx { Size = sizeof(Win32.MonitorInfoEx) };

        if (monitor == 0 || !Win32.GetMonitorInfo(monitor, ref info))
        {
            Log.Warn(Scope, "could not read the display's work area; using 1920x1040");
            return new ScreenRect(0, 0, 1920, 1040);
        }

        return new ScreenRect(info.Work.Left, info.Work.Top, info.Work.Width, info.Work.Height);
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsZoomed(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint processId);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint hwnd, int attribute, Win32.Rect* value, int size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access, int inherit, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    private static partial int QueryFullProcessImageName(nint process, uint flags, char* name, uint* size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("shlwapi.dll", EntryPoint = "AssocQueryStringW")]
    private static partial int AssocQueryString(uint flags, int str, char* association, char* extra, char* output, uint* length);
}
