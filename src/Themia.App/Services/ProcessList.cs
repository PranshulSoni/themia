using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Themia.Core.Diagnostics;
using Themia.Core.Model;

namespace Themia.App.Services;

/// <summary>
/// One row of the process list.
/// <para>
/// <paramref name="CpuKnown"/> is not decoration. The fallback path in <see cref="ProcessList"/>
/// opens each process by handle, and Windows refuses the processor times of anything elevated,
/// protected, or running in another session. Folding that into <c>CpuFraction = 0</c> would make
/// "busy but unreadable" look exactly like "idle", so the caller is told which it is and shows
/// <c>--</c>. The primary path reads every process, so its rows are always known.
/// </para>
/// </summary>
/// <param name="Pid">Process id, unique only among processes alive right now.</param>
/// <param name="Name">Image name without the extension.</param>
/// <param name="CpuFraction">Share of the whole machine, 0..1, since the previous sample.</param>
/// <param name="WorkingSetBytes">Physical memory currently charged to the process.</param>
/// <param name="CpuKnown">False when the processor times could not be read at all.</param>
/// <param name="Threads">[S-1144] Threads the process is running.</param>
/// <param name="Handles">[S-1144] Kernel handles it holds open.</param>
public readonly record struct ProcessUsage(
    int Pid,
    string Name,
    double CpuFraction,
    long WorkingSetBytes,
    bool CpuKnown,
    int Threads = 0,
    int Handles = 0)
{
    /// <summary>The CPU column, or <c>--</c> where the times are not readable.</summary>
    public string CpuText => CpuKnown ? Themia.Core.Widgets.Formatting.Percent(CpuFraction) : "--";

    /// <summary>The memory column.</summary>
    public string MemoryText => Themia.Core.Widgets.Formatting.Bytes(WorkingSetBytes);
}

/// <summary>[S-1144] Every process in one sample, counted: the idle process (pid 0) is not one.</summary>
public readonly record struct ProcessTotals(int Processes, long Threads, long Handles);

/// <summary>
/// One whole pass over the process table: every row, unranked, and the totals. Shared between the
/// widgets that want it within <see cref="ProcessList.Recent"/>'s age, so two of them on one tick
/// cost one walk.
/// </summary>
public sealed class ProcessSnapshot(IReadOnlyList<ProcessUsage> rows, ProcessTotals totals)
{
    public static ProcessSnapshot Empty { get; } = new([], default);

    public IReadOnlyList<ProcessUsage> Rows { get; } = rows;

    public ProcessTotals Totals { get; } = totals;

    /// <summary>The <paramref name="count"/> heaviest on <paramref name="sort"/>, heaviest first, ties by name then pid.</summary>
    public IReadOnlyList<ProcessUsage> Top(int count, ProcessSort sort)
    {
        if (count <= 0 || Rows.Count == 0)
        {
            return [];
        }

        var ranked = Rows.ToArray();
        Array.Sort(ranked, ProcessList.Ranking(sort));
        return count >= ranked.Length ? ranked : ranked[..count];
    }
}

/// <summary>
/// The running processes, ranked — the data behind a Task Manager style list.
/// <para>
/// The readings come from one <c>NtQuerySystemInformation(SystemProcessInformation)</c> call: the
/// kernel writes every process's times and working set into a buffer this app already owns, so
/// there is no handle to open and therefore nothing to be denied. The documented alternatives all
/// fail the same way — <see cref="Process.TotalProcessorTime"/> needs
/// <c>PROCESS_QUERY_LIMITED_INFORMATION</c> on each process and is refused for everything elevated,
/// protected or in another session, which on an ordinary non-elevated desktop is about a third of
/// what is running and includes Defender, Search and the service hosts. A process widget whose list
/// structurally cannot contain the actual CPU hogs is worse than no widget, so that path is the
/// fallback and this is the primary.
/// </para>
/// <para>
/// <c>NtQuerySystemInformation</c> is undocumented-but-stable: it is what Task Manager, Process
/// Explorer and every process listing on Windows use, the information class and the 64-bit layout
/// have not changed since Vista, and a layout that did change would fail the bounds checks below
/// and fall back rather than misreport. WMI and <c>PerformanceCounter</c> are not options at all
/// here — neither survives trimming, and both are orders of magnitude slower than a widget sampling
/// once every two seconds can justify.
/// </para>
/// <para>
/// Single-threaded, exactly like <see cref="SystemMetrics"/>: every call happens on the UI thread's
/// tick, so the sample state below needs no lock.
/// </para>
/// </summary>
internal static unsafe partial class ProcessList
{
    private const string Scope = "processes";

