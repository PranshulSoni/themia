using System.Runtime.InteropServices;
using Themia.Core.Diagnostics;

namespace Themia.App.Interop;

/// <summary>One row in the tray menu. <see cref="Id"/> 0 means a separator.</summary>
internal readonly record struct TrayMenuItem(uint Id, string Label, bool Checked = false, bool Enabled = true)
{
    public static TrayMenuItem Separator => new(0, string.Empty);

    public bool IsSeparator => Id == 0;
}

/// <summary>
/// Notification-area icon backed directly by <c>Shell_NotifyIcon</c> and a native popup menu.
/// <para>
/// Deliberately not a XAML window: the tray menu is the one piece of UI that is open for a few
/// hundred milliseconds a day, and paying for a XAML island, its composition target and its render
/// thread to draw six lines of text is exactly the kind of cost this port exists to avoid.
/// </para>
/// </summary>
internal sealed unsafe class TrayIcon : IDisposable
{
    private const string Scope = "tray";
    private const string ClassName = "ThemiaTrayHost";
    private const uint WsPopup = 0x80000000;
    private const uint IconId = 1;

    private static TrayIcon? _instance;
    private static ushort _atom;

    /// <summary>
    /// Broadcast by the shell when the notification area is recreated — which happens every time
    /// explorer.exe restarts or crashes. Without handling it the icon disappears for the rest of the
    /// session, and with it the only way in and out of edit mode.
    /// </summary>
    private static uint _taskbarCreatedMessage;

    private nint _hwnd;
    private nint _icon;
    private string _tooltip = "Themia";
    private bool _iconAdded;
    private bool _ownsIcon;
    private bool _disposed;

    /// <summary>Raised when the user picks a menu row. The argument is the row's <see cref="TrayMenuItem.Id"/>.</summary>
    public event Action<uint>? CommandInvoked;

    /// <summary>Raised on a double-click of the tray icon.</summary>
    public event Action? Activated;

    /// <summary>Supplies the menu rows at the moment the menu opens, so view lists are never stale.</summary>
    public Func<IReadOnlyList<TrayMenuItem>>? MenuProvider { get; set; }

