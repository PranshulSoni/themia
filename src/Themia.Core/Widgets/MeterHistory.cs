using System.Globalization;

namespace Themia.Core.Widgets;

/// <summary>What a meter's raw value is measured in, so its history can be summarised in the reading's own terms.</summary>
public enum MeterUnit
{
    /// <summary>A share of a whole, 0..1, shown as a percentage (CPU, memory, disk, battery).</summary>
    Percent = 0,

    /// <summary>Bytes per second (network).</summary>
    Rate = 1,

    /// <summary>Milliseconds (ping).</summary>
    Latency = 2,
}

/// <summary>Lowest, mean and highest of a run of samples.</summary>
public readonly record struct MeterSummary(double Min, double Average, double Max);

/// <summary>
/// [S-664] The parts of a meter that only a larger tile shows: the minimum / average / maximum over
/// the samples the chart is showing, and the words for the span it covers. How many samples that
/// is comes from the meter's span setting (<see cref="MeterTiming"/>).
/// </summary>
public static class MeterHistory
{
    /// <summary>The lowest, mean and highest of <paramref name="samples"/>, or null when there are none.</summary>
    public static MeterSummary? Summarise(ReadOnlySpan<double> samples)
    {
        var count = 0;
        var sum = 0.0;
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;

        foreach (var sample in samples)
        {
            // A sample that is not a number is a gap in the reading, not a zero.
            if (!double.IsFinite(sample))
            {
                continue;
            }

            count++;
            sum += sample;
            min = Math.Min(min, sample);
            max = Math.Max(max, sample);
        }

        return count == 0 ? null : new MeterSummary(min, sum / count, max);
    }

    /// <summary>One value in the unit the meter reads in.</summary>
    public static string Format(MeterUnit unit, double value) => unit switch
    {
        MeterUnit.Rate => Formatting.Rate(value),
        MeterUnit.Latency => Formatting.Latency(value),
        _ => Formatting.Percent(value),
    };

    /// <summary>
    /// "min 3% · avg 12% · max 40%", or in <paramref name="compact"/> form "avg 12% · max 40%" (the
    /// minimum is the least informative of the three on a busy meter); empty when there is nothing
    /// to summarise.
    /// </summary>
    public static string SummaryLine(MeterUnit unit, MeterSummary? summary, bool compact = false)
    {
        if (summary is not { } s)
        {
            return string.Empty;
        }

        var tail = $"avg {Format(unit, s.Average)} · max {Format(unit, s.Max)}";
        return compact ? tail : $"min {Format(unit, s.Min)} · {tail}";
    }

    /// <summary>
    /// How many characters the summary line can take for a unit, at its widest. Layout decides on
    /// this rather than on the live text, so the line does not switch form from one sample to the
    /// next as "9%" becomes "10%".
    /// </summary>
    public static int SummaryCharacters(MeterUnit unit, bool compact)
    {
        var widest = Widest(unit);
        return SummaryLine(unit, new MeterSummary(widest, widest, widest), compact).Length;
    }

    /// <summary>How many characters one formatted value can take, at its widest.</summary>
    public static int ValueCharacters(MeterUnit unit) => Format(unit, Widest(unit)).Length;

    /// <summary>The value whose formatting is longest for a unit.</summary>
    private static double Widest(MeterUnit unit) => unit switch
    {
        MeterUnit.Rate => 1000 * 1024,   // "1000 KB/s"
        MeterUnit.Latency => 999,        // "999 ms"
        _ => 1,                          // "100%"
    };

    /// <summary>
    /// What a window of <paramref name="samples"/> taken every <paramref name="interval"/> spans,
    /// in words: "last 45 s", "last 2 min", "last 1 h".
    /// </summary>
    public static string Span(int samples, TimeSpan interval)
    {
        if (samples <= 0 || interval <= TimeSpan.Zero)
        {
            return string.Empty;
        }

        var span = TimeSpan.FromTicks(interval.Ticks * samples);

        if (span.TotalSeconds < 90)
        {
            return $"last {Math.Round(span.TotalSeconds).ToString("F0", CultureInfo.InvariantCulture)} s";
        }

        if (span.TotalMinutes < 90)
        {
            return $"last {Math.Round(span.TotalMinutes).ToString("F0", CultureInfo.InvariantCulture)} min";
        }

        return $"last {Math.Round(span.TotalHours).ToString("F0", CultureInfo.InvariantCulture)} h";
    }
}
