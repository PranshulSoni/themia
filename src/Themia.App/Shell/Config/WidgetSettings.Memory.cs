using Microsoft.UI.Xaml;
using Themia.Core.Localization;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

internal static partial class WidgetSettings
{
    /// <summary>
    /// [S-1134] The memory meter's metrics. Each says whether a figure may appear; how big the tile
    /// is decides whether it fits, so turning one on never crowds a small tile.
    /// </summary>
    public static void Memory(List<FrameworkElement> rows, MemoryConfig memory, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header(Str.WidgetsB_Settings_Meters_Show.Text()));

        rows.Add(FluentLayout.Row(Glyphs.Gauge, Str.WidgetsB_Settings_Memory_Bar_Label.Text(),
            Str.WidgetsB_Settings_Memory_Bar_Desc.Text(),
            FluentLayout.Toggle(memory.ShowBar, value => { memory.ShowBar = value; save(); }),
            "composition", "cached"));

        rows.Add(FluentLayout.Row(Glyphs.Memory, Str.WidgetsB_Settings_Memory_Available_Label.Text(),
            Str.WidgetsB_Settings_Memory_Available_Desc.Text(),
            FluentLayout.Toggle(memory.ShowAvailable, value => { memory.ShowAvailable = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Disk, Str.WidgetsB_Settings_Memory_Swap_Label.Text(),
            Str.WidgetsB_Settings_Memory_Swap_Desc.Text(),
            FluentLayout.Toggle(memory.ShowSwap, value => { memory.ShowSwap = value; save(); }),
            "page file", "pagefile", "virtual memory"));

        rows.Add(FluentLayout.Row(Glyphs.Count, Str.WidgetsB_Settings_Memory_Committed_Label.Text(),
            Str.WidgetsB_Settings_Memory_Committed_Desc.Text(),
            FluentLayout.Toggle(memory.ShowCommitted, value => { memory.ShowCommitted = value; save(); }),
            "commit charge"));

        var count = FluentLayout.Row(Glyphs.Count, Str.WidgetsB_Settings_Meters_MostProcesses_Label.Text(),
            Str.WidgetsB_Settings_Meters_MostProcesses_Desc.Text(),
            FluentLayout.Slider(memory.EffectiveTopProcessCount, 1, MemoryConfig.MaxTopProcesses, 1, string.Empty,
                value => { memory.TopProcessCount = (int)Math.Round(value); save(); }));

        rows.Add(FluentLayout.Expander(Glyphs.Processes, Str.WidgetUi_TopProcsLabel.Text(),
            Str.WidgetsB_Settings_Memory_TopProcesses_Desc.Text(),
            FluentLayout.Toggle(memory.ShowTopProcesses, value => { memory.ShowTopProcesses = value; save(); }),
            [count]));

        MeterRows(rows, memory, save, Str.WidgetsB_Settings_Memory_Refresh.Text());
    }
}
