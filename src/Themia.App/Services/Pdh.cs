using System.Runtime.InteropServices;
using Themia.Core.Diagnostics;

namespace Themia.App.Services;

/// <summary>A counter added to a <see cref="PdhQuery"/>. Owned by the query: it goes when the query closes.</summary>
internal readonly record struct PdhCounter(nint Handle);

/// <summary>
/// [S-1125] One PDH query: a set of performance counters collected together.
/// <para>
/// PDH is the plain C API under Task Manager's and perfmon's charts, so <c>LibraryImport</c> binds it
/// with no runtime marshalling and it survives Native AOT — unlike <c>PerformanceCounter</c>, which
/// drags in the perf-counter registry stack and is not trim-safe, or WMI, which AOT cannot reach.
/// </para>
/// <para>
/// RAII: the query handle is a <see cref="SafeHandle"/>, closed exactly once on <see cref="Dispose"/>
/// or, if an owner forgets, by the finalizer. Counter handles belong to the query and close with it.
/// Counter paths are added by their English names (<c>PdhAddEnglishCounter</c>), so a German or
/// Japanese Windows finds the same counters. Not thread-safe: one owner samples it at a time.
/// </para>
/// </summary>
internal sealed partial class PdhQuery : IDisposable
{
    private const uint FormatDouble = 0x00000200;
    private const uint FormatLarge = 0x00000400;
    private const uint MoreData = 0x800007D2;

    /// <summary>PDH_CSTATUS_VALID_DATA and PDH_CSTATUS_NEW_DATA; anything above is a bad reading.</summary>
    private const uint LastGoodStatus = 1;

    private readonly QueryHandle _handle;
    private readonly string _scope;
    private bool _collected;

    private PdhQuery(QueryHandle handle, string scope)
    {
        _handle = handle;
        _scope = scope;
    }

    /// <summary>Open an empty query; null (and one log line) when PDH refuses.</summary>
    /// <param name="scope">The log scope of the owner, so a failure is filed with the widget it affects.</param>
    public static PdhQuery? Open(string scope)
    {
        var status = PdhOpenQuery(0, 0, out var handle);

        if (status != 0 || handle.IsInvalid)
        {
            handle.Dispose();
            Log.Warn(scope, $"could not open a performance counter query: 0x{status:X8}");
            return null;
        }

        return new PdhQuery(handle, scope);
    }

    /// <summary>
    /// Add a counter by its English path, wildcards allowed (<c>\GPU Engine(*)\Utilization Percentage</c>).
    /// Null when this machine has no such counter — an absent counter is not an error, so it is not logged.
    /// </summary>
    public PdhCounter? Add(string englishPath)
    {
        if (_handle.IsClosed)
        {
            return null;
        }

        return PdhAddEnglishCounter(_handle, englishPath, 0, out var counter) == 0 && counter != 0
            ? new PdhCounter(counter)
            : null;
    }

    /// <summary>
    /// Take one reading of every counter in the query. A rate counter needs two, so the first
    /// collection after opening reports "no data yet" for those; that one is not worth a log line.
    /// </summary>
    public void Collect()
    {
        if (_handle.IsClosed)
        {
            return;
        }

        var status = PdhCollectQueryData(_handle);

        if (status != 0 && _collected)
        {
            Log.Debug(_scope, $"collecting performance counters returned 0x{status:X8}");
        }

        _collected = true;
    }

    /// <summary>A single-instance counter's value as of the last <see cref="Collect"/>; null when it has no good reading.</summary>
    public double? Double(PdhCounter counter)
    {
        if (_handle.IsClosed)
        {
            return null;
        }

        return PdhGetFormattedCounterValue(counter.Handle, FormatDouble, out _, out var value) == 0
            && value.Status <= LastGoodStatus
            && double.IsFinite(value.Value.Double)
                ? value.Value.Double
                : null;
    }

    /// <summary>A single-instance counter as a 64-bit integer (byte counts); null when it has no good reading.</summary>
    public long? Large(PdhCounter counter)
    {
        if (_handle.IsClosed)
        {
            return null;
        }

        return PdhGetFormattedCounterValue(counter.Handle, FormatLarge, out _, out var value) == 0
            && value.Status <= LastGoodStatus
                ? value.Value.Large
                : null;
    }

    /// <summary>Every instance of a wildcard counter, as doubles. Bad readings are left out.</summary>
    public List<(string Instance, double Value)> Doubles(PdhCounter counter)
    {
        var result = new List<(string, double)>();
        ReadArray(counter, FormatDouble, (name, value) => result.Add((name, value.Double)));
        return result;
    }

    /// <summary>Every instance of a wildcard counter, as 64-bit integers. Bad readings are left out.</summary>
    public List<(string Instance, long Value)> Larges(PdhCounter counter)
    {
        var result = new List<(string, long)>();
        ReadArray(counter, FormatLarge, (name, value) => result.Add((name, value.Large)));
        return result;
    }

    private unsafe void ReadArray(PdhCounter counter, uint format, Action<string, Value> each)
    {
        if (_handle.IsClosed)
        {
            return;
        }

        // The instance list can grow between asking for the size and reading — a process starts
        // a GPU context, a disk arrives — so the size is asked again a few times rather than once.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            uint size = 0;
            var sized = PdhGetFormattedCounterArray(counter.Handle, format, ref size, out var count, 0);

            if (sized != MoreData || size == 0 || count == 0)
            {
                return;
            }

            var buffer = NativeMemory.Alloc(size);
            try
            {
                var read = PdhGetFormattedCounterArray(counter.Handle, format, ref size, out count, (nint)buffer);

                if (read == MoreData)
                {
                    continue;
                }

                if (read != 0)
                {
                    return;
                }

                var items = (Item*)buffer;
                for (var i = 0; i < count; i++)
                {
                    if (items[i].Status <= LastGoodStatus && items[i].Name is not null)
                    {
                        each(new string(items[i].Name), items[i].Value);
                    }
                }

                return;
            }
            finally
            {
                NativeMemory.Free(buffer);
            }
        }
    }

    public void Dispose() => _handle.Dispose();

    /// <summary>The query handle. Closing it also frees every counter added to it.</summary>
    private sealed class QueryHandle : SafeHandle
    {
        public QueryHandle()
            : base(0, ownsHandle: true)
        {
        }

        public override bool IsInvalid => handle == 0;

        protected override bool ReleaseHandle() => PdhCloseQuery(handle) == 0;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct Value
    {
        [FieldOffset(0)]
        public double Double;

        [FieldOffset(0)]
        public long Large;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct Item
    {
        public char* Name;
        public uint Status;
        public Value Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FormattedValue
    {
        public uint Status;
        public Value Value;
    }

    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW")]
    private static partial uint PdhOpenQuery(nint dataSource, nint userData, out QueryHandle query);

    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PdhAddEnglishCounter(QueryHandle query, string path, nint userData, out nint counter);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhCollectQueryData(QueryHandle query);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out FormattedValue value);

    [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    private static partial uint PdhGetFormattedCounterArray(nint counter, uint format, ref uint bufferSize, out uint itemCount, nint buffer);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhCloseQuery(nint query);
}
