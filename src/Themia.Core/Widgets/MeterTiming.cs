namespace Themia.Core.Widgets;

/// <summary>
/// [S-1120] [S-1121] How often a meter samples, and how much history its chart spans — the two
/// settings every meter has (the original's <c>refreshInterval.ts</c> and <c>graphSpan.ts</c>).
/// <para>
/// The span is a duration, not a sample count: the number of samples the chart holds follows from
/// it and the refresh interval, so "the last 5 minutes" stays the last 5 minutes when the user
/// slows the meter down. A chart narrower than its sample count in slots draws only the newest
/// samples it has room for, which keeps a tiny tile from drawing a smear of sub-pixel points.
/// </para>
/// </summary>
public static class MeterTiming
{
    /// <summary>The spans the settings offer, in seconds. A closed set, so the choice row can never show a blank.</summary>
    public static IReadOnlyList<int> SpanChoices { get; } = [30, 60, 120, 180, 300, 600, 1200, 1800, 3600];

    /// <summary>
    /// The most samples one series holds. At 1 h sampled every second the chart would otherwise
    /// rebuild a 3600-point line on every tick; at the cap it shows the newest 600 instead.
    /// </summary>
    public const int MaxCapacity = 600;

    /// <summary>Fastest refresh any meter offers, in seconds.</summary>
    public const int MinIntervalSeconds = 1;

    /// <summary>Slowest refresh a local meter offers, in seconds. Ping, which goes over the network, allows longer.</summary>
    public const int MaxIntervalSeconds = 60;

    /// <summary>
    /// The narrowest slot one sample gets on a chart, in DIPs. Under it neighbouring points land on
    /// the same pixel column and the line is noise, not history.
    /// </summary>
    public const double MinSlotWidth = 2;

    /// <summary>How many samples a <paramref name="spanSeconds"/> window holds when sampled every <paramref name="intervalSeconds"/>: 2..<see cref="MaxCapacity"/>.</summary>
    public static int Capacity(int spanSeconds, int intervalSeconds)
    {
        // Junk from a hand-edited file falls back rather than reaching a division.
        var span = spanSeconds > 0 ? spanSeconds : 60;
        var interval = intervalSeconds > 0 ? intervalSeconds : 1;
        return Math.Clamp((int)Math.Round((double)span / interval, MidpointRounding.AwayFromZero), 2, MaxCapacity);
    }

    /// <summary>
    /// How many of a chart's <paramref name="capacity"/> samples it draws at <paramref name="chartWidth"/>
    /// DIPs: all of them, unless that would put two in one <see cref="MinSlotWidth"/>. An unmeasured
    /// chart draws the whole capacity.
    /// </summary>
    public static int Window(int capacity, double chartWidth)
    {
        var cap = Math.Clamp(capacity, 2, MaxCapacity);

        if (!double.IsFinite(chartWidth) || chartWidth <= 0)
        {
            return cap;
        }

        return Math.Clamp((int)Math.Floor(chartWidth / MinSlotWidth), 2, cap);
    }

    /// <summary>The nearest offered span to a stored one, so a value outside the set still lands on a real choice.</summary>
    public static int NearestSpan(int seconds)
    {
        var best = SpanChoices[0];

        foreach (var choice in SpanChoices)
        {
            if (Math.Abs(choice - seconds) < Math.Abs(best - seconds))
            {
                best = choice;
            }
        }

        return best;
    }

    /// <summary>A span in words, for the choice row: "30 seconds", "1 minute", "20 minutes", "1 hour".</summary>
    public static string SpanLabel(int seconds) => seconds switch
    {
        < 60 => $"{seconds} seconds",
        60 => "1 minute",
        3600 => "1 hour",
        _ when seconds % 3600 == 0 => $"{seconds / 3600} hours",
        _ => $"{Math.Round(seconds / 60.0)} minutes",
    };
}
