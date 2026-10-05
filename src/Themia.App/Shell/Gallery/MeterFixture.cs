#if DEBUG
using Themia.Core.Model;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-1823] The meters' gallery samples, as functions of time: a processor that idles with bursts,
/// memory that drifts, network downloads, disk reads and writes, graphics load. Every value is a
/// pure function of a second on one clock, so a preview's history can be filled with the samples it
/// would have taken over the last window (negative seconds) and the live samples carry on the same
/// curve — the page shows what a meter looks like after running a while, not its first seconds.
/// <para>
/// The clock is shared and starts when the page is reset, not per call: every tile on the page
/// asks once a tick, and seven tiles reading the same moment draw the same chart. Read only by a
/// meter that is a preview. Debug builds only.
/// </para>
/// </summary>
internal static class MeterFixture
{
    private static readonly HashSet<WidgetKind> Installed = [];
    private static long _start = Environment.TickCount64;

    /// <summary>Seconds on the fixture clock: 0 when the page was reset.</summary>
    public static double Now => (Environment.TickCount64 - _start) / 1000.0;

    /// <summary>Start the clock again and uninstall every kind (the plain pages read the machine).</summary>
    public static void Reset()
    {
        _start = Environment.TickCount64;
        Installed.Clear();
    }

    public static void Install(WidgetKind kind) => Installed.Add(kind);

    public static bool Has(WidgetKind kind) => Installed.Contains(kind);

    /// <summary>Processor load, 0..1: a 10–25% murmur with a build-sized burst every 80 s.</summary>
    public static double Cpu(double t)
        => Math.Clamp(0.1 + (0.15 * Noise(t / 3)) + (0.55 * Bump(t + 20, 80, 22) * (0.7 + (0.3 * Noise(t)))), 0, 1);

    /// <summary>Memory in use, 0..1: around 60%, rising a little with the processor's bursts.</summary>
    public static double Memory(double t)
        => Math.Clamp(0.58 + (0.03 * Noise(t / 25)) + (0.07 * Bump(t + 15, 80, 30)), 0, 1);

    /// <summary>Download and upload, bytes per second: chatter, with a download every 50 s.</summary>
    public static (double Down, double Up) Network(double t)
    {
        var down = (60e3 * Noise(t / 2)) + (7.5e6 * Bump(t + 12, 50, 14) * (0.6 + (0.4 * Noise(t * 1.3))));
        var up = (18e3 * Noise((t / 2) + 40)) + (0.6e6 * Bump(t + 30, 50, 6));
        return (down, up);
    }

    /// <summary>Reads and writes, bytes per second, on the fixture's system drive: background writes, and copies.</summary>
    public static (double Read, double Write) Disk(double t)
    {
        var read = (150e3 * Noise(t / 4)) + (95e6 * Bump(t + 70, 110, 30) * (0.6 + (0.4 * Noise(t / 2))));
        var write = (220e3 * Noise((t / 3) + 17)) + (40e6 * Bump(t + 35, 110, 18) * (0.5 + (0.5 * Noise((t / 2) + 9))));
        return (read, write);
    }

    /// <summary>A router's answer in ms, or null for a miss: about 12 ms, one probe in twelve slower, one in twenty lost.</summary>
    public static double? Ping(double t)
    {
        var probe = Math.Floor(t);

        if (Hash(probe + 0.5) > 0.95)
        {
            return null;
        }

        var spike = Hash(probe * 0.37) > 0.92 ? 15 + (20 * Hash(probe + 0.25)) : 0;
        return Math.Round(10 + (4 * Noise(t / 40)) + (2 * Hash(probe)) + spike);
    }

    /// <summary>Graphics load, 0..1, per adapter: the first renders in bursts, the second barely works.</summary>
    public static double Gpu(int adapter, double t) => adapter == 0
        ? Math.Clamp(0.06 + (0.08 * Noise(t / 4)) + (0.6 * Bump(t + 40, 90, 35) * (0.75 + (0.25 * Noise(t / 2)))), 0, 1)
        : Math.Clamp(0.02 + (0.05 * Noise((t / 5) + 31)), 0, 1);

    /// <summary>A smooth pulse, 0..1, <paramref name="width"/> seconds long, once every <paramref name="period"/> seconds.</summary>
    private static double Bump(double t, double period, double width)
    {
        var phase = ((t % period) + period) % period;
        return phase >= width ? 0 : Math.Pow(Math.Sin(Math.PI * phase / width), 2);
    }

    /// <summary>Smooth value noise, 0..1: a hash per whole second, eased between them. Deterministic.</summary>
    private static double Noise(double t)
    {
        var i = Math.Floor(t);
        var f = t - i;
        var ease = f * f * (3 - (2 * f));
        return Hash(i) + ((Hash(i + 1) - Hash(i)) * ease);
    }

    private static double Hash(double n)
    {
        var x = Math.Sin(n * 12.9898) * 43758.5453;
        return x - Math.Floor(x);
    }
}
#endif
