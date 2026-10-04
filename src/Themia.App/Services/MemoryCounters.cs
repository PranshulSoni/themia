using System.Runtime.InteropServices;
using Themia.App.Interop;
using Themia.Core.Widgets;

namespace Themia.App.Services;

/// <summary>
/// [S-1130] The memory meter's data: <c>GlobalMemoryStatusEx</c> for the totals and the commit
/// charge, and — only while a widget wants the detail — the page-file, standby and modified
/// counters through one shared <see cref="PdhQuery"/>.
/// <para>
/// The query is opened by the first memory widget that asks for detail and closed when the last one
/// detaches (<see cref="Acquire"/>), so a desktop without a memory widget holds no counters. Every
/// read happens on the UI thread's tick, so the state below needs no lock; widgets reading in the
/// same tick share one snapshot (<see cref="SampleCache{T}"/>).
/// </para>
/// </summary>
internal static class MemoryCounters
{
    private const string Scope = "memory";

    private static readonly SampleCache<MemorySnapshot> Cache = new();

    private static int _leases;
    private static PdhQuery? _query;
    private static bool _queryFailed;
    private static PdhCounter? _pagefile;
    private static PdhCounter? _standbyCore;
    private static PdhCounter? _standbyNormal;
    private static PdhCounter? _standbyReserve;
    private static PdhCounter? _modified;

    /// <summary>Hold the detail counters open; dispose to let them go.</summary>
    public static IDisposable Acquire()
    {
        _leases++;
        return new Lease();
    }

    /// <summary>One snapshot, or null when Windows would not say how much memory there is.</summary>
    /// <param name="detailed">Read the page-file and cache counters too (they cost a PDH collection).</param>
    public static MemorySnapshot? Read(bool detailed)
    {
        var now = Environment.TickCount64;

        // Opened before the cache is consulted: when the first widget in a tick did not want the
        // detail, the next one that does must not be handed that bare snapshot for ever.
        var query = detailed ? Open() : null;

        if (Cache.TryReuse(now, out var recent) && (query is null || recent.Standby is not null || recent.PagefileUsage is not null))
        {
            return recent;
        }

        var status = new Win32.MemoryStatusEx { Length = (uint)Marshal.SizeOf<Win32.MemoryStatusEx>() };

        if (!Win32.GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0)
        {
            return null;
        }

        // TotalPageFile is the commit limit (memory plus every page file), and what is left of it is
        // AvailPageFile; their difference is the commit charge.
        var commitLimit = status.TotalPageFile;
        var commitTotal = status.TotalPageFile > status.AvailPageFile ? status.TotalPageFile - status.AvailPageFile : 0;

        double? pagefile = null;
        ulong? standby = null;
        ulong? modified = null;

        if (query is not null)
        {
            query.Collect();
            pagefile = _pagefile is { } p && query.Double(p) is { } percent ? percent / 100 : null;
            standby = Sum(query, _standbyCore, _standbyNormal, _standbyReserve);
            modified = _modified is { } m && query.Large(m) is long bytes && bytes >= 0 ? (ulong)bytes : null;
        }

        var snapshot = new MemorySnapshot(status.TotalPhys, status.AvailPhys, commitTotal, commitLimit, standby, modified, pagefile);
        Cache.Store(now, snapshot);
        return snapshot;
    }

    /// <summary>The three standby lists together; null unless all three answered (a partial sum would understate the cache).</summary>
    private static ulong? Sum(PdhQuery query, params PdhCounter?[] counters)
    {
        ulong total = 0;

        foreach (var counter in counters)
        {
            if (counter is not { } c || query.Large(c) is not { } bytes || bytes < 0)
            {
                return null;
            }

            total += (ulong)bytes;
        }

        return total;
    }

    private static PdhQuery? Open()
    {
        if (_query is not null || _queryFailed || _leases == 0)
        {
            return _query;
        }

        _query = PdhQuery.Open(Scope);

        if (_query is null)
        {
            // Logged by Open. Not retried every tick: the detail lines simply stay hidden.
            _queryFailed = true;
            return null;
        }

        // Each is optional: a machine without a page file has no Paging File instance, and the
        // lines that need a missing counter are left out rather than guessed.
        _pagefile = _query.Add(@"\Paging File(_Total)\% Usage");
        _standbyCore = _query.Add(@"\Memory\Standby Cache Core Bytes");
        _standbyNormal = _query.Add(@"\Memory\Standby Cache Normal Priority Bytes");
        _standbyReserve = _query.Add(@"\Memory\Standby Cache Reserve Bytes");
        _modified = _query.Add(@"\Memory\Modified Page List Bytes");
        return _query;
    }

    private static void Release()
    {
        _leases = Math.Max(0, _leases - 1);

        if (_leases > 0)
        {
            return;
        }

        _query?.Dispose();
        _query = null;
        _queryFailed = false;
        _pagefile = _standbyCore = _standbyNormal = _standbyReserve = _modified = null;
        Cache.Clear();
    }

    /// <summary>One widget's hold on the counters. Disposing twice releases once.</summary>
    private sealed class Lease : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            Release();
        }
    }
}
