using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>
/// Settings rows for the widgets that read the machine itself: the process list and the ping meter.
/// <para>
/// Same shape as the rest of <see cref="WidgetSettings"/> — each control writes straight to the
/// live config and saves, because the widget is on the desktop behind the window and watching it
/// change is the point.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    public static void Processes(List<FrameworkElement> rows, ProcessesConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Processes"));

        rows.Add(FluentLayout.Row(Glyphs.Sort, "Rank by", "Which column the list is ordered on",
            FluentLayout.Choice(
                [(ProcessSort.Cpu, "Processor"), (ProcessSort.Memory, "Memory")],
                config.Sort,
                value => { config.Sort = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Most processes",
            "A ceiling: the widget shows as many whole rows as fit, up to this",
            FluentLayout.Slider(config.EffectiveMaxCount, 1, ProcessesConfig.MaxRows, 1, string.Empty,
                value => { config.MaxCount = (int)Math.Round(value); save(); }),
            "rows", "count"));

        // [S-1155] Columns are offered, not forced: each appears only where the row is wide enough.
        rows.Add(FluentLayout.Header("Columns"));

        rows.Add(FluentLayout.Row(Glyphs.Memory, "Memory", "The memory each process holds, beside its processor share",
            FluentLayout.Toggle(config.ShowMemory, value => { config.ShowMemory = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Processes, "Threads", "How many threads each process runs, on a wide widget",
            FluentLayout.Toggle(config.ShowThreads, value => { config.ShowThreads = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Tag, "Process ID", "Each process's id, on a wide widget",
            FluentLayout.Toggle(config.ShowPid, value => { config.ShowPid = value; save(); }),
            "pid"));

        rows.Add(FluentLayout.Row(Glyphs.Info, "Totals", "How many processes and threads are running, under a tall list",
            FluentLayout.Toggle(config.ShowTotals, value => { config.ShowTotals = value; save(); }),
            "footer", "count"));
    }
}
