#if DEBUG
using Themia.App.Services;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-1169] The disk and ping meters' gallery samples: three made-up drives (one near full, one
/// removable) with reads and writes that rise and fall, and a router that answers in 3–40 ms and
/// misses one probe in nine — so a machine with one drive and no network still shows the populated
/// layout, the second series, and loss on the chart's baseline.
/// <para>
/// Read only by a meter that is a preview (<see cref="Widgets.IWidgetSurface.IsPreview"/>), so a
/// desktop widget in the same Debug process never sees made-up drives. Debug builds only.
/// </para>
/// </summary>
internal static class DiskPingFixture
{
    private const ulong GiB = 1024UL * 1024 * 1024;

    // The samples run on a clock, not a call count: every preview tile asks once a tick, and a
    // shared counter would advance once per tile and draw each chart as noise.
    private static long _diskStart;
    private static long _pingStart;

    /// <summary>Whether the disk sample is installed; the drives a preview reads instead of the machine's.</summary>
    public static IReadOnlyList<DiskVolume>? Volumes { get; private set; }

    /// <summary>Whether the ping sample is installed.</summary>
    public static bool Ping { get; private set; }

    public static void InstallDisk(bool populated)
    {
        _diskStart = Environment.TickCount64;
        Volumes = populated
            ?
            [
                new DiskVolume("C:\\", "System", "NTFS", false, 931 * GiB, 391 * GiB),
                new DiskVolume("D:\\", "Data", "NTFS", false, 1863 * GiB, 335 * GiB),
                new DiskVolume("E:\\", "", "exFAT", true, 58 * GiB, 4 * GiB),
            ]
            : null;
    }

    public static void InstallPing(bool populated)
    {
        _pingStart = Environment.TickCount64;
        Ping = populated;
    }

    /// <summary>Rates that swell and ebb, writes trailing reads, so the chart has two shapes to draw.</summary>
    public static DiskActivity Activity()
    {
        var t = (int)((Environment.TickCount64 - _diskStart) / 1000);
        var read = Math.Max(0, 18e6 * Math.Sin(t / 4.0) + (6e6 * Math.Sin(t / 1.7)));
        var write = Math.Max(0, 7e6 * Math.Sin((t - 3) / 5.0) + 1.5e6);

        var rates = new Dictionary<string, DiskRates>(StringComparer.OrdinalIgnoreCase)
        {
            ["C:"] = new(read, write),
            ["D:"] = new(read / 6, write / 3),
            ["E:"] = new(0, t % 7 == 0 ? 2.4e6 : 0),
        };

        return new DiskActivity(rates);
    }

    /// <summary>The next probe: an answer in ms, or null for a miss (one in nine).</summary>
    public static double? Probe()
    {
        var t = (int)((Environment.TickCount64 - _pingStart) / 1000);

        if (t % 9 == 4)
        {
            return null;
        }

        return Math.Round(12 + (6 * Math.Sin(t / 2.5)) + (t % 11 == 7 ? 28 : 0));
    }

    public static DiskConfig DiskConfig() => new() { Drive = "C:\\", IntervalSeconds = 1, GraphSpanSeconds = 60 };

    public static PingConfig PingConfig() => new() { Host = "192.168.1.1", HostLabel = "Router", IntervalSeconds = 1, GraphSpanSeconds = 60 };
}
#endif
