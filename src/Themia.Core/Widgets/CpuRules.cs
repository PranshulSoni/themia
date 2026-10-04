using System.Globalization;

namespace Themia.Core.Widgets;

/// <summary>
/// One logical processor's cumulative times, in 100-ns units, as
/// <c>SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION</c> reports them. <see cref="Kernel"/> already
/// includes <see cref="Idle"/>, exactly as <c>GetSystemTimes</c>' totals do.
/// </summary>
public readonly record struct CoreTimes(long Idle, long Kernel, long User);

/// <summary>
/// The processor meter's arithmetic and wording: per-core load, the clock, uptime, totals and the
/// sensor lines. Pure, so the edge cases (a counter that went backwards, a sensor that reads 400 °C,
/// a reading that has not arrived yet) are pinned by tests rather than by looking at a tile.
/// </summary>
public static class CpuRules
{
    /// <summary>[S-1146] The coldest and hottest a processor sensor may plausibly read; outside is a broken sensor.</summary>
    public const double MinCelsius = 0;

    /// <inheritdoc cref="MinCelsius"/>
    public const double MaxCelsius = 150;

    /// <summary>[S-1147] The plausible range of a fan, in RPM; 0 is a header with no fan on it.</summary>
    public const double MinRpm = 1;

    /// <inheritdoc cref="MinRpm"/>
    public const double MaxRpm = 60000;

    /// <summary>The widest a clock caption gets: "9.99 GHz".</summary>
    public const int FrequencyCharacters = 8;

    /// <summary>
    /// [S-1142] One processor's load between two samples: busy over elapsed, where busy is everything
    /// that was not idle. Zero when nothing elapsed or a counter went backwards (a processor taken
    /// offline and back), rather than a negative or a division by zero.
    /// </summary>
    public static double Load(CoreTimes before, CoreTimes after)
    {
        var total = (after.Kernel - before.Kernel) + (after.User - before.User);
        var idle = after.Idle - before.Idle;

        if (total <= 0 || idle < 0)
        {
            return 0;
        }

        return Math.Clamp(1 - ((double)idle / total), 0, 1);
    }

    /// <summary>
    /// [S-1142] Every processor's load. A processor count that changed between the samples (one
    /// came online) gives no answer for this tick rather than pairing the wrong processors.
    /// </summary>
    public static double[]? Loads(IReadOnlyList<CoreTimes> before, IReadOnlyList<CoreTimes> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        if (before.Count != after.Count || after.Count == 0)
        {
            return null;
        }

        var loads = new double[after.Count];

        for (var i = 0; i < loads.Length; i++)
        {
            loads[i] = Load(before[i], after[i]);
        }

        return loads;
    }

    /// <summary>
    /// [S-1141] The clock the processor is actually running at, in MHz: the base clock scaled by
    /// <c>% Processor Performance</c> — what Task Manager prints as "Speed", turbo included.
    /// Without the counter, the clock the power manager reports (often just the base clock). Null
    /// when neither says anything.
    /// </summary>
    public static double? EffectiveMhz(double? performancePercent, double baseMhz, double reportedMhz)
    {
        if (performancePercent is { } p && double.IsFinite(p) && p > 0 && baseMhz > 0)
        {
            return baseMhz * p / 100;
        }

        return reportedMhz > 0 && double.IsFinite(reportedMhz) ? reportedMhz : null;
    }

    /// <summary>[S-1141] A clock as the original printed it: gigahertz, two decimals ("4.72 GHz").</summary>
    public static string Gigahertz(double mhz)
        => string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, mhz) / 1000:0.00} GHz");

    /// <summary>
    /// [S-1143] Time since boot in its two largest units, as the original showed it: <c>3d 4h</c>,
    /// <c>2h 5m</c>, <c>7m</c>.
    /// </summary>
    public static string Uptime(TimeSpan up)
    {
        if (up < TimeSpan.Zero)
        {
            up = TimeSpan.Zero;
        }

        if (up.TotalDays >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)up.TotalDays}d {up.Hours}h");
        }

        if (up.TotalHours >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)up.TotalHours}h {up.Minutes}m");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{(int)up.TotalMinutes}m");
    }

    /// <summary>A count with thousands grouped: "4,210".</summary>
    public static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>[S-1144] "312 processes · 4,210 threads", the footer of a process list.</summary>
    public static string Totals(int processes, long threads)
        => $"{Count(processes)} processes · {Count(threads)} threads";

    /// <summary>[S-1146] A thermal-zone reading (Kelvin) in Celsius, or null when it is not a plausible processor temperature.</summary>
    public static double? Celsius(double kelvin)
    {
        if (!double.IsFinite(kelvin))
        {
            return null;
        }

        var celsius = kelvin - 273.15;
        return Plausible(celsius) ? celsius : null;
    }

    /// <summary>[S-1146] The hottest plausible zone, the one the original reported; null when none is plausible.</summary>
    public static double? HottestZone(IEnumerable<double> kelvins)
    {
        ArgumentNullException.ThrowIfNull(kelvins);

        double? hottest = null;

        foreach (var kelvin in kelvins)
        {
            if (Celsius(kelvin) is { } c && (hottest is null || c > hottest))
            {
                hottest = c;
            }
        }

        return hottest;
    }

    /// <summary>Whether a Celsius reading is one a processor can have.</summary>
    public static bool Plausible(double celsius) => double.IsFinite(celsius) && celsius >= MinCelsius && celsius <= MaxCelsius;

    /// <summary>
    /// [S-1146] The temperature line: null before the first poll answered (so a tile never flashes
    /// "N/A" while the sensor is still being asked), "N/A" when no source has a reading, else whole
    /// degrees.
    /// </summary>
    public static string? TemperatureText(bool polled, double? celsius)
    {
        if (!polled)
        {
            return null;
        }

        return celsius is { } c && Plausible(c)
            ? string.Create(CultureInfo.InvariantCulture, $"{Math.Round(c, MidpointRounding.AwayFromZero):0} °C")
            : "N/A";
    }

    /// <summary>[S-1147] The fan line, on the same rule as <see cref="TemperatureText"/>.</summary>
    public static string? FanText(bool polled, double? rpm)
    {
        if (!polled)
        {
            return null;
        }

        return rpm is { } r && double.IsFinite(r) && r >= MinRpm && r <= MaxRpm
            ? $"{Count((long)Math.Round(r))} RPM"
            : "N/A";
    }
}
