using Themia.App.Services;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets.Meters;

/// <summary>
/// Throughput across the active adapters. The network package (upload as the second series,
/// per-interface rows, session totals, public address) adds its extras here.
/// </summary>
internal sealed class NetworkMeter : MeterPart
{
    /// <summary>The lowest top the chart may have: 1 KB/s, so idle chatter stays a flat line (the original's floor).</summary>
    private const double ChartFloor = 1024;

    public override WidgetKind Kind => WidgetKind.Network;

    public override MeterUnit Unit => MeterUnit.Rate;

    /// <summary>"1.2 MB/s".</summary>
    public override double ReadingCharacters => 8;

    /// <summary>"up 105 KB/s" changes length from second to second, so it is placed at its worst case.</summary>
    public override double CaptionCharacters(string live) => Math.Max(11, live.Length);

    /// <summary>[S-1124] Against the visible window's peak, never the session's.</summary>
    public override ChartScale Scale => ChartScale.Peak(ChartFloor);

    public override void Sample(MeterDemand demand, IMeterHost host) => host.Present(NetworkMetrics.Network());
}
