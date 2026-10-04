using Microsoft.UI.Xaml;
using Themia.App.Services;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>The GPU meter's page: which adapters, what it shows, then the history and refresh every meter has.</summary>
internal static partial class WidgetSettings
{
    public static void Gpu(List<FrameworkElement> rows, GpuConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header(Str.WidgetsB_Settings_Gpu_Gpu.Text()));

        // [S-433] One switch per adapter, offered only when there is a choice to make. The picker
        // reflects the raw selection: unticking the last adapter leaves it empty, and the widget
        // falls back to the primary adapter on its own.
        var adapters = GpuSampler.Adapters();
        if (adapters.Count > 1)
        {
            foreach (var adapter in adapters)
            {
                var index = adapter.Index;
                var selected = GpuRules.SelectedAdapters(config.Adapters).Contains(index);

                rows.Add(FluentLayout.Row(Glyphs.Gpu, adapter.Name,
                    adapter.DedicatedBytes > 0 ? Str.WidgetsB_Settings_Gpu_OwnMemory.Format(("size", Formatting.Bytes(adapter.DedicatedBytes))) : Str.WidgetsB_Settings_Gpu_SharesMemory.Text(),
                    FluentLayout.Toggle(selected, on =>
                    {
                        var list = GpuRules.SelectedAdapters(config.Adapters);
                        list.Remove(index);

                        if (on)
                        {
                            list.Add(index);
                        }

                        config.Adapters = list;
                        save();
                    })));
            }

            rows.Add(FluentLayout.Note(Str.WidgetsB_Settings_Gpu_PanelsNote.Text()));
        }

        rows.Add(FluentLayout.Row(Glyphs.Memory, Str.WidgetsB_Settings_Gpu_Dedicated_Label.Text(), Str.WidgetsB_Settings_Gpu_Dedicated_Desc.Text(),
            FluentLayout.Toggle(config.ShowDedicated, value => { config.ShowDedicated = value; save(); }),
            "vram", "video memory"));

        rows.Add(FluentLayout.Row(Glyphs.Memory, Str.WidgetsB_Settings_Gpu_Shared_Label.Text(), Str.WidgetsB_Settings_Gpu_Shared_Desc.Text(),
            FluentLayout.Toggle(config.ShowShared, value => { config.ShowShared = value; save(); })));

        // [S-1203] Video decode and encode are engines like the others, so they are in this list.
        rows.Add(FluentLayout.Row(Glyphs.Group, Str.WidgetsB_Settings_Gpu_Engines_Label.Text(), Str.WidgetsB_Settings_Gpu_Engines_Desc.Text(),
            FluentLayout.Toggle(config.ShowEngines, value => { config.ShowEngines = value; save(); }),
            "encoder", "decoder", "video engines"));

        rows.Add(FluentLayout.Row(Glyphs.Apps, Str.WidgetUi_TopProcsLabel.Text(), Str.WidgetsB_Settings_Gpu_TopProcesses_Desc.Text(),
            FluentLayout.Toggle(config.ShowProcesses, value => { config.ShowProcesses = value; save(); }),
            "apps", "per process"));

        // [S-439] Only an NVIDIA driver answers these; elsewhere the lines stay hidden whatever is set.
        rows.Add(FluentLayout.Header(Str.WidgetsB_Settings_Gpu_FromDriver.Text()));
        rows.Add(FluentLayout.Note(Str.WidgetsB_Settings_Gpu_DriverNote.Text()));

        rows.Add(FluentLayout.Row(Glyphs.Sun, Str.WidgetsB_Meters_Temperature.Text(), null,
            FluentLayout.Toggle(config.ShowTemperature, value => { config.ShowTemperature = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Lightning, Str.WidgetsB_Settings_Gpu_PowerDraw.Text(), null,
            FluentLayout.Toggle(config.ShowPower, value => { config.ShowPower = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Motion, Str.WidgetsB_Settings_Gpu_FanSpeed.Text(), null,
            FluentLayout.Toggle(config.ShowFan, value => { config.ShowFan = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Timer, Str.WidgetUi_Clocks.Text(), Str.WidgetsB_Settings_Gpu_Clocks_Desc.Text(),
            FluentLayout.Toggle(config.ShowClocks, value => { config.ShowClocks = value; save(); })));

        // [S-1200] The history toggle, its span and the refresh: the rows every meter ends with.
        MeterRows(rows, config, save, Str.WidgetsB_Settings_Gpu_Refresh.Text());
    }
}
