#if DEBUG
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-1186] The battery meter's gallery sample: a laptop discharging a point a minute, at 78% when
/// the page is drawn, so a machine without a battery can still see the populated layout at every
/// size. A function of a second on <see cref="MeterFixture"/>'s clock, so the history before the
/// page is filled from it too ([S-1823]).
/// <para>
/// Read only by a battery meter that is a preview (<see cref="Widgets.IWidgetSurface.IsPreview"/>),
/// so a desktop widget in the same Debug process never sees a made-up battery. Debug builds only.
/// </para>
/// </summary>
internal static class BatteryFixture
{
    /// <summary>Whether the sample is installed: preview tiles read it instead of the machine.</summary>
    public static bool Installed { get; private set; }

    public static void Install(bool populated) => Installed = populated;

    /// <summary>The power status at <paramref name="t"/> seconds.</summary>
    public static BatteryPower Power(double t)
    {
        var percent = (int)Math.Clamp(Math.Round(78 - (t / 60)), 5, 100);
        return new BatteryPower(HasBattery: true, Percent: percent, OnMains: false, Charging: false, Remaining: TimeSpan.FromSeconds(percent * 160));
    }

    /// <summary>The meter's own defaults: every 15 s over five minutes.</summary>
    public static BatteryConfig Config() => new();
}
#endif
