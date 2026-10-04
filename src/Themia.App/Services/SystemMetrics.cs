using Themia.App.Interop;
using Themia.Core.Diagnostics;

namespace Themia.App.Services;

/// <summary>
/// System readings, straight from Win32.
/// <para>
/// Not <c>PerformanceCounter</c>: it pulls in the whole perf-counter registry stack, is slow to
/// first value, and is not trim-safe — none of which a widget that samples one number per second
/// can justify. These calls are a few microseconds each and work under AOT unchanged.
/// </para>
/// </summary>
internal static class SystemMetrics
{
    private const string Scope = "metrics";

    // CPU load is a delta between two samples of the system's cumulative timers, so the previous
    // sample has to be remembered. Single-threaded: every read happens on the UI thread's tick.
    private static ulong _lastIdle;
    private static ulong _lastKernel;
    private static ulong _lastUser;
    private static bool _haveCpuBaseline;

    // [S-643] Every CPU widget reads through this, so a second tile in the same tick gets the
    // first one's reading instead of a near-zero delta against the baseline it just moved.
    private static readonly Themia.Core.Widgets.SampleCache<MeterReading> CpuCache = new();

    /// <summary>
    /// Processor load across all cores.
    /// <para>
    /// The very first call has no previous sample to subtract, so it reports 0 rather than
    /// inventing a number — one tick later it is real.
    /// </para>
    /// </summary>
    public static MeterReading Cpu()
    {
        var now = Environment.TickCount64;
        if (CpuCache.TryReuse(now, out var recent))
        {
            return recent;
        }

        var reading = ReadCpu();
        CpuCache.Store(now, reading);
        return reading;
    }

    private static MeterReading ReadCpu()
    {
        if (!Win32.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return MeterReading.Unavailable("unavailable");
        }

        var idleTicks = idle.ToUInt64();
        var kernelTicks = kernel.ToUInt64();
        var userTicks = user.ToUInt64();

        if (!_haveCpuBaseline)
        {
            (_lastIdle, _lastKernel, _lastUser, _haveCpuBaseline) = (idleTicks, kernelTicks, userTicks, true);
            return new MeterReading(0, "0%", "all cores", true);
        }

        // GetSystemTimes' kernel total already includes idle, so busy = (kernel+user) - idle.
        var idleDelta = idleTicks - _lastIdle;
        var totalDelta = (kernelTicks - _lastKernel) + (userTicks - _lastUser);

        (_lastIdle, _lastKernel, _lastUser) = (idleTicks, kernelTicks, userTicks);

        if (totalDelta == 0)
        {
            return new MeterReading(0, "0%", "all cores", true);
        }

        var busy = Math.Clamp(1.0 - ((double)idleDelta / totalDelta), 0, 1);
        return new MeterReading(busy, Themia.Core.Widgets.Formatting.Percent(busy), "all cores", true);
    }

    /// <summary>Space used on a drive. An empty or unreadable drive reports unavailable rather than 0%.</summary>
    public static MeterReading Disk(string drive)
    {
        var root = NormaliseDrive(drive);

        if (!Win32.GetDiskFreeSpaceEx(root, out _, out var total, out var totalFree) || total == 0)
        {
            return MeterReading.Unavailable($"{root.TrimEnd('\\')} unavailable");
        }

        var used = total - totalFree;
        var fraction = (double)used / total;

        return new MeterReading(
            fraction,
            Themia.Core.Widgets.Formatting.Percent(fraction),
            $"{Themia.Core.Widgets.Formatting.Bytes(totalFree)} free of {Themia.Core.Widgets.Formatting.Bytes(total)}",
            true,
            Facts:
            [
                new MeterFact("Used", Themia.Core.Widgets.Formatting.Bytes(used)),
                new MeterFact("Free", Themia.Core.Widgets.Formatting.Bytes(totalFree)),
                new MeterFact("Total", Themia.Core.Widgets.Formatting.Bytes(total)),
            ]);
    }

    /// <summary>
    /// The drive root to query. Falls back to the system drive, so an empty or malformed setting
    /// still shows something true rather than an error.
    /// </summary>
    public static string NormaliseDrive(string drive)
    {
        var trimmed = drive?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        }

        if (trimmed.Length == 1 && char.IsLetter(trimmed[0]))
        {
            return $"{char.ToUpperInvariant(trimmed[0])}:\\";
        }

        return trimmed.EndsWith('\\') ? trimmed : trimmed + "\\";
    }

    /// <summary>Charge level, and whether it is on mains.</summary>
    /// <param name="showTimeRemaining">Put Windows' estimate in the caption while on battery.</param>
    /// <param name="charging">[S-1123] On mains: the meter's colour is the accent, not a low-charge warning.</param>
    public static MeterReading Battery(bool showTimeRemaining, out bool charging)
    {
        charging = false;

        if (!Win32.GetSystemPowerStatus(out var status))
        {
            return MeterReading.Unavailable("unavailable");
        }

        // 255 is "unknown", which desktops without a battery report.
        if (status.BatteryLifePercent > 100)
        {
            return MeterReading.Missing("No battery");
        }

        var fraction = status.BatteryLifePercent / 100.0;
        charging = status.ACLineStatus == 1;

        string? secondary = charging ? "charging" : null;

        // -1 is "unknown"; the estimate is also meaningless while charging.
        if (!charging && showTimeRemaining && status.BatteryLifeTime > 0 && status.BatteryLifeTime != uint.MaxValue)
        {
            secondary = $"{Themia.Core.Widgets.Formatting.Duration(TimeSpan.FromSeconds(status.BatteryLifeTime))} left";
        }

        secondary ??= "on battery";
        return new MeterReading(fraction, $"{status.BatteryLifePercent}%", secondary, true);
    }

    /// <summary>Drives that can be reported on, for the config dropdown.</summary>
    public static List<string> FixedDrives()
    {
        var drives = new List<string>();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType is DriveType.Fixed or DriveType.Removable && drive.IsReady)
                {
                    drives.Add(drive.Name);
                }
            }
        }
        catch (IOException ex)
        {
            Log.Warn(Scope, "could not enumerate drives", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Warn(Scope, "could not enumerate drives", ex);
        }

        if (drives.Count == 0)
        {
            drives.Add(NormaliseDrive(string.Empty));
        }

        return drives;
    }
}
