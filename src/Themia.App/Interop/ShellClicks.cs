using System.Runtime.InteropServices;
using Themia.App.Services;
using Themia.Core.Diagnostics;
using Themia.Core.Shell;

namespace Themia.App.Interop;

/// <summary>
/// [S-938] What a click on a widget has to clean up after itself.
/// <para>
/// Outside edit mode the surface is <c>WS_EX_NOACTIVATE</c>, so a click on it changes nothing about
/// the foreground — and three shell surfaces wait for exactly that change before they close: Start
/// and Search, the desktop's (or any app's) context menu, and the desktop's icon selection. Each is
/// nudged here the way a click on an ordinary window would have.
/// </para>
/// </summary>
internal static unsafe class ShellClicks
{
    private const string Scope = "shell-clicks";

    /// <summary>Single-flight for the selection clear, which crosses into Explorer and may be slow.</summary>
    private static int _clearing;

    /// <summary>Set while a sweep is running, read back by the enumeration callback.</summary>
    private static bool _menuFound;

    /// <summary>
    /// A press landed on a widget surface. <paramref name="activatable"/> is true in edit mode, where
    /// the press activates the surface and Windows closes Start and menus on its own; an Escape sent
    /// then would land on the surface itself instead.
    /// </summary>
    public static void OnSurfacePressed(bool activatable)
    {
        try
        {
            if (!activatable && (StartOrSearchInFront() || ShellMenuOpen()))
            {
                PressEscape();
            }
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "dismissing Start or a menu threw", ex);
        }

        if (Interlocked.Exchange(ref _clearing, 1) == 0)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    DesktopIcons.ClearSelection();
                }
                catch (Exception ex)
                {
                    Log.Debug(Scope, $"clearing the desktop selection threw: {ex.Message}");
                }
                finally
                {
                    Volatile.Write(ref _clearing, 0);
                }
            });
        }
    }

    /// <summary>Start or Search has the foreground. The class check is free; the process is only asked when it matches.</summary>
    private static bool StartOrSearchInFront()
    {
        var foreground = Win32.GetForegroundWindow();
        var windowClass = ClassOf(foreground);
        if (windowClass != ShellClickRules.CoreWindowClass)
        {
            return false;
        }

        Win32.GetWindowThreadProcessId(foreground, out var pid);
        return ShellClickRules.IsStartOrSearch(windowClass, ProcessNames.ExePath(pid));
    }

    /// <summary>
    /// A context menu is open. The Windows 11 menu never takes the foreground, so this is a sweep of
    /// the top-level windows: a class read per window, and a process query only for a visible popup
    /// that could be a menu — the click with no menu open never gets that far.
    /// </summary>
    private static bool ShellMenuOpen()
    {
        _menuFound = false;
        Win32.EnumWindows((nint)(delegate* unmanaged<nint, nint, int>)&OnWindow, 0);
        return _menuFound;
    }

    [UnmanagedCallersOnly]
    private static int OnWindow(nint hwnd, nint lParam)
    {
        try
        {
            if (!Win32.IsWindowVisible(hwnd))
            {
                return 1;
            }

            var windowClass = ClassOf(hwnd);
            if (!ShellClickRules.IsMenuCandidate(windowClass))
            {
                return 1;
            }

            string? owner = null;
            if (windowClass != ShellClickRules.ClassicMenuClass)
            {
                Win32.GetWindowThreadProcessId(hwnd, out var pid);
                owner = ProcessNames.ExePath(pid);
            }

            if (ShellClickRules.IsShellMenu(windowClass, owner))
            {
                _menuFound = true;
                return 0;
            }
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "the menu sweep threw", ex);
            return 0;
        }

        return 1;
    }

    /// <summary>
    /// One Escape, to whatever has the foreground. Bringing the desktop forward instead does not
    /// work: a process that never takes the foreground has no right to give it away.
    /// </summary>
    private static void PressEscape()
    {
        var inputs = stackalloc Win32.Input[2];
        inputs[0] = Key(up: false);
        inputs[1] = Key(up: true);

        if (Win32.SendInput(2, inputs, sizeof(Win32.Input)) != 2)
        {
            Log.Debug(Scope, $"SendInput did not take (win32 {Marshal.GetLastWin32Error()}); Start or the menu stays open");
        }

        static Win32.Input Key(bool up) => new()
        {
            Type = Win32.InputKeyboard,
            Data = new Win32.InputUnion
            {
                Keyboard = new Win32.KeyboardInput { Vk = Win32.VkEscape, Flags = up ? Win32.KeyEventKeyUp : 0 },
            },
        };
    }

    private static string? ClassOf(nint hwnd)
    {
        if (hwnd == 0)
        {
            return null;
        }

        var buffer = stackalloc char[64];
        var length = Win32.GetClassName(hwnd, buffer, 64);
        return length > 0 ? new string(buffer, 0, length) : null;
    }
}
