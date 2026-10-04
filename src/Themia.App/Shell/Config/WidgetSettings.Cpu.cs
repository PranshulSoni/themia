using Microsoft.UI.Xaml;
using Themia.Core.Localization;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

internal static partial class WidgetSettings
{
    /// <summary>
    /// [S-1149] The processor meter's metrics. Each says whether a figure may appear; how big the
    /// tile is decides whether it fits, so turning one on never crowds a small tile.
    /// </summary>
    public static void Cpu(List<FrameworkElement> rows, CpuConfig cpu, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(cpu);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header(Str.WidgetsB_Settings_Meters_Show.Text()));

        rows.Add(FluentLayout.Row(Glyphs.Lightning, Str.WidgetsB_Settings_Cpu_Speed_Label.Text(),
            Str.WidgetsB_Settings_Cpu_Speed_Desc.Text(),
            FluentLayout.Toggle(cpu.ShowFrequency, value => { cpu.ShowFrequency = value; save(); }),
            "frequency", "clock", "GHz"));

        rows.Add(FluentLayout.Row(Glyphs.Grid, Str.WidgetsB_Settings_Cpu_Cores_Label.Text(),
            Str.WidgetsB_Settings_Cpu_Cores_Desc.Text(),
            FluentLayout.Toggle(cpu.ShowCores, value => { cpu.ShowCores = value; save(); }),
            "per-core", "threads", "logical processors"));

        rows.Add(FluentLayout.Row(Glyphs.Hours, Str.WidgetsB_Settings_Cpu_Uptime_Label.Text(),
            Str.WidgetsB_Settings_Cpu_Uptime_Desc.Text(),
            FluentLayout.Toggle(cpu.ShowUptime, value => { cpu.ShowUptime = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, Str.WidgetsB_Settings_Cpu_Processes_Label.Text(),
            Str.WidgetsB_Settings_Cpu_Processes_Desc.Text(),
            FluentLayout.Toggle(cpu.ShowProcessCount, value => { cpu.ShowProcessCount = value; save(); }),
            "threads", "handles"));

        var count = FluentLayout.Row(Glyphs.Count, Str.WidgetsB_Settings_Meters_MostProcesses_Label.Text(),
            Str.WidgetsB_Settings_Meters_MostProcesses_Desc.Text(),
            FluentLayout.Slider(cpu.EffectiveTopProcessCount, 1, CpuConfig.MaxTopProcesses, 1, string.Empty,
                value => { cpu.TopProcessCount = (int)Math.Round(value); save(); }));

        rows.Add(FluentLayout.Expander(Glyphs.Processes, Str.WidgetUi_TopProcsLabel.Text(),
            Str.WidgetsB_Settings_Cpu_TopProcesses_Desc.Text(),
            FluentLayout.Toggle(cpu.ShowTopProcesses, value => { cpu.ShowTopProcesses = value; save(); }),
            [count]));

        rows.Add(FluentLayout.Header(Str.WidgetsB_Settings_Cpu_Sensors.Text()));

        rows.Add(FluentLayout.Row(Glyphs.Thermometer, Str.WidgetsB_Settings_Cpu_Temperature_Label.Text(),
            Str.WidgetsB_Settings_Cpu_Temperature_Desc.Text(),
            FluentLayout.Toggle(cpu.ShowTemperature, value => { cpu.ShowTemperature = value; save(); }),
            "temp", "heat", "LibreHardwareMonitor", "sensor"));

        rows.Add(FluentLayout.Row(Glyphs.Wind, Str.WidgetsB_Settings_Cpu_Fan_Label.Text(),
            Str.WidgetsB_Settings_Cpu_Fan_Desc.Text(),
            FluentLayout.Toggle(cpu.ShowFan, value => { cpu.ShowFan = value; save(); }),
            "RPM", "cooling", "LibreHardwareMonitor", "sensor"));

        MeterRows(rows, cpu, save, Str.WidgetsB_Settings_Cpu_Refresh.Text());
    }
}
