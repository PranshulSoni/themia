using System.Runtime.InteropServices;
using Themia.Core.Diagnostics;

namespace Themia.App.Interop;

/// <summary>
/// [S-927] Tells game mode when to look again. Event-driven: no timer, nothing wakes while nothing
/// changes, and it exists only while the game-mode switch is on.
/// <para>
/// The original asked the shell every two seconds. Here every way a full-screen app can arrive or
/// leave has its own event, and each one only says "look again" — the decision is always taken from
/// a fresh query ([S-920]), so a missed or duplicated event can delay it but never invert it:
/// </para>
/// <list type="bullet">
/// <item>the foreground changes (a game launched, alt-tabbed, minimised or quit) —
/// <c>EVENT_SYSTEM_FOREGROUND</c>;</item>
/// <item>the window already in front becomes or stops being full-screen (a browser's F11, a video's
/// full-screen button, a borderless game toggling its mode) — the shell hook's
/// <c>HSHELL_RUDEAPPACTIVATED</c> / <c>HSHELL_WINDOWACTIVATED</c>, which is how the taskbar learns
/// to get out of the way, and <c>HSHELL_MONITORCHANGED</c> when it moves display;</item>
/// <item>an exclusive full-screen game switches the display mode — <c>WM_DISPLAYCHANGE</c>.</item>
/// </list>
/// <para>
/// Its own hidden top-level window, not the command window's: the shell hook and the broadcasts need
/// a real top-level window, and keeping it separate means switching game mode off removes every hook
/// it ever added, rather than leaving a registration on a window that lives for the whole session.
/// </para>
/// </summary>
internal sealed unsafe class FullscreenWatch : IDisposable
{
    private const string Scope = "game-mode";
    private const string ClassName = "ThemiaFullscreenWatch";

    private const nint HshellWindowDestroyed = 2;
    private const nint HshellWindowActivated = 4;
    private const nint HshellMonitorChanged = 16;
    private const nint HshellRudeAppActivated = 0x8004;

    private static FullscreenWatch? _instance;
    private static ushort _atom;
    private static uint _shellHookMessage;

    private nint _hwnd;
    private nint _foregroundHook;
    private bool _shellHooked;
    private bool _disposed;

    /// <summary>Something happened that may have changed whether a full-screen app has the screen. Raised on the UI thread.</summary>
    public event Action? Changed;

    /// <summary>Create the window and install the hooks. Returns false (logged) rather than throwing.</summary>
    public bool Start()
    {
        if (_instance is not null && !ReferenceEquals(_instance, this))
        {
            Log.Error(Scope, "a full-screen watch already exists; refusing to start a second");
            return false;
        }

        _instance = this;
        var hInstance = Win32.GetModuleHandle(null);

        if (_atom == 0)
        {
            fixed (char* className = ClassName)
            {
                var wc = new Win32.WndClassEx
                {
                    Size = (uint)sizeof(Win32.WndClassEx),
                    WndProc = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&StaticWndProc,
                    Instance = hInstance,
                    ClassName = (nint)className,
                };

                _atom = Win32.RegisterClassEx(ref wc);
            }

            if (_atom == 0)
            {
                Log.Error(Scope, $"RegisterClassEx failed (win32 {Marshal.GetLastWin32Error()}); game mode cannot watch for full-screen apps");
                return false;
            }
        }

        _hwnd = Win32.CreateWindowEx(0, ClassName, "Themia full-screen watch", Win32.WsPopup, 0, 0, 0, 0, 0, 0, hInstance, 0);
        if (_hwnd == 0)
        {
            Log.Error(Scope, $"CreateWindowEx failed (win32 {Marshal.GetLastWin32Error()}); game mode cannot watch for full-screen apps");
            return false;
        }

        if (_shellHookMessage == 0)
        {
            _shellHookMessage = Win32.RegisterWindowMessage("SHELLHOOK");
        }

        _shellHooked = _shellHookMessage != 0 && Win32.RegisterShellHookWindow(_hwnd);
        if (!_shellHooked)
        {
            // Foreground changes still arrive, so a game that is launched or alt-tabbed into is still
            // caught; only a window going full-screen in place is missed until the next switch.
            Log.Warn(Scope, $"could not register for shell notifications (win32 {Marshal.GetLastWin32Error()}); " +
                "a window that goes full-screen without changing focus is noticed at the next focus change");
        }

        var callback = (nint)(delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void>)&OnWinEvent;
        _foregroundHook = Win32.SetWinEventHook(
            Win32.EventSystemForeground,
            Win32.EventSystemForeground,
            0,
            callback,
            0,
            0,
            Win32.WinEventOutOfContext | Win32.WinEventSkipOwnProcess);

        if (_foregroundHook == 0)
        {
            Log.Warn(Scope, "could not watch foreground changes; game mode reacts only to full-screen and display changes");
        }

        return true;
    }

    private void Raise()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "a full-screen watch subscriber threw", ex);
        }
    }

    [UnmanagedCallersOnly]
    private static nint StaticWndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        // Called by Windows itself: a managed exception unwinding out of here is undefined behaviour.
        try
        {
            var self = _instance;
            if (self is not null && self._hwnd == hwnd)
            {
                if (_shellHookMessage != 0 && msg == _shellHookMessage)
                {
                    if (wParam is HshellWindowActivated or HshellRudeAppActivated or HshellMonitorChanged or HshellWindowDestroyed)
                    {
                        self.Raise();
                    }

                    return 0;
                }

                if (msg == Win32.WmDisplayChangeMessage)
                {
                    self.Raise();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(Scope, $"the full-screen watch window procedure threw handling message 0x{msg:X}", ex);
        }

        return Win32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    [UnmanagedCallersOnly]
    private static void OnWinEvent(nint hook, uint eventId, nint hwnd, int objectId, int childId, uint thread, uint time)
    {
        // A hook whose callback throws is silently removed for the rest of the session.
        try
        {
            _instance?.Raise();
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "the foreground hook threw", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_foregroundHook != 0)
        {
            Win32.UnhookWinEvent(_foregroundHook);
            _foregroundHook = 0;
        }

        if (_shellHooked)
        {
            Win32.DeregisterShellHookWindow(_hwnd);
            _shellHooked = false;
        }

        if (_hwnd != 0)
        {
            Win32.DestroyWindow(_hwnd);
            _hwnd = 0;
        }

        if (ReferenceEquals(_instance, this))
        {
            _instance = null;
        }
    }
}
