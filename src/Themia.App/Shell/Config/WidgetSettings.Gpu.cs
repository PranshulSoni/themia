using Microsoft.UI.Xaml;
using Themia.App.Services;
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

        rows.Add(FluentLayout.Header("GPU"));

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
                    adapter.DedicatedBytes > 0 ? $"{Formatting.Bytes(adapter.DedicatedBytes)} of its own memory" : "Shares system memory",
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

            rows.Add(FluentLayout.Note("With two or more switched on, each adapter gets its own panel."));
        }

        rows.Add(FluentLayout.Row(Glyphs.Memory, "Dedicated memory", "The adapter's own video memory, with a bar",
            FluentLayout.Toggle(config.ShowDedicated, value => { config.ShowDedicated = value; save(); }),
            "vram", "video memory"));

        rows.Add(FluentLayout.Row(Glyphs.Memory, "Shared memory", "System memory the adapter is borrowing",
            FluentLayout.Toggle(config.ShowShared, value => { config.ShowShared = value; save(); })));

        // [S-1203] Video decode and encode are engines like the others, so they are in this list.
        rows.Add(FluentLayout.Row(Glyphs.Group, "Engines", "3D, compute, copy and the video encoder and decoder, one bar each",
            FluentLayout.Toggle(config.ShowEngines, value => { config.ShowEngines = value; save(); }),
            "encoder", "decoder", "video engines"));

        rows.Add(FluentLayout.Row(Glyphs.Apps, "Top processes", "The apps using the adapter most",
            FluentLayout.Toggle(config.ShowProcesses, value => { config.ShowProcesses = value; save(); }),
            "apps", "per process"));

        // [S-439] Only an NVIDIA driver answers these; elsewhere the lines stay hidden whatever is set.
        rows.Add(FluentLayout.Header("From the driver"));
        rows.Add(FluentLayout.Note("NVIDIA cards report these. Other adapters leave the lines out."));

        rows.Add(FluentLayout.Row(Glyphs.Sun, "Temperature", null,
            FluentLayout.Toggle(config.ShowTemperature, value => { config.ShowTemperature = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Lightning, "Power draw", null,
            FluentLayout.Toggle(config.ShowPower, value => { config.ShowPower = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Motion, "Fan speed", null,
            FluentLayout.Toggle(config.ShowFan, value => { config.ShowFan = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Timer, "Clocks", "Core and memory clock",
            FluentLayout.Toggle(config.ShowClocks, value => { config.ShowClocks = value; save(); })));

        // [S-1200] The history toggle, its span and the refresh: the rows every meter ends with.
        MeterRows(rows, config, save, "the adapters are sampled");
    }
}