    /// <summary>Create the host window and add the icon. Returns false (logged) rather than throwing.</summary>
    public bool Create(string tooltip, string? iconPath)
    {
        if (_instance is not null && !ReferenceEquals(_instance, this))
        {
            Log.Error(Scope, "a tray icon already exists; refusing to create a second");
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

        // A real (but never shown) window, not HWND_MESSAGE: TrackPopupMenuEx needs a window that
        // can be brought to the foreground, and a message-only window cannot be.
        _hwnd = Win32.CreateWindowEx(0, ClassName, "Themia", WsPopup, 0, 0, 0, 0, 0, 0, hInstance, 0);
        if (_hwnd == 0)
        {
            Log.Error(Scope, $"CreateWindowEx failed (win32 {Marshal.GetLastWin32Error()})");
            return false;
        }

        _taskbarCreatedMessage = Win32.RegisterWindowMessage("TaskbarCreated");
        if (_taskbarCreatedMessage == 0)
        {
            Log.Warn(Scope, "could not register for TaskbarCreated; the icon will not return after an explorer restart");
        }

        _tooltip = tooltip;
        _icon = LoadIconOrDefault(iconPath, hInstance);

        return AddIcon();
    }

    private bool AddIcon()
    {
        var data = new Win32.NotifyIconData
        {
            Size = sizeof(Win32.NotifyIconData),
            Wnd = _hwnd,
            Id = IconId,
            Flags = Win32.NifMessage | Win32.NifIcon | Win32.NifTip,
            CallbackMessage = Win32.WmTrayCallback,
            Icon = _icon,
        };

        data.SetTip(_tooltip);

        if (!Win32.ShellNotifyIcon(Win32.NimAdd, ref data))
        {
            Log.Error(Scope, $"Shell_NotifyIcon(NIM_ADD) failed (win32 {Marshal.GetLastWin32Error()})");
            return false;
        }

        _iconAdded = true;
        return true;
    }

    private nint LoadIconOrDefault(string? iconPath, nint hInstance)
    {
        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
        {
            var loaded = Win32.LoadImage(0, iconPath, Win32.ImageIcon, 0, 0, Win32.LrLoadFromFile | Win32.LrDefaultSize);
            if (loaded != 0)
            {
                _ownsIcon = true;
                return loaded;
            }

            Log.Warn(Scope, $"could not load tray icon from {iconPath} (win32 {Marshal.GetLastWin32Error()}); using the default");
        }

        // IDI_APPLICATION. A shared system icon: not ours, so never destroyed.
        _ownsIcon = false;
        return Win32.LoadIcon(0, 32512);
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
                if (msg == Win32.WmTrayCallback)
                {
                    self.OnTrayCallback((uint)(lParam & 0xFFFF));
                    return 0;
                }

                if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
                {
                    self.Readd();
                    return 0;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(Scope, $"tray window procedure threw handling message 0x{msg:X}", ex);
        }

        return Win32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    /// <summary>Put the icon back after the shell recreated the notification area.</summary>
    private void Readd()
    {
        if (_disposed)
        {
            return;
        }

        _iconAdded = false;
        if (AddIcon())
        {
            Log.Info(Scope, "tray icon restored after the shell restarted");
        }
    }

    private void OnTrayCallback(uint mouseMessage)
    {
        switch (mouseMessage)
        {
            case Win32.WmRButtonUp:
                ShowMenu();
                break;

            case Win32.WmLButtonDblClk:
                Activated?.Invoke();
                break;

            default:
                break;
        }
    }

    private void ShowMenu()
    {
        var items = MenuProvider?.Invoke();
        if (items is null || items.Count == 0)
        {
            return;
        }

        if (!Win32.GetCursorPos(out var cursor))
        {
            Log.Warn(Scope, "GetCursorPos failed; cannot place the tray menu");
            return;
        }

        var menu = Win32.CreatePopupMenu();
        if (menu == 0)
        {
            Log.Error(Scope, $"CreatePopupMenu failed (win32 {Marshal.GetLastWin32Error()})");
            return;
        }

        try
        {
            foreach (var item in items)
            {
                if (item.IsSeparator)
                {
                    Win32.AppendMenu(menu, Win32.MfSeparator, 0, null);
                    continue;
                }

                var flags = Win32.MfString;
                if (item.Checked)
                {
                    flags |= Win32.MfChecked;
                }

                if (!item.Enabled)
                {
                    flags |= Win32.MfDisabled | Win32.MfGrayed;
                }

                Win32.AppendMenu(menu, flags, item.Id, item.Label);
            }

            // Required dance: without foreground ownership the menu does not dismiss when the user
            // clicks elsewhere, and is left stuck on screen.
            Win32.SetForegroundWindow(_hwnd);

            var chosen = Win32.TrackPopupMenuEx(
                menu,
                Win32.TpmRightButton | Win32.TpmReturnCmd | Win32.TpmRightAlign | Win32.TpmBottomAlign,
                cursor.X,
                cursor.Y,
                _hwnd,
                0);

            if (chosen > 0)
            {
                CommandInvoked?.Invoke((uint)chosen);
            }
        }
        finally
        {
            Win32.DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_iconAdded)
        {
            var data = new Win32.NotifyIconData
            {
                Size = sizeof(Win32.NotifyIconData),
                Wnd = _hwnd,
                Id = IconId,
            };

            if (!Win32.ShellNotifyIcon(Win32.NimDelete, ref data))
            {
                Log.Warn(Scope, "Shell_NotifyIcon(NIM_DELETE) failed; the icon may linger until hover");
            }

            _iconAdded = false;
        }

        if (_ownsIcon && _icon != 0)
        {
            Win32.DestroyIcon(_icon);
            _icon = 0;
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
