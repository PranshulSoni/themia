using System.Runtime.InteropServices;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;

namespace Themia.App.Services;

/// <summary>
/// The processor meter's extras: the load of each logical processor and the clock it is running at.
/// <para>
/// Per-core load is <c>NtQuerySystemInformation(SystemProcessorPerformanceInformation)</c>: each
/// processor's cumulative idle / kernel / user times, the per-processor version of the totals
/// <c>GetSystemTimes</c> gives the reading, differenced between two samples like it. One call for all
/// processors, no handles, no counters to hold open. It reports the calling thread's processor group
/// (up to 64 logical processors); a machine with more shows the first group's.
/// </para>
/// <para>
/// The clock is Task Manager's "Speed": the base clock (<c>CallNtPowerInformation</c>'s
/// <c>MaxMhz</c>) scaled by the PDH counter <c>% Processor Performance</c>, which counts turbo — the
/// power manager's own <c>CurrentMhz</c> mostly just repeats the base clock, so it is only the
/// answer when the counter is not there. The counter is held only while a widget leases it.
/// </para>
/// <para>Single-threaded: every read happens on the UI thread's tick.</para>
/// </summary>
internal static unsafe partial class ProcessorStatus
{
    private const string Scope = "cpu";

    /// <summary><c>SystemProcessorPerformanceInformation</c>.</summary>
    private const int ProcessorPerformanceClass = 8;

    /// <summary><c>POWER_INFORMATION_LEVEL.ProcessorInformation</c>.</summary>
    private const int ProcessorInformationLevel = 11;

    /// <summary>One processor group's worth: the most the performance class reports.</summary>
    private const int MaxProcessors = 64;

    private static readonly SampleCache<CoreSample> CoreCache = new();
    private static readonly SampleCache<double> ClockCache = new();

    private static CoreTimes[] _previous = [];
    private static string? _coreFault;

    private static int _clockLeases;
    private static PdhQuery? _clockQuery;
    private static PdhCounter? _performance;
    private static bool _clockQueryFailed;
    private static (double Base, double Reported)? _power;

    /// <summary>
    /// [S-1142] Each logical processor's load since the last read, or null on the first read (no
    /// baseline yet) and when Windows would not say. Widgets reading within the same tick share it.
    /// </summary>
    public static double[]? CoreLoads()
    {
        var now = Environment.TickCount64;

        if (CoreCache.TryReuse(now, out var recent))
        {
            return recent.Loads;
        }

        var loads = ReadCores();
        CoreCache.Store(now, new CoreSample(loads));
        return loads;
    }

    private static double[]? ReadCores()
    {
        var count = Math.Clamp(Environment.ProcessorCount, 1, MaxProcessors);
        var buffer = stackalloc PerformanceInformation[count];
        var status = NtQuerySystemInformation(ProcessorPerformanceClass, buffer, (uint)(count * sizeof(PerformanceInformation)), out var written);

        if (status != 0)
        {
            NoteCoreFault($"NtQuerySystemInformation(processor performance) returned 0x{status:X8}");
            return null;
        }

        NoteCoreFault(null);

        var reported = (int)Math.Min(count, written / (uint)sizeof(PerformanceInformation));
        var now = new CoreTimes[reported];

        for (var i = 0; i < reported; i++)
        {
            now[i] = new CoreTimes(buffer[i].IdleTime, buffer[i].KernelTime, buffer[i].UserTime);
        }

        var loads = CpuRules.Loads(_previous, now);
        _previous = now;
        return loads;
    }

    /// <summary>One log line per change of state, not one per tick on a machine where it never works.</summary>
    private static void NoteCoreFault(string? fault)
    {
        if (fault == _coreFault)
        {
            return;
        }

        _coreFault = fault;

        if (fault is not null)
        {
            Log.Warn(Scope, $"{fault}; the per-core bars stay hidden");
        }
    }

    /// <summary>Hold the clock counter open; dispose to let it go.</summary>
    public static IDisposable AcquireClock()
    {
        _clockLeases++;
        return new Lease();
    }

