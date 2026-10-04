namespace Themia.Core.Widgets;

/// <summary>
/// [S-1124] What the top of a meter's chart stands for.
/// <para>
/// A share (CPU, memory, a drive, a battery) has a natural top, 100%, and the chart is drawn
/// against it so a steady 47% looks steady. A rate or a latency has none: it is drawn against the
/// highest sample <i>in the window on screen</i>, never below <see cref="Floor"/>. Not against the
/// highest sample ever seen — one download an hour ago would otherwise flatten the trace for the
/// rest of the session, which is how the first port drew network throughput.
/// </para>
/// </summary>
/// <param name="FollowsWindow">True for a rate or latency: the top is the window's peak.</param>
/// <param name="Floor">The lowest the top may be (for a share, the top itself).</param>
public readonly record struct ChartScale(bool FollowsWindow, double Floor)
{
    /// <summary>A share, 0..1, drawn against 100%.</summary>
    public static ChartScale Share { get; } = new(false, 1);

    /// <summary>Drawn against the window's peak, but never a top lower than <paramref name="floor"/>, so idle jitter stays flat.</summary>
    public static ChartScale Peak(double floor) => new(true, floor > 0 && double.IsFinite(floor) ? floor : 1);

    /// <summary>The value the top of the chart stands for, over one or two series' visible samples.</summary>
    public double Ceiling(ReadOnlySpan<double> first, ReadOnlySpan<double> second = default)
    {
        if (!FollowsWindow)
        {
            return Floor;
        }

        var top = Floor;

        foreach (var value in first)
        {
            if (double.IsFinite(value) && value > top)
            {
                top = value;
            }
        }

        foreach (var value in second)
        {
            if (double.IsFinite(value) && value > top)
            {
                top = value;
            }
        }

        return top;
    }

    /// <summary>Where <paramref name="value"/> sits under <paramref name="ceiling"/>, 0..1; a gap stays a gap (NaN).</summary>
    public static double Fraction(double value, double ceiling)
        => !double.IsFinite(value) ? double.NaN : ceiling > 0 ? Math.Clamp(value / ceiling, 0, 1) : 0;
}
