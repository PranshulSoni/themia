using Themia.App.Services;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets.Meters;

/// <summary>
/// Processor load across all cores. The CPU package (cores, frequency, uptime, temperature, top
/// processes) adds its extras here, as details and a list on the reading.
/// </summary>
internal sealed class CpuMeter : MeterPart
{
    public override WidgetKind Kind => WidgetKind.Cpu;

    public override MeterPressure PressureOf(MeterReading reading)
        => MeterPressures.For(PressureScale.Usage, reading.Available ? reading.Fraction : null);

    public override void Sample(MeterDemand demand, IMeterHost host) => host.Present(SystemMetrics.Cpu());
}
