using System.Runtime.InteropServices;
using Themia.Core.Diagnostics;

namespace Themia.App.Interop;

/// <summary>Where the widget surface sits in the window z-order.</summary>
internal enum WidgetLayer
{
    /// <summary>On the desktop: below every ordinary window, never activated, never in the taskbar.</summary>
    Desktop = 0,

    /// <summary>An ordinary window that still never takes focus.</summary>
    Normal = 1,

    /// <summary>Above everything.</summary>
    Topmost = 2,
}

/// <summary>
/// Turns a plain WinUI window into a desktop widget surface: no taskbar entry, never steals focus,
/// parked in the requested z-band, and — critically — click-through everywhere except the widgets
/// themselves.
/// </summary>
internal static class DesktopLayer
{
    private const string Scope = "desktop-layer";

    /// <summary>
    /// Apply the window styles a widget surface needs.
    /// <para>
    /// <c>WS_EX_NOACTIVATE</c> is what makes this feel like part of the desktop rather than an app:
    /// clicking a widget neither focuses it nor raises it over whatever the user is working in.
    /// <c>WS_EX_TOOLWINDOW</c> keeps it out of the taskbar and Alt-Tab. In edit mode the surface has
    /// to accept keyboard input, so NOACTIVATE is lifted for the duration.
    /// </para>
    /// </summary>
    public static void Apply(nint hwnd, WidgetLayer layer, bool interactive)
    {
        if (hwnd == 0 || !Win32.IsWindow(hwnd))
        {
            return;
        }

        var ex = (uint)Win32.GetWindowLongPtr(hwnd, Win32.GwlExStyle);

        ex |= Win32.WsExToolWindow;
        ex &= ~Win32.WsExAppWindow;

        if (interactive)
        {
            ex &= ~Win32.WsExNoActivate;
        }
        else
        {
            ex |= Win32.WsExNoActivate;
        }

        // The surface is NEVER topmost, not even while editing. It is a desktop layer: widgets
        // belong under the user's windows, and editing them does not change that. Edit mode only
        // drops WS_EX_NOACTIVATE so the surface can take keyboard focus for the shortcuts — and
        // because an activatable window gets raised when clicked, BottomPin rewrites every
        // z-order change back to the bottom.
        if (layer == WidgetLayer.Topmost)
        {
            ex |= Win32.WsExTopmost;
        }
        else
        {
            ex &= ~Win32.WsExTopmost;
        }

        if (Win32.SetWindowLongPtr(hwnd, Win32.GwlExStyle, (nint)ex) == 0 && Marshal.GetLastWin32Error() != 0)
        {
            Log.Warn(Scope, $"SetWindowLongPtr(GWL_EXSTYLE) failed (win32 {Marshal.GetLastWin32Error()})");
        }

        Reassert(hwnd, layer, interactive);
    }