    /// <summary>Snapshot of every process and thread. <c>SystemProcessInformation</c>.</summary>
    private const int SystemProcessInformationClass = 5;

    private const int StatusSuccess = 0;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    /// <summary>
    /// How many times to regrow the buffer before giving up for this sample.
    /// <para>
    /// The size the kernel reports is the size it needed a moment ago; a process starting between
    /// the two calls makes it too small again. That is a race, not a loop, so a handful of attempts
    /// is generous — but it has to be bounded, because a machine churning processes fast enough
    /// could otherwise keep this spinning inside a tick.
    /// </para>
    /// </summary>
    private const int MaxQueryAttempts = 6;

    /// <summary>
    /// Enough for a few hundred processes on the first call, so the common case never regrows.
    /// </summary>
    private const int InitialSnapshotBytes = 256 * 1024;

    /// <summary>
    /// Headroom added when regrowing, so the handful of processes that started while the kernel was
    /// answering do not cost another round trip.
    /// </summary>
    private const int SnapshotSlackBytes = 64 * 1024;

    /// <summary>
    /// Below this, a delta is not worth computing. Two process widgets on one tick both call
    /// <see cref="Top"/> microseconds apart; dividing a near-zero CPU delta by a near-zero interval
    /// produces noise, or a divide by zero. Such a call reuses the value from the last real
    /// interval and leaves the baseline untouched, so the next tick still measures the full span.
    /// </summary>
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(100);

    // Per-process CPU state. CPU load is a delta, so the previous reading has to be remembered for
    // every process individually. Keyed on pid, but see Tracked.StartTicks: a pid alone is not an
    // identity.
    private static readonly Dictionary<int, Tracked> Tracking = [];

    // Reused between samples so the twice-a-second pass does not allocate a fresh list each time.
    private static readonly List<ProcessUsage> Scratch = [];
    private static readonly List<int> Expired = [];

    // The snapshot buffer, kept between samples: at a few hundred kilobytes, reallocating it on
    // every tick would be the most expensive thing this widget does. Pinned, because the kernel
    // writes each process's name into it and hands back a pointer into the buffer — a pointer that
    // a moving collector would invalidate while the names are still being read.
    private static byte[] _snapshot = GC.AllocateUninitializedArray<byte>(InitialSnapshotBytes, pinned: true);

    // Bumped once per sample; anything still carrying an older number was not in this pass and is
    // therefore gone. Without this the dictionary accumulates an entry for every process that has
    // ever run, which over a week of uptime and a few thousand build steps is a slow leak.
    private static int _generation;

    // What the last log line about the snapshot path said, so a machine where it is permanently
    // unavailable costs one line rather than one per tick. Null means "working".
    private static string? _snapshotFault;

    private static readonly Comparison<ProcessUsage> ByCpu = static (a, b) =>
    {
        var cpu = b.CpuFraction.CompareTo(a.CpuFraction);
        return cpu != 0 ? cpu : Tiebreak(a, b);
    };

    private static readonly Comparison<ProcessUsage> ByMemory = static (a, b) =>
    {
        var memory = b.WorkingSetBytes.CompareTo(a.WorkingSetBytes);
        return memory != 0 ? memory : Tiebreak(a, b);
    };

    // The last whole pass, for Recent: the rows copied out of Scratch (which the next pass
    // rewrites), when they were taken, and whether the pass worked.
    private static ProcessSnapshot _recent = ProcessSnapshot.Empty;
    private static long? _recentAt;

    internal static Comparison<ProcessUsage> Ranking(ProcessSort sort) => sort == ProcessSort.Cpu ? ByCpu : ByMemory;

