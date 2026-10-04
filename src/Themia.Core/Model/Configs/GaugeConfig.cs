using System.Text.Json.Serialization;
using Themia.Core.Widgets;

namespace Themia.Core.Model;

/// <summary>
/// Settings every meter shares: whether it draws its history, how often it samples, and how much
/// history the chart spans. Each kind's own options live in its own file beside this one.
/// </summary>
public abstract class GaugeConfig : WidgetConfig
{
    /// <param name="intervalSeconds">This kind's default refresh: what the number actually does.</param>
    /// <param name="graphSpanSeconds">This kind's default span, one of <see cref="MeterTiming.SpanChoices"/>.</param>
    protected GaugeConfig(int intervalSeconds, int graphSpanSeconds)
    {
        IntervalSeconds = intervalSeconds;
        GraphSpanSeconds = graphSpanSeconds;
    }

    /// <summary>Show the recent history behind the readout.</summary>
    public bool ShowGraph { get; set; } = true;

    /// <summary>[S-1120] Seconds between samples, 1 to <see cref="MaxIntervalSeconds"/>.</summary>
    public int IntervalSeconds { get; set; }

    /// <summary>[S-1121] How much history the chart spans, in seconds: one of <see cref="MeterTiming.SpanChoices"/>.</summary>
    public int GraphSpanSeconds { get; set; }

    /// <summary>The slowest refresh this kind offers. A local read tops out at a minute.</summary>
    [JsonIgnore]
    public virtual int MaxIntervalSeconds => MeterTiming.MaxIntervalSeconds;

    /// <summary>The refresh actually used: clamped on read too, so a value set in code cannot spin the tick.</summary>
    [JsonIgnore]
    public int EffectiveIntervalSeconds => Math.Clamp(IntervalSeconds, MeterTiming.MinIntervalSeconds, MaxIntervalSeconds);

    [JsonIgnore]
    public TimeSpan TickInterval => TimeSpan.FromSeconds(EffectiveIntervalSeconds);

    /// <summary>[S-1121] How many samples the chart holds: the span over the refresh, 2..600.</summary>
    [JsonIgnore]
    public int GraphCapacity => MeterTiming.Capacity(GraphSpanSeconds, EffectiveIntervalSeconds);

    protected void CopyTo(GaugeConfig other)
    {
        other.ShowGraph = ShowGraph;
        other.IntervalSeconds = IntervalSeconds;
        other.GraphSpanSeconds = GraphSpanSeconds;
    }

    /// <summary>
    /// Clamp the refresh and snap the span to an offered choice. A kind that adds its own repairs
    /// overrides this and appends to what it returns.
    /// </summary>
    public override IReadOnlyList<string> Repair()
    {
        var notes = new List<string>();

        if (EffectiveIntervalSeconds != IntervalSeconds)
        {
            notes.Add($"{Kind}: refresh {IntervalSeconds} s is outside {MeterTiming.MinIntervalSeconds}..{MaxIntervalSeconds}; using {EffectiveIntervalSeconds}");
            IntervalSeconds = EffectiveIntervalSeconds;
        }

        var span = MeterTiming.NearestSpan(GraphSpanSeconds);
        if (span != GraphSpanSeconds)
        {
            notes.Add($"{Kind}: graph span {GraphSpanSeconds} s is not an offered span; using {span}");
            GraphSpanSeconds = span;
        }

        return notes;
    }
}
