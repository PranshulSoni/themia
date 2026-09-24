using System.Runtime.InteropServices;
using System.Text;
using Themia.Core.Diagnostics;

namespace Themia.App.Interop;

/// <summary>
/// Notices when Windows shows the desktop (Win+D, or the Show Desktop button), so the widgets can
/// stay visible.
/// <para>
/// The widget surface is pinned to the bottom of the z-order, which is exactly where it belongs
/// while the user has windows open. But Show Desktop works by raising the desktop window itself
/// above everything — including anything pinned below it — so the widgets vanished at the one moment
/// the user was asking to see their desktop.
/// </para>
/// <para>
/// While the desktop is fronted there is nothing else on screen for the widgets to cover, so lifting
/// them for the duration does not break the "never above other windows" rule; it is the only way to
/// honour it and still be visible. They drop straight back the moment anything else takes focus.
/// </para>
/// </summary>
internal static unsafe class ShowDesktopWatcher
{
    private const string Scope = "show-desktop";

    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WinEventSkipOwnProcess = 0x0002;

    private static nint _hook;
    private static bool _active;

    /// <summary>Raised on the UI thread with true while the desktop is fronted.</summary>
    public static event Action<bool>? Changed;

    /// <summary>Whether the desktop is currently the foreground window.</summary>
    public static bool IsShowingDesktop => _active;

    /// <summary>
    /// Start watching. Idempotent. Installed out-of-context so the callback arrives on this thread's
    /// message queue rather than inside another process.
    /// </summary>
    public static void Start()
    {
        if (_hook != 0)
        {
            return;
        }

        var callback = (nint)(delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void>)&OnWinEvent;

        _hook = Win32.SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            0,
            callback,
            0,
            0,
            WinEventOutOfContext | WinEventSkipOwnProcess);

        if (_hook == 0)
        {
            Log.Warn(Scope, "could not watch for Show Desktop; widgets will hide when the desktop is fronted");
        }
    }

    public static void Stop()
    {
        if (_hook == 0)
        {
            return;
        }

        Win32.UnhookWinEvent(_hook);
        _hook = 0;
    }

    [UnmanagedCallersOnly]
    private static void OnWinEvent(nint hook, uint eventId, nint hwnd, int objectId, int childId, uint thread, uint time)
    {
        // A system hook callback runs on Windows' own call stack: letting a managed exception unwind
        // out of here is undefined behaviour, and a hook whose callback misbehaves is silently
        // removed for the rest of the session.
        try
        {
            if (hwnd == 0)
            {
                return;
            }

            var showing = IsDesktopWindow(hwnd);
            if (showing == _active)
            {
                return;
            }

            _active = showing;
            Changed?.Invoke(showing);
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "the Show Desktop hook threw", ex);
        }
    }

    /// <summary>
    /// Whether a window is the desktop. Both classes matter: <c>Progman</c> is the desktop itself,
    /// and <c>WorkerW</c> is the wallpaper host that takes its place once a slideshow or certain
    /// shell features are in use.
    /// </summary>
    /// <summary>
    /// Whether a window is the desktop. Both classes matter: <c>Progman</c> is the desktop itself,
    /// and <c>WorkerW</c> is the wallpaper host that takes its place once a slideshow or certain
    /// shell features are in use.
    /// </summary>
    private static string ClassOf(nint hwnd)
    {
        Span<char> buffer = stackalloc char[64];
        fixed (char* name = buffer)
        {
            var length = Win32.GetClassName(hwnd, name, buffer.Length);
            return length > 0 ? new string(name, 0, length) : "?";
        }
    }

    private static bool IsDesktopWindow(nint hwnd)
    {
        Span<char> buffer = stackalloc char[64];

        fixed (char* name = buffer)
        {
            var length = Win32.GetClassName(hwnd, name, buffer.Length);
            if (length <= 0)
            {
                return false;
            }

            var className = new string(name, 0, length);
            return className.Equals("Progman", StringComparison.Ordinal)
                || className.Equals("WorkerW", StringComparison.Ordinal);
        }
    }
}
