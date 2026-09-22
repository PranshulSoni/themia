using System.Runtime.InteropServices;
using Themia.Core.Diagnostics;

namespace Themia.App.Interop;

/// <summary>One display.</summary>
/// <param name="DeviceName">Stable id used to remember which view a monitor shows (e.g. <c>\\.\DISPLAY1</c>).</param>
/// <param name="WorkArea">Work area in <b>physical</b> pixels — the space the cursor is reported in.</param>
/// <param name="IsPrimary">Whether this is the primary display.</param>
internal readonly record struct MonitorTarget(string DeviceName, Win32.Rect WorkArea, bool IsPrimary);

/// <summary>
/// Enumerates displays through <c>EnumDisplayMonitors</c>.
/// <para>
/// Not <see cref="Microsoft.UI.Windowing.DisplayArea"/>: <c>DisplayArea.FindAll()</c> throws an
/// <see cref="InvalidCastException"/> out of the CsWinRT projection here, so the one API that would
/// hand back work areas already in AppWindow coordinates is unusable. The window therefore converts
/// these physical rects itself (see <c>DesktopWindow.ApplyPlacement</c>).
/// </para>
/// </summary>
internal static unsafe class Monitors
{
    private const string Scope = "monitors";
    private const uint MonitorPrimary = 0x00000001;

    private static readonly Lock Gate = new();
    private static List<MonitorTarget>? _collecting;

    /// <summary>
    /// All displays, primary first. Returns an empty list rather than throwing: a transient failure
    /// during a display change must not take the process down.
    /// </summary>
    public static List<MonitorTarget> All()
    {
        lock (Gate)
        {
            _collecting = [];
            try
            {
                var callback = (nint)(delegate* unmanaged<nint, nint, Win32.Rect*, nint, int>)&EnumCallback;
                if (!Win32.EnumDisplayMonitors(0, 0, callback, 0))
                {
                    Log.Warn(Scope, $"EnumDisplayMonitors failed (win32 {Marshal.GetLastWin32Error()})");
                }

                var found = _collecting;
                found.Sort(static (a, b) => b.IsPrimary.CompareTo(a.IsPrimary));
                return found;
            }
            finally
            {
                _collecting = null;
            }
        }
    }

    [UnmanagedCallersOnly]
    private static int EnumCallback(nint monitor, nint hdc, Win32.Rect* clip, nint data)
    {
        // A Win32 callback must never let a managed exception unwind into native code: that is
        // undefined behaviour, and it would happen inside the display driver's enumeration.
        try
        {
            var info = new Win32.MonitorInfoEx { Size = sizeof(Win32.MonitorInfoEx) };

            if (!Win32.GetMonitorInfo(monitor, ref info))
            {
                return 1; // skip this one, keep enumerating
            }

            var name = info.DeviceName;
            _collecting?.Add(new MonitorTarget(
                string.IsNullOrEmpty(name) ? $"display-{monitor}" : name,
                info.Work,
                (info.Flags & MonitorPrimary) != 0));
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "monitor enumeration callback threw", ex);
        }

        return 1;
    }
}