    /// <summary>[S-1141] The clock the processors are running at, in MHz; null when nothing says.</summary>
    public static double? Megahertz()
    {
        var now = Environment.TickCount64;

        if (ClockCache.TryReuse(now, out var recent))
        {
            return recent > 0 ? recent : null;
        }

        double? performance = null;

        if (OpenClock() is { } query && _performance is { } counter)
        {
            query.Collect();
            performance = query.Double(counter);
        }

        _power ??= ReadPower();
        var mhz = _power is { } power ? CpuRules.EffectiveMhz(performance, power.Base, power.Reported) : null;

        // The reported clock is read once: it is the base clock on almost every machine and costs a
        // kernel call per processor. With the counter answering it is not used at all.
        ClockCache.Store(now, mhz ?? 0);
        return mhz;
    }

    /// <summary>The base and the reported clock, the highest over all processors; null when the power manager refuses.</summary>
    private static (double Base, double Reported)? ReadPower()
    {
        var count = Math.Max(1, Environment.ProcessorCount);
        var bytes = (uint)(count * sizeof(PowerInformation));
        var buffer = (PowerInformation*)NativeMemory.Alloc(bytes);

        try
        {
            var status = CallNtPowerInformation(ProcessorInformationLevel, null, 0, buffer, bytes);

            if (status != 0)
            {
                Log.Warn(Scope, $"CallNtPowerInformation(ProcessorInformation) returned 0x{status:X8}; no clock speed");
                return null;
            }

            uint max = 0;
            uint current = 0;

            for (var i = 0; i < count; i++)
            {
                max = Math.Max(max, buffer[i].MaxMhz);
                current = Math.Max(current, buffer[i].CurrentMhz);
            }

            return (max, current);
        }
        finally
        {
            NativeMemory.Free(buffer);
        }
    }

    private static PdhQuery? OpenClock()
    {
        if (_clockQuery is not null || _clockQueryFailed || _clockLeases == 0)
        {
            return _clockQuery;
        }

        _clockQuery = PdhQuery.Open(Scope);

        if (_clockQuery is null)
        {
            // Logged by Open. The reported clock stands in.
            _clockQueryFailed = true;
            return null;
        }

        _performance = _clockQuery.Add(@"\Processor Information(_Total)\% Processor Performance");
        return _clockQuery;
    }

    private static void ReleaseClock()
    {
        _clockLeases = Math.Max(0, _clockLeases - 1);

        if (_clockLeases > 0)
        {
            return;
        }

        _clockQuery?.Dispose();
        _clockQuery = null;
        _performance = null;
        _clockQueryFailed = false;
        ClockCache.Clear();
    }

    /// <summary>A struct wrapper so the per-core array can ride in a <see cref="SampleCache{T}"/>.</summary>
    private readonly record struct CoreSample(double[]? Loads);

    /// <summary>One widget's hold on the clock counter. Disposing twice releases once.</summary>
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
            ReleaseClock();
        }
    }

    // LibraryImport: the stubs are generated at build time, so they survive Native AOT.
    [LibraryImport("ntdll")]
    private static partial int NtQuerySystemInformation(int informationClass, void* information, uint length, out uint returnLength);

    [LibraryImport("powrprof")]
    private static partial int CallNtPowerInformation(int level, void* input, uint inputLength, void* output, uint outputLength);

    /// <summary><c>SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION</c>: five 64-bit times and a count, 48 bytes.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    private readonly struct PerformanceInformation
    {
        [FieldOffset(0)]
        public readonly long IdleTime;

        /// <summary>Includes <see cref="IdleTime"/>.</summary>
        [FieldOffset(8)]
        public readonly long KernelTime;

        [FieldOffset(16)]
        public readonly long UserTime;
    }

    /// <summary><c>PROCESSOR_POWER_INFORMATION</c>: six ULONGs.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private readonly struct PowerInformation
    {
        [FieldOffset(4)]
        public readonly uint MaxMhz;

        [FieldOffset(8)]
        public readonly uint CurrentMhz;
    }
}