    /// <summary>
    /// [S-1145] The last pass if it is younger than <paramref name="maxAge"/>, else a new one. Every
    /// widget that lists processes reads through this, so a processor meter and a process list on
    /// the same desktop share one walk of the table per interval instead of each taking their own
    /// — and each other's CPU baselines — on every tick.
    /// </summary>
    public static ProcessSnapshot Recent(TimeSpan maxAge)
    {
        var now = Environment.TickCount64;

        if (_recentAt is { } at && now >= at && now - at < (long)maxAge.TotalMilliseconds)
        {
            return _recent;
        }

        if (!Sample())
        {
            _recentAt = now;
            _recent = ProcessSnapshot.Empty;
            return _recent;
        }

        return _recent;
    }

    /// <summary>
    /// The <paramref name="count"/> heaviest processes on <paramref name="sort"/>, heaviest first.
    /// <para>
    /// Also advances the CPU baseline, so this is the sampling call — not a free read of something
    /// measured elsewhere.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ProcessUsage> Top(int count, ProcessSort sort)
    {
        if (count <= 0)
        {
            return [];
        }

        if (!Sample())
        {
            return [];
        }

        Scratch.Sort(Ranking(sort));

        var taken = Math.Min(count, Scratch.Count);
        var rows = new ProcessUsage[taken];
        Scratch.CopyTo(0, rows, 0, taken);

        // A fresh array rather than a window onto Scratch: Scratch is rewritten by the next tick,
        // and a caller holding a view of it would watch its own list mutate underneath.
        return rows;
    }

    /// <summary>
    /// Fills <see cref="Scratch"/> with one row per process. False when the machine could not be
    /// asked at all, which is distinct from "asked, and nothing came back".
    /// </summary>
    private static bool Sample()
    {
        _generation++;
        Scratch.Clear();

        var now = Stopwatch.GetTimestamp();

        if (!SampleFromSnapshot(now) && !SampleFromHandles(now))
        {
            return false;
        }

        Prune();

        // [S-1144] Counted over the pass just taken; pid 0 never got a row.
        long threads = 0;
        long handles = 0;

        foreach (var row in Scratch)
        {
            threads += row.Threads;
            handles += row.Handles;
        }

        _recent = new ProcessSnapshot(Scratch.ToArray(), new ProcessTotals(Scratch.Count, threads, handles));
        _recentAt = Environment.TickCount64;
        return true;
    }

    // ---- primary path: one kernel snapshot ---------------------------------------------------

    /// <summary>
    /// Reads every process out of a single kernel snapshot. False when the call itself failed, in
    /// which case nothing has been written to <see cref="Scratch"/> and the fallback runs.
    /// </summary>
    private static bool SampleFromSnapshot(long now)
    {
        for (var attempt = 0; attempt < MaxQueryAttempts; attempt++)
        {
            int status;
            uint needed;

            fixed (byte* buffer = _snapshot)
            {
                status = NtQuerySystemInformation(
                    SystemProcessInformationClass,
                    buffer,
                    (uint)_snapshot.Length,
                    out needed);

                if (status == StatusSuccess)
                {
                    // Inside the pin, because the image names the kernel wrote point back into this
                    // same buffer and are copied out as the entries are walked.
                    Read(buffer, _snapshot.Length, now);
                    NoteSnapshotState(null);
                    return true;
                }
            }

            if (status != StatusInfoLengthMismatch)
            {
                NoteSnapshotState($"NtQuerySystemInformation returned 0x{status:X8}");
                return false;
            }

            Grow(needed);
        }

        NoteSnapshotState("the process snapshot kept outgrowing the buffer");
        return false;
    }

