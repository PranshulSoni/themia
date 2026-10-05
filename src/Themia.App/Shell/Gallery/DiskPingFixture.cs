#if DEBUG
using Themia.App.Services;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-1169] The disk and ping meters' gallery samples: three made-up drives (one removable) whose
/// reads and writes rise and fall, and a router that answers in about 12 ms, now and then slower,
/// and misses about one probe in twenty — so a machine with one drive and no network still shows
/// the populated layout and loss on the chart's baseline. Both are functions of a second on <see cref="MeterFixture"/>'s clock, so a
/// preview's history is filled with them too ([S-1823]).
/// <para>
/// Read only by a meter that is a preview (<see cref="Widgets.IWidgetSurface.IsPreview"/>), so a
/// desktop widget in the same Debug process never sees made-up drives. Debug builds only.
/// </para>
/// </summary>
internal static class DiskPingFixture
{
    /// <summary>Whether the disk sample is installed; the drives a preview names instead of the machine's.</summary>
    public static IReadOnlyList<DiskVolume>? Volumes { get; private set; }

    /// <summary>Whether the ping sample is installed.</summary>
    public static bool Ping { get; private set; }

    public static void InstallDisk(bool populated)
    {
        Volumes = populated
            ?
            [
                new DiskVolume("C:\\", "System", false),
                new DiskVolume("D:\\", "Data", false),
                new DiskVolume("E:\\", "", true),
            ]
            : null;
    }

    public static void InstallPing(bool populated) => Ping = populated;

    /// <summary>The drives' rates at <paramref name="t"/> seconds: the system drive busy, the others quieter.</summary>
    public static DiskActivity Activity(double t)
    {
        var (read, write) = MeterFixture.Disk(t);

        var rates = new Dictionary<string, DiskRates>(StringComparer.OrdinalIgnoreCase)
        {
            ["C:"] = new(read, write),
            ["D:"] = new(read / 6, write / 3),
            ["E:"] = new(0, (int)Math.Floor(t) % 7 == 0 ? 2.4e6 : 0),
        };

        return new DiskActivity(rates);
    }

    /// <summary>The probe at <paramref name="t"/> seconds: an answer in ms, or null for a miss (about one in twenty).</summary>
    public static double? Probe(double t) => MeterFixture.Ping(t);

    public static DiskConfig DiskConfig() => new() { Drive = "C:\\" };

    public static PingConfig PingConfig() => new() { Host = "192.168.1.1", HostLabel = "Router" };
}
#endif
