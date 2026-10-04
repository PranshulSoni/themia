using Microsoft.UI.Xaml;
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

        rows.Add(FluentLayout.Header("Show"));

        rows.Add(FluentLayout.Row(Glyphs.Gauge, "Usage bar",
            "A bar under the reading; in use, cached and free on a large tile",
            FluentLayout.Toggle(memory.ShowBar, value => { memory.ShowBar = value; save(); }),
            "composition", "cached"));

        rows.Add(FluentLayout.Row(Glyphs.Memory, "Available",
            "Memory free for programs to use right now",
            FluentLayout.Toggle(memory.ShowAvailable, value => { memory.ShowAvailable = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Disk, "Swap",
            "How much of the page file is in use; hidden without one",
            FluentLayout.Toggle(memory.ShowSwap, value => { memory.ShowSwap = value; save(); }),
            "page file", "pagefile", "virtual memory"));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Committed",
            "Memory promised to programs, of the most Windows can promise",
            FluentLayout.Toggle(memory.ShowCommitted, value => { memory.ShowCommitted = value; save(); }),
            "commit charge"));

        var count = FluentLayout.Row(Glyphs.Count, "Most processes",
            "A ceiling: a shorter tile shows fewer",
            FluentLayout.Slider(memory.EffectiveTopProcessCount, 1, MemoryConfig.MaxTopProcesses, 1, string.Empty,
                value => { memory.TopProcessCount = (int)Math.Round(value); save(); }));

        rows.Add(FluentLayout.Expander(Glyphs.Processes, "Top processes",
            "The processes holding the most memory, where the tile has room",
            FluentLayout.Toggle(memory.ShowTopProcesses, value => { memory.ShowTopProcesses = value; save(); }),
            [count]));

        MeterRows(rows, memory, save, "memory is read");
    }
}
