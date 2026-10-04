using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.Core.Localization;
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

        rows.Add(FluentLayout.Header(Str.WidgetUi_Processes.Text()));

        rows.Add(FluentLayout.Row(Glyphs.Sort, Str.WidgetsC_Processes_Settings_RankBy_Label.Text(), Str.WidgetsC_Processes_Settings_RankBy_Desc.Text(),
            FluentLayout.Choice(
                [(ProcessSort.Cpu, Str.WidgetsC_Processes_Settings_Processor.Text()), (ProcessSort.Memory, Str.WidgetsC_Processes_Col_Memory.Text())],
                config.Sort,
                value => { config.Sort = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, Str.WidgetsC_Processes_Settings_Most_Label.Text(),
            Str.WidgetsC_Processes_Settings_Most_Desc.Text(),
            FluentLayout.Slider(config.EffectiveMaxCount, 1, ProcessesConfig.MaxRows, 1, string.Empty,
                value => { config.MaxCount = (int)Math.Round(value); save(); }),
            "rows", "count"));

        // [S-1155] Columns are offered, not forced: each appears only where the row is wide enough.
        rows.Add(FluentLayout.Header(Str.WidgetsC_Processes_Settings_Columns.Text()));

        rows.Add(FluentLayout.Row(Glyphs.Memory, Str.WidgetsC_Processes_Col_Memory.Text(), Str.WidgetsC_Processes_Settings_Memory_Desc.Text(),
            FluentLayout.Toggle(config.ShowMemory, value => { config.ShowMemory = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Processes, Str.WidgetsC_Processes_Col_Threads.Text(), Str.WidgetsC_Processes_Settings_Threads_Desc.Text(),
            FluentLayout.Toggle(config.ShowThreads, value => { config.ShowThreads = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Tag, Str.WidgetsC_Processes_Settings_Pid_Label.Text(), Str.WidgetsC_Processes_Settings_Pid_Desc.Text(),
            FluentLayout.Toggle(config.ShowPid, value => { config.ShowPid = value; save(); }),
            "pid"));

        rows.Add(FluentLayout.Row(Glyphs.Info, Str.WidgetsC_Processes_Settings_Totals_Label.Text(), Str.WidgetsC_Processes_Settings_Totals_Desc.Text(),
            FluentLayout.Toggle(config.ShowTotals, value => { config.ShowTotals = value; save(); }),
            "footer", "count"));
    }
}
