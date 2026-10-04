using Microsoft.UI.Xaml;
using Themia.Core.Localization;
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

        rows.Add(FluentLayout.Header(Str.WidgetsB_Settings_Meters_Show.Text()));

        rows.Add(FluentLayout.Row(Glyphs.Timer, Str.WidgetsB_Settings_Battery_TimeRemaining_Label.Text(),
            Str.WidgetsB_Settings_Battery_TimeRemaining_Desc.Text(),
            FluentLayout.Toggle(battery.ShowTimeRemaining, value => { battery.ShowTimeRemaining = value; save(); }),
            "eta", "time to full", "time to empty"));

        rows.Add(FluentLayout.Row(Glyphs.Battery, Str.WidgetsB_Settings_Battery_Health_Label.Text(),
            Str.WidgetsB_Settings_Battery_Health_Desc.Text(),
            FluentLayout.Toggle(battery.ShowHealth, value => { battery.ShowHealth = value; save(); }),
            "wear", "design capacity", "full charge capacity"));

        rows.Add(FluentLayout.Row(Glyphs.Lightning, Str.WidgetsB_Settings_Battery_Rate_Label.Text(),
            Str.WidgetsB_Settings_Battery_Rate_Desc.Text(),
            FluentLayout.Toggle(battery.ShowRate, value => { battery.ShowRate = value; save(); }),
            "watts", "power draw"));

        rows.Add(FluentLayout.Row(Glyphs.Refresh, Str.WidgetsB_Settings_Battery_Cycles_Label.Text(),
            Str.WidgetsB_Settings_Battery_Cycles_Desc.Text(),
            FluentLayout.Toggle(battery.ShowCycles, value => { battery.ShowCycles = value; save(); }),
            "cycles"));

        rows.Add(FluentLayout.Row(Glyphs.Gauge, Str.WidgetsB_Settings_Battery_Voltage_Label.Text(),
            Str.WidgetsB_Settings_Battery_Voltage_Desc.Text(),
            FluentLayout.Toggle(battery.ShowVoltage, value => { battery.ShowVoltage = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Thermometer, Str.WidgetsB_Settings_Battery_Temperature_Label.Text(),
            Str.WidgetsB_Settings_Battery_Temperature_Desc.Text(),
            FluentLayout.Toggle(battery.ShowTemperature, value => { battery.ShowTemperature = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Power, Str.WidgetsB_Settings_Battery_Power_Label.Text(),
            Str.WidgetsB_Settings_Battery_Power_Desc.Text(),
            FluentLayout.Toggle(battery.ShowPower, value => { battery.ShowPower = value; save(); }),
            "power plan", "battery saver", "plugged in"));

        MeterRows(rows, battery, save, Str.WidgetsB_Settings_Battery_Refresh.Text());
    }
}
