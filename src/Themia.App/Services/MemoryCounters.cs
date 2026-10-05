using System.Runtime.InteropServices;
using Themia.App.Interop;
using Themia.Core.Widgets;

namespace Themia.App.Services;

/// <summary>
/// [S-1130] The machine's memory, from <c>GlobalMemoryStatusEx</c>. Every read happens on the UI
/// thread's tick, so widgets reading in the same tick share one snapshot (<see cref="SampleCache{T}"/>).
/// </summary>
internal static class MemoryCounters
{
    private static readonly SampleCache<MemorySnapshot> Cache = new();

    /// <summary>One snapshot, or null when Windows would not say how much memory there is.</summary>
    public static MemorySnapshot? Read()
    {
        var now = Environment.TickCount64;

        if (Cache.TryReuse(now, out var recent))
        {
            return recent;
        }

        var status = new Win32.MemoryStatusEx { Length = (uint)Marshal.SizeOf<Win32.MemoryStatusEx>() };

        if (!Win32.GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0)
        {
            return null;
        }

        var snapshot = new MemorySnapshot(status.TotalPhys, status.AvailPhys);
        Cache.Store(now, snapshot);
        return snapshot;
    }
}
