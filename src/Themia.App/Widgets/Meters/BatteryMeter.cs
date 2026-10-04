using Themia.App.Services;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets.Meters;

/// <summary>
/// Charge level. The battery package (health, cycles, voltage, rate, exact status) adds its extras
/// here. Its history is drawn like any other meter's when the graph is on ([S-1126] — the first port
/// ignored the setting for this kind).
/// </summary>
internal sealed class BatteryMeter : MeterPart
{
    private bool _showTimeRemaining = true;
    private bool _charging;

    public override WidgetKind Kind => WidgetKind.Battery;

    public override bool Moves => false;

    public override bool CanBeAbsent => true;

    public override MeterPressure PressureOf(MeterReading reading)
        => MeterPressures.For(PressureScale.Charge, reading.Available ? reading.Fraction : null, _charging);

    public override bool ApplyConfig(WidgetConfig config, IMeterHost host)
    {
        _showTimeRemaining = (config as BatteryConfig)?.ShowTimeRemaining ?? true;
        return false;
    }

    public override void Sample(MeterDemand demand, IMeterHost host)
    {
        var reading = SystemMetrics.Battery(_showTimeRemaining, out var charging);
        _charging = charging;
        host.Present(reading);
    }
}
