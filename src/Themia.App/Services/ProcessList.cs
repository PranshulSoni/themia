using System.ComponentModel;
using System.Diagnostics;
using Themia.Core.Diagnostics;
using Themia.Core.Model;

namespace Themia.App.Services;

/// <summary>
/// One row of the process list.
/// <para>
/// <paramref name="CpuKnown"/> is not decoration. Windows refuses the processor times of any
/// process this app cannot open — anything elevated, anything in another session, and the
/// protected system processes — which on an ordinary non-elevated desktop is roughly a third of
/// everything running. Folding that into <c>CpuFraction = 0</c> would make "busy but unreadable"
/// look exactly like "idle", so the caller is told which it is and shows <c>--</c>.
/// </para>
/// </summary>
/// <param name="Pid">Process id, unique only among processes alive right now.</param>
/// <param name="Name">Image name without the extension, as <c>Process.ProcessName</c> reports it.</param>
/// <param name="CpuFraction">Share of the whole machine, 0..1, since the previous sample.</param>
/// <param name="WorkingSetBytes">Physical memory currently charged to the process.</param>
/// <param name="CpuKnown">False when the processor times could not be read at all.</param>
public readonly record struct ProcessUsage(
    int Pid,
    string Name,
    double CpuFraction,
    long WorkingSetBytes,
    bool CpuKnown)
{
    /// <summary>The CPU column, or <c>--</c> where the times are not readable.</summary>
    public string CpuText => CpuKnown ? Themia.Core.Widgets.Formatting.Percent(CpuFraction) : "--";

    /// <summary>The memory column.</summary>
    public string MemoryText => Themia.Core.Widgets.Formatting.Bytes(WorkingSetBytes);
}

/// <summary>
/// The running processes, ranked — the data behind a Task Manager style list.
/// <para>
/// Built on <see cref="Process"/> rather than WMI or <c>PerformanceCounter</c> for the same reason
/// <see cref="SystemMetrics"/> avoids them: both drag in reflection-heavy stacks that do not
/// survive trimming, and both are far slower than a widget sampling once a second can justify.
/// </para>
/// <para>
/// Single-threaded, exactly like <see cref="SystemMetrics"/>: every call happens on the UI thread's
/// tick, so the sample state below needs no lock.
/// </para>
/// </summary>
internal static class ProcessList
{
    private const string Scope = "processes";

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

    // Reused between samples so the once-a-second pass does not allocate a fresh list each time.
    private static readonly List<ProcessUsage> Scratch = [];
    private static readonly List<int> Expired = [];

    // Bumped once per sample; anything still carrying an older number was not in this pass and is
    // therefore gone. Without this the dictionary accumulates an entry for every process that has
    // ever run, which over a week of uptime and a few thousand build steps is a slow leak.
    private static int _generation;

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

        Scratch.Sort(sort == ProcessSort.Cpu ? ByCpu : ByMemory);

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

        _generation++;
        Scratch.Clear();

        try
        {
            var now = Stopwatch.GetTimestamp();

            foreach (var process in processes)
            {
                // Pid 0 is the idle accounting bucket, not a process. It has no readable times and
                // an 8 KB working set, so it would only ever sit at the bottom of the list looking
                // like something the user could act on.
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

        Prune();
        return true;
    }

    /// <summary>Reads one process and appends its row.</summary>
    private static void Observe(Process process, long now)
    {
        int pid;
        string name;
        long workingSet;

        try
        {
            pid = process.Id;
            name = process.ProcessName;

            // These three come out of the snapshot Process.GetProcesses() already took, so they do
            // not need a handle and in practice never fail. The processor times below do.
            workingSet = process.WorkingSet64;
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
            // Whenever there is a CPU number to attribute there is also a start time to pin it to.
            startTicks = process.StartTime.Ticks;
        }
        catch (Win32Exception)
        {
            // ignore: expected for elevated, other-session and protected processes, and it is the
            // steady state for about a third of them — logging would write hundreds of lines a
            // second. The row says CpuKnown: false instead of inventing a figure.
            Scratch.Add(new ProcessUsage(pid, name, 0, workingSet, false));
            return;
        }
        catch (InvalidOperationException)
        {
            // ignore: exited mid-read; memory is still worth showing for this one last tick
            Scratch.Add(new ProcessUsage(pid, name, 0, workingSet, false));
            return;
        }

        Scratch.Add(new ProcessUsage(pid, name, Measure(pid, startTicks, cpuTicks, now), workingSet, true));
    }

    /// <summary>
    /// Folds one reading into the per-process state and returns its share of the machine.
    /// </summary>
    private static double Measure(int pid, long startTicks, long cpuTicks, long now)
    {
        if (!Tracking.TryGetValue(pid, out var tracked))
        {
            // First sighting: there is nothing to subtract from, so report 0 rather than dividing
            // the process's whole lifetime of CPU by one tick and claiming it is pinning a core.
            // One tick later the number is real.
            Tracking[pid] = new Tracked(startTicks, cpuTicks, now, _generation);
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
            tracked.Reset(startTicks, cpuTicks, now);
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
    private sealed class Tracked(long startTicks, long cpuTicks, long at, int seen)
    {
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

        public void Reset(long newStartTicks, long newCpuTicks, long at)
        {
            StartTicks = newStartTicks;
            CpuTicks = newCpuTicks;
            At = at;
            Fraction = 0;
        }
    }
}
