using Themia.App.Services;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets.Meters;

/// <summary>
/// Space used on a drive. The disk package (several drives, read / write rates as a second series,
/// open in Explorer) adds its extras here.
/// </summary>
internal sealed class DiskMeter : MeterPart
{
    private string _drive = string.Empty;

    public override WidgetKind Kind => WidgetKind.Disk;

    /// <summary>Space used barely moves: its tiles carry used / free / total instead of min / avg / max.</summary>
    public override bool Moves => false;

    public override MeterPressure PressureOf(MeterReading reading)
        => MeterPressures.For(PressureScale.Storage, reading.Available ? reading.Fraction : null);

    public override bool ApplyConfig(WidgetConfig config, IMeterHost host)
    {
        _drive = (config as DiskConfig)?.Drive ?? string.Empty;
        return false;
    }

    public override void Sample(MeterDemand demand, IMeterHost host) => host.Present(SystemMetrics.Disk(_drive));
}
