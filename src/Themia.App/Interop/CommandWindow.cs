using System.Runtime.InteropServices;
using Themia.Core.Diagnostics;

namespace Themia.App.Interop;

/// <summary>
/// The app's one hidden window, and the only way to reach it from outside its own surfaces.
/// <para>
/// Themia deliberately puts nothing in the notification area: it is wallpaper with widgets on it,
/// not a program the user runs, and a permanent tray entry advertises it as the latter. That leaves
/// two ways in, and this window is both of them — a global hotkey, and a second launch of the
/// executable, which activates the copy already running instead of starting another one.
/// </para>
/// <para>
/// Not a XAML window: it is never shown and never painted, and paying for a XAML island and its
/// render thread to own a hotkey is exactly the cost this port exists to avoid.
/// </para>
/// </summary>
internal sealed unsafe class CommandWindow : IDisposable
{
    private const string Scope = "command";

    /// <summary>
    /// Also the address the next launch looks the running copy up by, so it must stay stable across
    /// versions — an old copy and a new one that disagree about it are two visible desktops.
    /// </summary>
    private const string ClassName = "ThemiaCommandWindow";

    private const uint WsPopup = 0x80000000;
    private const int HotkeyId = 1;

    private static CommandWindow? _instance;
    private static ushort _atom;

    /// <summary>
    /// The "you are already running" message. Registered by name, so both processes resolve it to
    /// the same number without sharing anything else.
    /// </summary>
    private static uint _activateMessage;

    private nint _hwnd;
    private bool _hotkeyRegistered;
    private bool _disposed;

    /// <summary>Raised when the user asks for edit mode, by hotkey or by launching the app again.</summary>
    public event Action? EditModeToggleRequested;

    /// <summary>
    /// Hand the request to a copy that is already running and report that we should stand down.
    /// <para>
    /// Called before anything is built, because the alternative is two processes drawing two widget
    /// layers over one wallpaper, with nothing on screen to say which is which and no tray icon
    /// left to tell them apart by.
    /// </para>
    /// </summary>
    public static bool TrySignalRunningInstance()
    {
        var existing = Win32.FindWindow(ClassName, null);
        if (existing == 0)
        {
            return false;
        }

        var message = ActivateMessage();
        if (message == 0)
        {
            // We know something is already running but cannot reach it. Starting a second desktop
            // layer on top of it is the worse of the two outcomes.
            Log.Error(Scope, "another copy is running but the activation message could not be registered; not starting a second");
            return true;
        }

        if (!Win32.PostMessage(existing, message, 0, 0))
        {
            Log.Error(Scope, $"could not signal the running copy (win32 {Marshal.GetLastWin32Error()}); not starting a second");
        }

        return true;
    }

    private static uint ActivateMessage()
    {
        if (_activateMessage == 0)
        {
            _activateMessage = Win32.RegisterWindowMessage("ThemiaActivate");
        }

        return _activateMessage;
    }

    /// <summary>Create the window and claim the hotkey. Returns false (logged) rather than throwing.</summary>
    public bool Create()
    {
        if (_instance is not null && !ReferenceEquals(_instance, this))
        {
            Log.Error(Scope, "a command window already exists; refusing to create a second");
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
                Log.Error(Scope, $"RegisterClassEx failed (win32 {Marshal.GetLastWin32Error()})");
                return false;
            }
        }

        // A real top-level window rather than HWND_MESSAGE: the next launch finds this one with
        // FindWindow, which does not see message-only windows. It is never shown.
        _hwnd = Win32.CreateWindowEx(0, ClassName, "Themia", WsPopup, 0, 0, 0, 0, 0, 0, hInstance, 0);
        if (_hwnd == 0)
        {
            Log.Error(Scope, $"CreateWindowEx failed (win32 {Marshal.GetLastWin32Error()})");
            return false;
        }

        if (ActivateMessage() == 0)
        {
            Log.Warn(Scope, "could not register the activation message; launching Themia again will not reach this copy");
        }

        RegisterHotkey();
        return true;
    }

    /// <summary>
    /// Claim Ctrl+Alt+W. NOREPEAT because a held key would otherwise toggle edit mode dozens of
    /// times a second.
    /// </summary>
    private void RegisterHotkey()
    {
        _hotkeyRegistered = Win32.RegisterHotKey(
            _hwnd,
            HotkeyId,
            Win32.ModControl | Win32.ModAlt | Win32.ModNoRepeat,
            Win32.VkW);

        if (_hotkeyRegistered)
        {
            Log.Info(Scope, "Ctrl+Alt+W toggles edit mode");
            return;
        }

        // Another program owns the combination. Double-clicking a widget and launching Themia again
        // both still work, so this is a degraded app rather than an unusable one.
        Log.Error(
            Scope,
            $"Ctrl+Alt+W is taken by another program (win32 {Marshal.GetLastWin32Error()}); " +
            "edit mode can still be reached by double-clicking a widget or launching Themia again");
    }

    [UnmanagedCallersOnly]
    private static nint StaticWndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        // A WndProc is called by Windows itself. Letting a managed exception unwind out of here is
        // undefined behaviour, so every path is wrapped and the message falls through to DefWindowProc.
        try
        {
            var self = _instance;
            if (self is not null && self._hwnd == hwnd)
            {
                if (msg == Win32.WmHotkey && (int)wParam == HotkeyId)
                {
                    self.EditModeToggleRequested?.Invoke();
                    return 0;
                }

                if (_activateMessage != 0 && msg == _activateMessage)
                {
                    self.EditModeToggleRequested?.Invoke();
                    return 0;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(Scope, $"the command window procedure threw handling message 0x{msg:X}", ex);
        }

        return Win32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_hotkeyRegistered)
        {
            // A hotkey outliving its window is a combination nothing can claim until the session
            // ends, including the copy the user starts next.
            if (!Win32.UnregisterHotKey(_hwnd, HotkeyId))
            {
                Log.Warn(Scope, $"UnregisterHotKey failed (win32 {Marshal.GetLastWin32Error()})");
            }

            _hotkeyRegistered = false;
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