    /// <summary>
    /// Park the window in its z-band. Cheap enough to re-run whenever something might have
    /// reshuffled the z-order (a display change, a return from edit mode).
    /// </summary>
    public static void Reassert(nint hwnd, WidgetLayer layer, bool interactive)
    {
        if (hwnd == 0 || !Win32.IsWindow(hwnd))
        {
            return;
        }

        var insertAfter = (layer, interactive) switch
        {
            (WidgetLayer.Topmost, _) => Win32.HwndTopmost,
            (WidgetLayer.Desktop, _) => Win32.HwndBottom,
            _ => Win32.HwndNoTopmost,
        };

        // Deliberately no SWP_FRAMECHANGED: it forces a non-client area recalculation, and that is
        // what made Windows hand the borderless surface its title bar and border back the moment
        // edit mode toggled. Only the z-order and activation flags change here, so the frame does
        // not need recomputing.
        const uint Flags = Win32.SwpNoMove | Win32.SwpNoSize | Win32.SwpNoActivate | Win32.SwpNoOwnerZOrder;

        if (!Win32.SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0, Flags))
        {
            Log.Warn(Scope, $"SetWindowPos failed (win32 {Marshal.GetLastWin32Error()})");
        }
    }

    /// <summary>
    /// Restrict the window to the union of the given rects, so clicks anywhere else land on the
    /// desktop underneath instead of on an invisible full-screen pane.
    /// <para>
    /// Rects are in <b>physical</b> pixels relative to the window's top-left — the <i>window</i>, not
    /// the client area. Those differ by the frame, so a caller working in client space has to add the
    /// client origin first (see <c>DesktopWindow.ClientToWindowOffset</c>) or the whole mask sits
    /// shifted from the widgets it is supposed to trace. Passing an empty list hides the surface
    /// entirely (an empty view really should not swallow the desktop).
    /// </para>
    /// </summary>
    public static void SetHitRegion(nint hwnd, IReadOnlyList<Win32.Rect> rects)
    {
        if (hwnd == 0 || !Win32.IsWindow(hwnd))
        {
            return;
        }

        // Built as a union, then handed to the window. SetWindowRgn takes ownership on success, so
        // the handle is only ours to free while we still hold it, and on every failure path.
        var combined = Win32.CreateRectRgn(0, 0, 0, 0);
        if (combined == 0)
        {
            Log.Error(Scope, "CreateRectRgn failed; leaving the previous hit region in place");
            return;
        }

        try
        {
            foreach (var r in rects)
            {
                if (r.Width <= 0 || r.Height <= 0)
                {
                    continue;
                }

                var piece = Win32.CreateRectRgn(r.Left, r.Top, r.Right, r.Bottom);
                if (piece == 0)
                {
                    Log.Warn(Scope, "CreateRectRgn failed for a widget rect; it will not be clickable");
                    continue;
                }

                try
                {
                    Win32.CombineRgn(combined, combined, piece, Win32.RgnOr);
                }
                finally
                {
                    Win32.DeleteObject(piece);
                }
            }

            if (Win32.SetWindowRgn(hwnd, combined, redraw: true) == 0)
            {
                Log.Warn(Scope, $"SetWindowRgn failed (win32 {Marshal.GetLastWin32Error()})");
                Win32.DeleteObject(combined);
            }

            // Success: the window owns `combined` now. Deleting it here would free a live region.
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "building the hit region threw", ex);
            Win32.DeleteObject(combined);
        }
    }

    /// <summary>
    /// Strip the window frame at the Win32 level.
    /// <para>
    /// <c>OverlappedPresenter.SetBorderAndTitleBar(false, false)</c> does <b>not</b> clear
    /// <c>WS_CAPTION</c> here: the surface kept a caption and a 3px border the whole time, which the
    /// click-through region happened to clip away. The moment edit mode dropped the region the title
    /// bar became visible, and the border meant client coordinates were offset from window
    /// coordinates, so the mask never quite lined up with the widgets either.
    /// </para>
    /// <para>
    /// Re-applied whenever the styles are touched, because toggling activation makes the presenter
    /// reassert its own frame.
    /// </para>
    /// </summary>
    public static void MakeChromeless(nint hwnd)
    {
        if (hwnd == 0 || !Win32.IsWindow(hwnd))
        {
            return;
        }

        var style = (uint)Win32.GetWindowLongPtr(hwnd, Win32.GwlStyle);
        var wanted = (style & ~(Win32.WsCaption | Win32.WsThickFrame | Win32.WsSysMenu | Win32.WsMinimizeBox | Win32.WsMaximizeBox))
            | Win32.WsPopup
            | Win32.WsClipChildren;

        if (style == wanted)
        {
            return;
        }

        Win32.SetWindowLongPtr(hwnd, Win32.GwlStyle, (nint)wanted);

        // SWP_FRAMECHANGED is required here, and only here: the frame genuinely changed, so the
        // non-client area has to be recomputed for the new style to take effect.
        Win32.SetWindowPos(
            hwnd,
            0,
            0, 0, 0, 0,
            Win32.SwpNoMove | Win32.SwpNoSize | Win32.SwpNoZOrder | Win32.SwpNoActivate | Win32.SwpFrameChanged);
    }

    /// <summary>Drop the mask so the whole window is hittable again (edit mode).</summary>
    public static void ClearHitRegion(nint hwnd)
    {
        if (hwnd == 0 || !Win32.IsWindow(hwnd))
        {
            return;
        }

        Win32.SetWindowRgn(hwnd, 0, redraw: true);
    }
}
