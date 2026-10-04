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

        rows.Add(FluentLayout.Row(Glyphs.Memory, "Show memory", "A second column beside the processor share",
            FluentLayout.Toggle(config.ShowMemory, value => { config.ShowMemory = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Processes",
            "How many the list holds; a short widget shows fewer",
            FluentLayout.Slider(config.EffectiveMaxCount, 1, 20, 1, string.Empty,
                value => { config.MaxCount = (int)Math.Round(value); save(); })));
    }
}
