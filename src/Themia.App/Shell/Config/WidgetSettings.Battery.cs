using Microsoft.UI.Xaml;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

internal static partial class WidgetSettings
{
    /// <summary>
    /// [S-1183] The battery meter's figures. Each says whether a figure may appear; how big the tile
    /// is decides whether it fits, and a figure the battery does not report hides on its own.
    /// </summary>
    public static void Battery(List<FrameworkElement> rows, BatteryConfig battery, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(battery);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Show"));

        rows.Add(FluentLayout.Row(Glyphs.Timer, "Time remaining",
            "Windows' estimate while on battery; time to full while charging",
            FluentLayout.Toggle(battery.ShowTimeRemaining, value => { battery.ShowTimeRemaining = value; save(); }),
            "eta", "time to full", "time to empty"));

        rows.Add(FluentLayout.Row(Glyphs.Battery, "Health",
            "How much the battery holds now against what it held new",
            FluentLayout.Toggle(battery.ShowHealth, value => { battery.ShowHealth = value; save(); }),
            "wear", "design capacity", "full charge capacity"));

        rows.Add(FluentLayout.Row(Glyphs.Lightning, "Charge rate",
            "How fast the battery is charging or draining, in watts",
            FluentLayout.Toggle(battery.ShowRate, value => { battery.ShowRate = value; save(); }),
            "watts", "power draw"));

        rows.Add(FluentLayout.Row(Glyphs.Refresh, "Cycle count",
            "Charge cycles, where the battery counts them",
            FluentLayout.Toggle(battery.ShowCycles, value => { battery.ShowCycles = value; save(); }),
            "cycles"));

        rows.Add(FluentLayout.Row(Glyphs.Gauge, "Voltage",
            "The battery's voltage right now",
            FluentLayout.Toggle(battery.ShowVoltage, value => { battery.ShowVoltage = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Thermometer, "Temperature",
            "Most batteries have no sensor; shows N/A then",
            FluentLayout.Toggle(battery.ShowTemperature, value => { battery.ShowTemperature = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Power, "Power source and plan",
            "Plugged in or on battery, the power plan and battery saver",
            FluentLayout.Toggle(battery.ShowPower, value => { battery.ShowPower = value; save(); }),
            "power plan", "battery saver", "plugged in"));

        MeterRows(rows, battery, save, "the charge is read");
    }
}
