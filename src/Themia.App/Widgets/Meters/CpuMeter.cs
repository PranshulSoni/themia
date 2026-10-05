using Themia.App.Services;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets.Meters;

/// <summary>[S-1140] Processor load across all cores, coloured by how busy the machine is.</summary>
internal sealed class CpuMeter : MeterPart
{
    public override WidgetKind Kind => WidgetKind.Cpu;

    public override MeterPressure PressureOf(MeterReading reading)
        => MeterPressures.For(PressureScale.Usage, reading.Available ? reading.Fraction : null);

    public override void Sample(IMeterHost host) => host.Present(SystemMetrics.Cpu());

#if DEBUG
    public override bool SampleFixture(IMeterHost host, double seconds)
    {
        if (!Preview || !Shell.Gallery.MeterFixture.Has(Kind))
        {
            return false;
        }

        var load = Shell.Gallery.MeterFixture.Cpu(seconds);
        host.Present(new MeterReading(load, Formatting.Percent(load), true));
        return true;
    }
#endif
}