    /// <summary>Walks the entry chain, appending one row per process.</summary>
    private static void Read(byte* buffer, int length, long now)
    {
        var entrySize = Unsafe.SizeOf<SystemProcessInformation>();
        var threadSize = Unsafe.SizeOf<SystemThreadInformation>();
        var offset = 0;

        while (true)
        {
            if (length - offset < entrySize)
            {
                // Truncated: the rows already read are real, so they stay. Bailing out beats
                // reading past the end of the buffer.
                return;
            }

            ref readonly var entry = ref Unsafe.AsRef<SystemProcessInformation>(buffer + offset);

            // The variable-length thread array follows the fixed part. It is not read, but its size
            // is what says where this entry legitimately ends — an entry claiming more threads than
            // the buffer can hold is a layout the code does not understand, not data to parse.
            var span = entrySize + ((long)entry.NumberOfThreads * threadSize);

            if (span > length - offset)
            {
                return;
            }

            Observe(in entry, now);

            var next = entry.NextEntryOffset;

            if (next == 0)
            {
                return; // the documented terminator
            }

            // Entries run forward and never overlap. Anything else means the chain is not what this
            // code thinks it is, and following it would be reading arbitrary memory as a process.
            if (next < span || next > (uint)(length - offset))
            {
                return;
            }

            offset += (int)next;
        }
    }

    /// <summary>Turns one snapshot entry into a row.</summary>
    private static void Observe(ref readonly SystemProcessInformation entry, long now)
    {
        var pid = (int)entry.UniqueProcessId;

        // Pid 0 is the idle accounting bucket, not a process. Its "CPU" is the machine's idle time,
        // which would sit at the top of a list ranked by CPU and mean nothing the user can act on.
        if (pid == 0)
        {
            return;
        }

        var cpuTicks = entry.KernelTime + entry.UserTime;
        var workingSet = (long)entry.WorkingSetSize;

        // CreateTime is a FILETIME, and the fallback path converts its own start time to one too,
        // so a row keeps the same identity whichever path produced it and switching paths does not
        // look like a recycled pid.
        var startTicks = entry.CreateTime;
        var name = NameOf(in entry, pid, startTicks);

        Scratch.Add(new ProcessUsage(
            pid, name, Measure(pid, name, startTicks, cpuTicks, now), workingSet, true,
            (int)Math.Min(entry.NumberOfThreads, int.MaxValue), (int)Math.Min(entry.HandleCount, int.MaxValue)));
    }

    /// <summary>
    /// The image name, without its extension.
    /// <para>
    /// Materialised once per process, not once per sample. A process cannot change its image name,
    /// so a pid whose start time still matches is the same process wearing the same name — and
    /// allocating five hundred identical strings twice a second was, measurably, the most expensive
    /// thing this code did.
    /// </para>
    /// </summary>
    private static string NameOf(ref readonly SystemProcessInformation entry, int pid, long startTicks)
    {
        if (Tracking.TryGetValue(pid, out var tracked) && tracked.StartTicks == startTicks)
        {
            return tracked.Name;
        }

        var image = entry.ImageName;

        if (image.Buffer == 0 || image.Length == 0)
        {
            // The kernel leaves the name empty for a few entries. A pid is not a name, but it is
            // true, and it keeps the row's identity visible instead of showing a blank line.
            return $"pid {pid}";
        }

        // Length is in bytes, as UNICODE_STRING always is.
        var name = new string((char*)image.Buffer, 0, image.Length / sizeof(char));

        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
    }

    /// <summary>Makes the buffer big enough for what the kernel just asked for, plus headroom.</summary>
    private static void Grow(uint needed)
    {
        var target = (long)needed + SnapshotSlackBytes;

        // Doubling as well, so a machine that keeps growing converges instead of regrowing by the
        // same slack every attempt.
        target = Math.Max(target, _snapshot.Length * 2L);

        // The old array is garbage the moment this returns; there is no handle here to leak.
        _snapshot = GC.AllocateUninitializedArray<byte>((int)Math.Min(target, int.MaxValue), pinned: true);
    }

    /// <summary>
    /// Logs a change in whether the snapshot path works, and nothing on the ticks in between.
    /// </summary>
    private static void NoteSnapshotState(string? fault)
    {
        if (fault == _snapshotFault)
        {
            return;
        }

        var recovered = fault is null;
        _snapshotFault = fault;

        if (recovered)
        {
            Log.Info(Scope, "reading processes from the kernel snapshot again");
            return;
        }

        Log.Warn(
            Scope,
            $"{fault}; falling back to per-process handles, which cannot read the times of elevated or out-of-session processes");
    }

