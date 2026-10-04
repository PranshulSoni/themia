#if DEBUG
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-1186] The battery meter's gallery sample: a laptop discharging from 78% (to 60%), with a battery that
/// reports everything (health, rate, cycles, voltage, chemistry, maker), so a machine without a
/// battery can still see the populated layout at every size.
/// <para>
/// Read only by a battery meter that is a preview (<see cref="Widgets.IWidgetSurface.IsPreview"/>),
/// so a desktop widget in the same Debug process never sees a made-up battery. The charge drops a
/// point every few samples so the chart has a slope to draw. Debug builds only.
/// </para>
/// </summary>
internal static class BatteryFixture
{
    private static int _calls;

    /// <summary>The device figures preview tiles read instead of the machine's, or null for the machine's.</summary>
    public static BatteryPack? Pack { get; private set; }

    public static void Install(bool populated)
    {
        _calls = 0;
        Pack = populated
            ? new BatteryPack(
                Relative: false,
                DesignCapacity: 57_000,
                FullCapacity: 51_870,
                RemainingCapacity: 40_460,
                Rate: -8_400,
                VoltageMillivolts: 12_380,
                Cycles: 212,
                TemperatureCelsius: 31,
                Chemistry: "Li-ion",
                Manufacturer: "Sunwoda",
                Count: 1)
            : null;
    }

    /// <summary>The power status preview tiles read, or null when no fixture is installed.</summary>
    public static BatteryPower? Power()
    {
        if (Pack is null)
        {
            return null;
        }

        var percent = Math.Max(60, 78 - (_calls++ / 4));
        return new BatteryPower(HasBattery: true, Percent: percent, OnMains: false, Charging: false, Remaining: TimeSpan.FromSeconds(percent * 160));
    }

    /// <summary>Sampled every second over a 30-second span, so a short gallery wait draws a real chart.</summary>
    public static BatteryConfig Config() => new() { IntervalSeconds = 1, GraphSpanSeconds = 30, ShowTemperature = true };
}
#endif
