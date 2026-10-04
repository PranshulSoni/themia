using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Widgets;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>
/// Settings rows for the GPU meter, the mixer and screen time.
/// <para>
/// Same shape as the rest of <see cref="WidgetSettings"/>: each control writes straight to the live
/// config and saves, because the widget is on the desktop behind the window and watching it change
/// is the point. What it shows first, then how it behaves, then how often it updates.
/// </para>
/// </summary>
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

        AddGraphToggle(rows, config, save);

        rows.Add(FluentLayout.Row(Glyphs.Memory, "Dedicated memory", "The adapter's own video memory, with a bar",
            FluentLayout.Toggle(config.ShowDedicated, value => { config.ShowDedicated = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Memory, "Shared memory", "System memory the adapter is borrowing",
            FluentLayout.Toggle(config.ShowShared, value => { config.ShowShared = value; save(); })));

        // [S-439] Only an NVIDIA driver answers these; elsewhere the rows stay hidden whatever is set.
        rows.Add(FluentLayout.Header("From the driver"));
        rows.Add(FluentLayout.Note("NVIDIA cards report these. Other adapters leave the rows out."));

        rows.Add(FluentLayout.Row(Glyphs.Sun, "Temperature", null,
            FluentLayout.Toggle(config.ShowTemperature, value => { config.ShowTemperature = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Lightning, "Power draw", null,
            FluentLayout.Toggle(config.ShowPower, value => { config.ShowPower = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Motion, "Fan speed", null,
            FluentLayout.Toggle(config.ShowFan, value => { config.ShowFan = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Timer, "Clocks", "Core and memory clock",
            FluentLayout.Toggle(config.ShowClocks, value => { config.ShowClocks = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Video, "Video engines", "Encoder and decoder load",
            FluentLayout.Toggle(config.ShowVideoEngines, value => { config.ShowVideoEngines = value; save(); })));

        FluentLayout.RefreshSeconds(rows, "the adapters are sampled", Math.Clamp(config.IntervalSeconds, 1, 60), 1, 60, 1,
            value => { config.IntervalSeconds = value; save(); });
    }

    public static void Mixer(List<FrameworkElement> rows, MixerConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Mixer"));

        // [S-442] The original request: one slider per app, not one per tab.
        rows.Add(FluentLayout.Row(Glyphs.Group, "One row per app",
            "A browser playing in three tabs gets one slider instead of three",
            FluentLayout.Toggle(config.GroupByApp, value => { config.GroupByApp = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Volume, "Master volume", "The device's own level, as the first row",
            FluentLayout.Toggle(config.ShowMaster, value => { config.ShowMaster = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Play, "Only apps playing now",
            "Leave out apps that hold an audio session but are silent",
            FluentLayout.Toggle(config.OnlyActive, value => { config.OnlyActive = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Bell, "System sounds", "Windows' own notification sounds",
            FluentLayout.Toggle(config.ShowSystemSounds, value => { config.ShowSystemSounds = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Apps, "App icons", "Each app's own icon rather than a generic one",
            FluentLayout.Toggle(config.ShowIcons, value => { config.ShowIcons = value; save(); })));

        rows.Add(FluentLayout.Note("The list follows Windows as apps start and stop playing, and moves to whichever output device is the default."));
    }

    public static void ScreenTime(List<FrameworkElement> rows, ScreenTimeConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Screen time"));

        // [S-451] Two axes: what is covered, and how it is drawn. Changing the period writes both,
        // with the style clamped into the new period, so the pair stored is always drawable.
        var style = new ComboBox { MinWidth = 150 };

        void FillStyles()
        {
            style.Items.Clear();
            var period = ScreenTimeRules.ClampPeriod(config.Period);
            var current = ScreenTimeRules.ClampStyle(period, config.Style);

            foreach (var option in ScreenTimeRules.StylesFor(period))
            {
                style.Items.Add(new ComboBoxItem { Content = StyleName(option), Tag = option });
            }

            style.SelectedIndex = ScreenTimeRules.StylesFor(period).ToList().IndexOf(current);
        }

        FillStyles();
        style.SelectionChanged += (_, _) =>
        {
            if (style.SelectedItem is ComboBoxItem { Tag: ScreenTimeStyle chosen } && chosen != config.Style)
            {
                config.Style = chosen;
                save();
            }
        };

        rows.Add(FluentLayout.Row(Glyphs.Calendar, "Period", "One day, or a rolling window that ends today",
            FluentLayout.Choice(
                [(ScreenTimePeriod.Day, "Day"), (ScreenTimePeriod.Week, "Last 7 days"), (ScreenTimePeriod.Month, "Last 30 days")],
                ScreenTimeRules.ClampPeriod(config.Period),
                value =>
                {
                    config.Period = value;
                    config.Style = ScreenTimeRules.ClampStyle(value, config.Style);
                    save();
                    FillStyles();
                })));

        rows.Add(FluentLayout.Row(Glyphs.Palette, "Style", "A day draws its apps; a window draws its days", style));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Apps listed",
            "How many the legend holds; 0 lists none and keeps the drawing",
            FluentLayout.Slider(ScreenTimeRules.EffectiveMaxApps(config.MaxApps), 0, ScreenTimeRules.MaxAppsLimit, 1, string.Empty,
                value => { config.MaxApps = (int)Math.Round(value); save(); })));

        rows.Add(FluentLayout.Header("Figures"));

        rows.Add(FluentLayout.Row(Glyphs.Hours, "Average per day", "In a window, beside the total and on each app",
            FluentLayout.Toggle(config.ShowAverage, value => { config.ShowAverage = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Shuffle, "Switches", "How often you moved between apps",
            FluentLayout.Toggle(config.ShowSwitches, value => { config.ShowSwitches = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Share", "Each app's part of the total, as a percentage",
            FluentLayout.Toggle(config.ShowPercent, value => { config.ShowPercent = value; save(); })));

        AddLimits(rows, config, save);
        AddHiddenApps(rows, config, save);

        rows.Add(FluentLayout.Note(
            "Screen time is recorded on this PC only, while Themia is running. It is kept apart from your layout, so copying or exporting a widget never carries it."));
    }

    private static string StyleName(ScreenTimeStyle style) => style switch
    {
        ScreenTimeStyle.Doughnut => "Doughnut",
        ScreenTimeStyle.Bars => "Bars",
        ScreenTimeStyle.Heatmap => "Calendar",
        _ => "None",
    };

    /// <summary>
    /// [S-457] Daily limits, edited in quarter hours and stored in seconds. A limit of zero is no
    /// limit at all, never one that everything instantly exceeds.
    /// </summary>
    private static void AddLimits(List<FrameworkElement> rows, ScreenTimeConfig config, Action save)
    {
        rows.Add(FluentLayout.Header("Limits"));

        rows.Add(FluentLayout.Row(Glyphs.Timer, "Daily limit", "Total screen time per day; 0 is off",
            FluentLayout.Slider(Math.Clamp(config.GoalSeconds / 60, 0, 1440), 0, 1440, 15, " min",
                value => { config.GoalSeconds = (int)Math.Round(value) * 60; save(); })));

        // The apps the history knows, so a limit is picked rather than typed and mistyped.
        var known = KnownApps();

        FluentLayout.ListEditor(rows, new ListEditorOptions<ScreenTimeAppLimit>
        {
            Items = config.AppLimits,
            Save = save,
            EmptyText = "No app has a limit of its own.",
            AddTitle = "Limit an app",
            AddAction = "Add",
            Create = () => new ScreenTimeAppLimit { App = known.Count > 0 ? known[0].App : string.Empty, Seconds = 3600 },
            Body = limit => LimitEditor(limit, known, save),
        });
    }

    private static FrameworkElement LimitEditor(ScreenTimeAppLimit limit, IReadOnlyList<(string App, string Name)> known, Action save)
    {
        var app = new ComboBox { MinWidth = 180 };
        var options = known.ToList();

        // An app no longer in the history keeps its row rather than silently becoming another.
        if (limit.App.Length > 0 && options.All(o => o.App != limit.App))
        {
            options.Insert(0, (limit.App, AppNames.Prettify(limit.App)));
        }

        foreach (var (key, name) in options)
        {
            app.Items.Add(new ComboBoxItem { Content = name, Tag = key });
        }

        app.SelectedIndex = options.FindIndex(o => o.App == limit.App);
        app.SelectionChanged += (_, _) =>
        {
            if (app.SelectedItem is ComboBoxItem { Tag: string key })
            {
                limit.App = key;
                save();
            }
        };

        var minutes = FluentLayout.Slider(Math.Clamp(limit.Seconds / 60, 15, 720), 15, 720, 15, " min",
            value => { limit.Seconds = (int)Math.Round(value) * 60; save(); });

        return new StackPanel { Spacing = Fluent.Space1, Children = { app, minutes } };
    }

    /// <summary>
    /// [S-455] The apps hidden from this widget, each with a way back. The keys are all the config
    /// holds — no durations, no dates.
    /// </summary>
    private static void AddHiddenApps(List<FrameworkElement> rows, ScreenTimeConfig config, Action save)
    {
        rows.Add(FluentLayout.Header("Hidden apps"));

        var host = new StackPanel { Spacing = Fluent.Space1 };
        rows.Add(host);

        var names = KnownApps().ToDictionary(a => a.App, a => a.Name, StringComparer.Ordinal);

        void Rebuild()
        {
            host.Children.Clear();

            if (config.HiddenApps.Count == 0)
            {
                host.Children.Add(FluentLayout.Note("No hidden apps. Right-click an app in the widget to hide it."));
                return;
            }

            foreach (var key in config.HiddenApps.ToList())
            {
                var show = FluentLayout.IconCommand(Glyphs.View, "Show again");
                show.Click += (_, _) =>
                {
                    config.HiddenApps.Remove(key);
                    save();
                    Rebuild();
                };

                host.Children.Add(FluentLayout.Row(Glyphs.Hidden, names.GetValueOrDefault(key) ?? AppNames.Prettify(key), key, show));
            }
        }

        Rebuild();
    }

    /// <summary>Apps in the last month of history, busiest first, as (key, name).</summary>
    private static List<(string App, string Name)> KnownApps()
    {
        if (AppServices.ScreenTime is not { } source)
        {
            return [];
        }

        return [.. ScreenTimeRules.Aggregate(source.Range(30), new HashSet<string>()).Select(a => (a.App, a.Name))];
    }
}