    // ---- fallback path: one handle per process ------------------------------------------------

    /// <summary>
    /// The documented degradation, used only when the snapshot call fails. It cannot see the
    /// processor times of anything this app may not open, and says so per row rather than
    /// reporting 0.
    /// </summary>
    private static bool SampleFromHandles(long now)
    {
        Process[] processes;

        try
        {
            processes = Process.GetProcesses();
        }
        catch (InvalidOperationException ex)
        {
            Log.Warn(Scope, "could not enumerate processes", ex);
            return false;
        }
        catch (Win32Exception ex)
        {
            Log.Warn(Scope, "could not enumerate processes", ex);
            return false;
        }

        try
        {
            foreach (var process in processes)
            {
                if (process.Id == 0)
                {
                    continue;
                }

                Observe(process, now);
            }
        }
        finally
        {
            // Every Process holds an OS handle, including the ones skipped above and any left
            // behind if a read throws something unforeseen. Leaking a few hundred handles a second
            // exhausts the process in minutes.
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        return true;
    }

    /// <summary>Reads one process through its handle and appends its row.</summary>
    private static void Observe(Process process, long now)
    {
        int pid;
        string name;
        long workingSet;
        int threads;
        int handles;

        try
        {
            pid = process.Id;
            name = process.ProcessName;

            // These come out of the snapshot Process.GetProcesses() already took, so they do not
            // need a handle and in practice never fail. The processor times below do.
            workingSet = process.WorkingSet64;
            threads = process.Threads.Count;
            handles = process.HandleCount;
        }
        catch (InvalidOperationException)
        {
            return; // ignore: the process exited between the snapshot and this read, so it has no row
        }
        catch (Win32Exception)
        {
            return; // ignore: unreadable even at snapshot level; one process, not the list
        }

        long cpuTicks;
        long startTicks;

        try
        {
            cpuTicks = process.TotalProcessorTime.Ticks;

            // Read in the same breath as the times, and for the same reason: both come from
            // GetProcessTimes through one handle, so either both are available or neither is.
            // Converted to a FILETIME to match what the snapshot path stores, so a row's identity
            // does not change when the two paths swap.
            startTicks = process.StartTime.ToFileTime();
        }
        catch (Win32Exception)
        {
            // ignore: expected for elevated, other-session and protected processes, and it is the
            // steady state for about a third of them — logging would write hundreds of lines a
            // second. The row says CpuKnown: false instead of inventing a figure.
            Scratch.Add(new ProcessUsage(pid, name, 0, workingSet, false, threads, handles));
            return;
        }
        catch (InvalidOperationException)
        {
            // ignore: exited mid-read; memory is still worth showing for this one last tick
            Scratch.Add(new ProcessUsage(pid, name, 0, workingSet, false, threads, handles));
            return;
        }
        catch (ArgumentOutOfRangeException)
        {
            // ignore: a start time outside the FILETIME range is not a time this app can reason
            // about, so the row keeps its memory and declares its CPU unknown
            Scratch.Add(new ProcessUsage(pid, name, 0, workingSet, false, threads, handles));
            return;
        }

        Scratch.Add(new ProcessUsage(pid, name, Measure(pid, name, startTicks, cpuTicks, now), workingSet, true, threads, handles));
    }

    // ---- shared arithmetic ---------------------------------------------------------------------

    /// <summary>
    /// Folds one reading into the per-process state and returns its share of the machine.
    /// </summary>
    private static double Measure(int pid, string name, long startTicks, long cpuTicks, long now)
    {
        if (!Tracking.TryGetValue(pid, out var tracked))
        {
            // First sighting: there is nothing to subtract from, so report 0 rather than dividing
            // the process's whole lifetime of CPU by one tick and claiming it is pinning a core.
            // One tick later the number is real.
            Tracking[pid] = new Tracked(name, startTicks, cpuTicks, now, _generation);
            return 0;
        }

        tracked.Seen = _generation;

        // Windows reuses pids, and quickly — on a busy machine a pid freed by one process is often
        // handed to an unrelated one within seconds. Subtracting the old process's cumulative CPU
        // from the new one's yields a large negative delta, or worse, a plausible-looking positive
        // one that pins the wrong name to the wrong load. The start time is what actually
        // distinguishes them, so a changed start time means this is a different process wearing a
        // recycled number: throw the baseline away and start it over.
        if (tracked.StartTicks != startTicks)
        {
            tracked.Reset(name, startTicks, cpuTicks, now);
            return 0;
        }

        var elapsed = Stopwatch.GetElapsedTime(tracked.At, now);

        if (elapsed < MinimumInterval)
        {
            return tracked.Fraction;
        }

        var fraction = CpuFraction(cpuTicks - tracked.CpuTicks, elapsed, Environment.ProcessorCount);

        tracked.CpuTicks = cpuTicks;
        tracked.At = now;
        tracked.Fraction = fraction;
        return fraction;
    }

    /// <summary>
    /// Processor time consumed over a wall-clock interval, as a share of the whole machine.
    /// <para>
    /// A process spread across every core can burn <c>ProcessorCount</c> seconds of CPU per second
    /// of wall clock, so dividing by the core count is what puts a saturated machine at 100% rather
    /// than at 2400% — and matches what <see cref="SystemMetrics.Cpu"/> shows above the list.
    /// </para>
    /// <para>Kept free of <see cref="Process"/> so the arithmetic can be exercised on its own.</para>
    /// </summary>
    internal static double CpuFraction(long cpuDeltaTicks, TimeSpan elapsed, int processorCount)
    {
        if (cpuDeltaTicks <= 0 || elapsed <= TimeSpan.Zero || processorCount <= 0)
        {
            return 0;
        }

        return Math.Clamp((double)cpuDeltaTicks / (elapsed.Ticks * (double)processorCount), 0, 1);
    }

    /// <summary>Drops the state of processes that were not in this pass.</summary>
    private static void Prune()
    {
        if (Tracking.Count == 0)
        {
            return;
        }

        Expired.Clear();

        foreach (var (pid, tracked) in Tracking)
        {
            if (tracked.Seen != _generation)
            {
                Expired.Add(pid);
            }
        }

        foreach (var pid in Expired)
        {
            Tracking.Remove(pid);
        }
    }

    /// <summary>
    /// Ranks equal rows by something that does not change between ticks. Without it, the dozens of
    /// processes sitting at 0% and similar memory reorder themselves on every sample and the list
    /// visibly churns while nothing is happening.
    /// </summary>
    private static int Tiebreak(ProcessUsage a, ProcessUsage b)
    {
        var name = string.CompareOrdinal(a.Name, b.Name);
        return name != 0 ? name : a.Pid.CompareTo(b.Pid);
    }

    /// <summary>
    /// What is remembered about one process between samples. A class, not a struct, so a reading
    /// updates it in place instead of rehashing the dictionary entry; one small object per live
    /// process, pruned when it exits.
    /// </summary>
    private sealed class Tracked(string name, long startTicks, long cpuTicks, long at, int seen)
    {
        /// <summary>
        /// The image name, held so it is built once per process rather than once per sample. See
        /// <see cref="NameOf"/>.
        /// </summary>
        public string Name { get; private set; } = name;

        /// <summary>Identity, alongside the pid. See the recycling note in <see cref="Measure"/>.</summary>
        public long StartTicks { get; private set; } = startTicks;

        /// <summary>Cumulative processor time at the last reading that moved the baseline.</summary>
        public long CpuTicks { get; set; } = cpuTicks;

        /// <summary><see cref="Stopwatch"/> timestamp of that reading — monotonic, unlike the wall clock.</summary>
        public long At { get; set; } = at;

        /// <summary>
        /// The last share actually measured. Held per process rather than globally because a
        /// process whose times failed on one pass keeps its baseline and is measured across both
        /// intervals on the next.
        /// </summary>
        public double Fraction { get; set; }

        /// <summary>Generation of the last pass that saw this process.</summary>
        public int Seen { get; set; } = seen;

        public void Reset(string newName, long newStartTicks, long newCpuTicks, long at)
        {
            Name = newName;
            StartTicks = newStartTicks;
            CpuTicks = newCpuTicks;
            At = at;
            Fraction = 0;
        }
    }

    // ---- ntdll ---------------------------------------------------------------------------------

    // LibraryImport, not DllImport: the marshalling stub is source generated at build time, and
    // DllImport builds one with runtime IL emission, which does not exist under Native AOT. Every
    // parameter is already blittable, so the generated stub is a direct call.
    [LibraryImport("ntdll")]
    private static partial int NtQuerySystemInformation(
        int systemInformationClass,
        byte* systemInformation,
        uint systemInformationLength,
        out uint returnLength);

    /// <summary>
    /// <c>UNICODE_STRING</c>. <see cref="Length"/> counts <b>bytes</b>, not characters, and the
    /// buffer is not null-terminated.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private readonly struct UnicodeString
    {
        [FieldOffset(0)]
        public readonly ushort Length;

        // 0x02 MaximumLength, then four bytes of padding before the 8-aligned pointer.
        [FieldOffset(8)]
        public readonly nint Buffer;
    }

    /// <summary>
    /// <c>SYSTEM_PROCESS_INFORMATION</c>, 64-bit layout.
    /// <para>
    /// Explicit offsets rather than a sequential field list, because only a handful of the
    /// structure's thirty-odd members are read and the rest would be unreferenced fields whose only
    /// job is to push the next offset along — a layout that is silently wrong the moment one of
    /// them is mistyped. Offsets are checked against the total <c>Size</c>, so a member at the wrong
    /// place fails to compile rather than reading the neighbouring field.
    /// </para>
    /// <para>
    /// <c>KernelTime</c>, <c>UserTime</c> and <c>CreateTime</c> are 100-nanosecond units, the same
    /// as <see cref="TimeSpan"/> ticks and <c>FILETIME</c> respectively.
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 0x100)]
    private readonly struct SystemProcessInformation
    {
        /// <summary>Bytes from this entry to the next, or 0 at the end of the chain.</summary>
        [FieldOffset(0x00)]
        public readonly uint NextEntryOffset;

        /// <summary>How many <see cref="SystemThreadInformation"/> records follow the fixed part.</summary>
        [FieldOffset(0x04)]
        public readonly uint NumberOfThreads;

        [FieldOffset(0x20)]
        public readonly long CreateTime;

        [FieldOffset(0x28)]
        public readonly long UserTime;

        [FieldOffset(0x30)]
        public readonly long KernelTime;

        [FieldOffset(0x38)]
        public readonly UnicodeString ImageName;

        [FieldOffset(0x50)]
        public readonly nint UniqueProcessId;

        /// <summary>[S-1144] After <c>InheritedFromUniqueProcessId</c> (0x58).</summary>
        [FieldOffset(0x60)]
        public readonly uint HandleCount;

        [FieldOffset(0x90)]
        public readonly nuint WorkingSetSize;
    }

    /// <summary>
    /// <c>SYSTEM_THREAD_INFORMATION</c>, 64-bit layout — 0x50 bytes.
    /// <para>
    /// Declared for its size alone. An array of these follows every process entry, and its length is
    /// what says where the entry ends, so the walk needs the size to tell a well-formed chain from
    /// one it should stop following. No member of it is read, and declaring members that nothing
    /// reads would be dead code whose correctness nothing checks.
    /// </para>
    /// <para>
    /// The layout it stands for: <c>KernelTime</c> 0x00, <c>UserTime</c> 0x08, <c>CreateTime</c>
    /// 0x10, <c>WaitTime</c> 0x18, <c>StartAddress</c> 0x20, <c>ClientId</c> 0x28,
    /// <c>Priority</c> 0x38, <c>BasePriority</c> 0x3C, <c>ContextSwitches</c> 0x40,
    /// <c>ThreadState</c> 0x44, <c>WaitReason</c> 0x48, padded to 0x50.
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 0x50)]
    private readonly struct SystemThreadInformation
    {
    }
}
