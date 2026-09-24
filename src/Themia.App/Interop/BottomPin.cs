using System.Runtime.InteropServices;
using Themia.Core.Diagnostics;

namespace Themia.App.Interop;

/// <summary>
/// Keeps a window pinned to the bottom of the z-order, permanently.
/// <para>
/// A widget surface belongs on the desktop, under everything else — including while it is being
/// edited. Calling <c>SetWindowPos(HWND_BOTTOM)</c> once is not enough: edit mode has to drop
/// <c>WS_EX_NOACTIVATE</c> so the surface can take keyboard focus for the copy/cut/paste shortcuts,
/// and an activatable window gets raised above its neighbours the moment it is clicked.
/// </para>
/// <para>
/// So the window procedure is subclassed and every <c>WM_WINDOWPOSCHANGING</c> is rewritten to say
/// "bottom". That covers the raise-on-activate path and anything else that reorders the window,
/// without polling for it — the alternative, re-asserting the z-order on a timer, is both a race and
/// a wakeup the app does not otherwise need.
/// </para>
/// </summary>
internal static unsafe class BottomPin
{
    private const string Scope = "bottom-pin";
    private const uint WmWindowPosChanging = 0x0046;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public nint Hwnd;
        public nint HwndInsertAfter;
        public int X;
        public int Y;
        public int Cx;
        public int Cy;
        public uint Flags;
    }

    private static readonly Lock Gate = new();
    private static readonly Dictionary<nint, nint> OriginalProcs = [];
    private static bool _pinning = true;

    /// <summary>
    /// Stop forcing the bottom, without unsubclassing.
    /// <para>
    /// Used while the desktop is fronted: the surface is deliberately lifted then, and a pin that
    /// kept rewriting the z-order would immediately undo it.
    /// </para>
    /// </summary>
    public static void SetPinning(bool pinning)
    {
        lock (Gate)
        {
            _pinning = pinning;
        }
    }

    /// <summary>Start forcing <paramref name="hwnd"/> to the bottom. Idempotent.</summary>
    public static void Attach(nint hwnd)
    {
        if (hwnd == 0 || !Win32.IsWindow(hwnd))
        {
            return;
        }

        lock (Gate)
        {
            if (OriginalProcs.ContainsKey(hwnd))
            {
                return;
            }

            var replacement = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&SubclassProc;
            var original = Win32.SetWindowLongPtr(hwnd, Win32.GwlpWndProc, replacement);
            if (original == 0)
            {
                Log.Warn(Scope, $"could not subclass the widget surface (win32 {Marshal.GetLastWin32Error()}); it may float above other windows");
                return;
            }

            OriginalProcs[hwnd] = original;
        }
    }

    /// <summary>Stop forcing the z-order and restore the original window procedure.</summary>
    public static void Detach(nint hwnd)
    {
        lock (Gate)
        {
            if (!OriginalProcs.Remove(hwnd, out var original))
            {
                return;
            }

            if (Win32.IsWindow(hwnd))
            {
                Win32.SetWindowLongPtr(hwnd, Win32.GwlpWndProc, original);
            }
        }
    }

    [UnmanagedCallersOnly]
    private static nint SubclassProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        nint original;

        // A window procedure runs on Windows' own call stack: a managed exception unwinding out of
        // here is undefined behaviour, so every path is guarded and falls through to the original.
        try
        {
            lock (Gate)
            {
                if (!OriginalProcs.TryGetValue(hwnd, out original))
                {
                    return Win32.DefWindowProc(hwnd, msg, wParam, lParam);
                }
            }

            bool pinning;
            lock (Gate)
            {
                pinning = _pinning;
            }

            if (pinning && msg == WmWindowPosChanging && lParam != 0)
            {
                var position = (WindowPos*)lParam;

                // Rewrite whatever z-order was requested into "bottom", and clear SWP_NOZORDER so
                // the value is actually honoured rather than ignored.
                position->HwndInsertAfter = Win32.HwndBottom;
                position->Flags &= ~Win32.SwpNoZOrder;
            }
        }
        catch (Exception ex)
        {
            Log.Error(Scope, $"window procedure threw handling message 0x{msg:X}", ex);
            return Win32.DefWindowProc(hwnd, msg, wParam, lParam);
        }

        return Win32.CallWindowProc(original, hwnd, msg, wParam, lParam);
    }
}
