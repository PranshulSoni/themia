using System.Runtime.InteropServices;
using Themia.Core.Diagnostics;
using Themia.Core.Shell;

namespace Themia.App.Interop;

/// <summary>
/// [S-930] Windows' own desktop icons: whether they are shown, the switch that hides them, and their
/// selection.
/// <para>
/// Everything goes through the shell, never around it. Hiding is the exact command the desktop's
/// context menu posts (View › Show desktop icons), so Explorer hides the icons <i>and</i> persists it
/// in its own settings: the choice survives Themia quitting, Explorer restarting and a reboot, and
/// the context menu's check mark stays honest. Themia never re-asserts or restores it ([S-932]).
/// </para>
/// <para>
/// The shell's windows are looked up on every use rather than cached: Explorer restarts replace
/// them, and a cached handle would post the command at a dead window. The lookup is two or three
/// <c>FindWindowEx</c> calls, and it only runs on a user action or a shell event.
/// </para>
/// </summary>
internal static unsafe class DesktopIcons
{
    private const string Scope = "desktop-icons";

    /// <summary>The desktop context menu's "Show desktop icons" command. Undocumented, unchanged since Vista, the same on 10 and 11.</summary>
    private const nint ToggleIconsCommand = 0x7402;

    private const uint LvmGetSelectedCount = 0x1032;

    /// <summary>
    /// The desktop's view (<c>SHELLDLL_DefView</c>) and its icon list (<c>SysListView32</c>), or null
    /// when Explorer is not running. The view lives under <c>Progman</c>, or under a <c>WorkerW</c>
    /// once a wallpaper slideshow or a wallpaper engine has put one in between.
    /// </summary>
    public static (nint View, nint List)? Find()
    {
        var view = Win32.FindWindowEx(Win32.FindWindow("Progman", null), 0, "SHELLDLL_DefView", null);

        var worker = (nint)0;
        while (view == 0)
        {
            worker = Win32.FindWindowEx(0, worker, "WorkerW", null);
            if (worker == 0)
            {
                return null;
            }

            view = Win32.FindWindowEx(worker, 0, "SHELLDLL_DefView", null);
        }

        // FindWindowEx ignores visibility, so the list is found whether the icons are shown or not.
        var list = Win32.FindWindowEx(view, 0, "SysListView32", null);
        return list == 0 ? null : (view, list);
    }

    /// <summary>
    /// [S-931] Whether the icons are shown, or null when the shell cannot be found. Explorer hides the
    /// list window itself, so its visibility <i>is</i> the state and is current the instant the shell
    /// has handled the command — unlike the persisted flag, which Explorer writes lazily.
    /// </summary>
    public static bool? AreVisible() => Find() is { } shell ? Win32.IsWindowVisible(shell.List) : null;

    /// <summary>
    /// [S-931] Show or hide the icons. The command is a toggle with no "set" form, so it is posted
    /// only when the live state is known and differs. Posted, never sent: the window lives in
    /// Explorer, and a synchronous call would block on the shell's whole handling of the command.
    /// </summary>
    /// <returns>Whether the command was posted.</returns>
    public static bool SetVisible(bool visible)
    {
        var shell = Find();
        if (shell is null)
        {
            Log.Warn(Scope, "the desktop's icon view was not found; desktop icons left as they are");
            return false;
        }

        if (!DesktopIconRules.NeedsToggle(Win32.IsWindowVisible(shell.Value.List), visible))
        {
            return false;
        }

        if (!Win32.PostMessage(shell.Value.View, Win32.WmCommand, ToggleIconsCommand, 0))
        {
            Log.Warn(Scope, $"could not post the desktop icons command (win32 {Marshal.GetLastWin32Error()})");
            return false;
        }

        Log.Info(Scope, visible ? "showing desktop icons" : "hiding desktop icons");
        return true;
    }

    /// <summary>
    /// [S-938] Clear the desktop's icon selection, as a click anywhere else on the desktop would.
    /// <para>
    /// Asks first, cheaply: <c>LVM_GETSELECTEDCOUNT</c> needs no memory in Explorer, so the common
    /// click with nothing selected costs one message. Only when something is selected does it talk
    /// to the desktop's folder view (<see cref="DesktopFolderView"/>). Blocking — call it off the UI
    /// thread.
    /// </para>
    /// </summary>
    public static void ClearSelection()
    {
        if (Find() is not { } shell)
        {
            return;
        }

        // Bounded: a hung Explorer must not hold this thread.
        if (Win32.SendMessageTimeout(shell.List, LvmGetSelectedCount, 0, 0, Win32.SmtoAbortIfHung | Win32.SmtoBlock, 200, out var count) == 0
            || count <= 0)
        {
            return;
        }

        DesktopFolderView.DeselectAll();
    }

    /// <summary>
    /// [S-932] Hear the icons being shown or hidden, by anyone — the desktop's context menu, a script,
    /// this app. The shell broadcasts nothing for it, but the list window's own show and hide are
    /// window events, so a WinEvent hook scoped to that one thread hears exactly them. Dispose to stop.
    /// Returns null (logged) when the shell cannot be found or the hook cannot be installed.
    /// </summary>
    public static IDisposable? Watch(Action changed)
    {
        ArgumentNullException.ThrowIfNull(changed);

        if (Find() is not { } shell)
        {
            return null;
        }

        var thread = Win32.GetWindowThreadProcessId(shell.List, out var pid);
        if (thread == 0)
        {
            return null;
        }

        var watch = new IconWatch(shell.List, changed);
        var callback = (nint)(delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void>)&OnWinEvent;
        watch.Hook = Win32.SetWinEventHook(Win32.EventObjectShow, Win32.EventObjectHide, 0, callback, pid, thread, Win32.WinEventOutOfContext);

        if (watch.Hook == 0)
        {
            Log.Warn(Scope, "could not watch the desktop icons; the switch shows what was read when the page opened");
            return null;
        }

        lock (Watches)
        {
            Watches.Add(watch);
        }

        return watch;
    }

    private static readonly List<IconWatch> Watches = [];

    [UnmanagedCallersOnly]
    private static void OnWinEvent(nint hook, uint eventId, nint hwnd, int objectId, int childId, uint thread, uint time)
    {
        // A hook callback must never let an exception out; one that misbehaves is removed for good.
        try
        {
            // Icons (child items) are shown and hidden all the time; only the list window itself is the switch.
            if (objectId != Win32.ObjIdWindow || childId != Win32.ChildIdSelf)
            {
                return;
            }

            IconWatch? match = null;
            lock (Watches)
            {
                foreach (var watch in Watches)
                {
                    if (watch.Hook == hook && watch.List == hwnd)
                    {
                        match = watch;
                        break;
                    }
                }
            }

            match?.Changed();
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "the desktop icons hook threw", ex);
        }
    }

    private sealed class IconWatch(nint list, Action changed) : IDisposable
    {
        public nint List { get; } = list;

        public nint Hook { get; set; }

        public void Changed() => changed();

        public void Dispose()
        {
            lock (Watches)
            {
                Watches.Remove(this);
            }

            if (Hook != 0)
            {
                Win32.UnhookWinEvent(Hook);
                Hook = 0;
            }
        }
    }
}
