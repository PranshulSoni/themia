using Microsoft.UI.Xaml;
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

        rows.Add(FluentLayout.Header("Show"));

        rows.Add(FluentLayout.Row(Glyphs.Lightning, "Speed",
            "The clock the processor is running at, beside the load",
            FluentLayout.Toggle(cpu.ShowFrequency, value => { cpu.ShowFrequency = value; save(); }),
            "frequency", "clock", "GHz"));

        rows.Add(FluentLayout.Row(Glyphs.Grid, "Cores",
            "A bar for each logical processor",
            FluentLayout.Toggle(cpu.ShowCores, value => { cpu.ShowCores = value; save(); }),
            "per-core", "threads", "logical processors"));

        rows.Add(FluentLayout.Row(Glyphs.Hours, "Uptime",
            "How long since Windows started",
            FluentLayout.Toggle(cpu.ShowUptime, value => { cpu.ShowUptime = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Processes",
            "How many processes, threads and handles are open",
            FluentLayout.Toggle(cpu.ShowProcessCount, value => { cpu.ShowProcessCount = value; save(); }),
            "threads", "handles"));

        var count = FluentLayout.Row(Glyphs.Count, "Most processes",
            "A ceiling: a shorter tile shows fewer",
            FluentLayout.Slider(cpu.EffectiveTopProcessCount, 1, CpuConfig.MaxTopProcesses, 1, string.Empty,
                value => { cpu.TopProcessCount = (int)Math.Round(value); save(); }));

        rows.Add(FluentLayout.Expander(Glyphs.Processes, "Top processes",
            "The processes using the most processor time, where the tile has room",
            FluentLayout.Toggle(cpu.ShowTopProcesses, value => { cpu.ShowTopProcesses = value; save(); }),
            [count]));

        rows.Add(FluentLayout.Header("Sensors"));

        rows.Add(FluentLayout.Row(Glyphs.Thermometer, "Temperature",
            "From the ACPI thermal zone, or LibreHardwareMonitor with its web server on (port 8085); N/A when neither answers",
            FluentLayout.Toggle(cpu.ShowTemperature, value => { cpu.ShowTemperature = value; save(); }),
            "temp", "heat", "LibreHardwareMonitor", "sensor"));

        rows.Add(FluentLayout.Row(Glyphs.Wind, "Fan",
            "Needs LibreHardwareMonitor running with its web server on (port 8085); Windows has no fan reading of its own",
            FluentLayout.Toggle(cpu.ShowFan, value => { cpu.ShowFan = value; save(); }),
            "RPM", "cooling", "LibreHardwareMonitor", "sensor"));

        MeterRows(rows, cpu, save, "the load is read");
    }
}
