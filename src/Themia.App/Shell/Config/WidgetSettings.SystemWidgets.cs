using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Widgets;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>
/// Settings rows for screen time (the GPU meter's are in <c>WidgetSettings.Gpu.cs</c>, the mixer's in
/// <c>WidgetSettings.Mixer.cs</c>).
/// <para>
/// Same shape as the rest of <see cref="WidgetSettings"/>: each control writes straight to the live
/// config and saves, because the widget is on the desktop behind the window and watching it change
/// is the point. What it shows first, then how it behaves, then how often it updates.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    public static void ScreenTime(List<FrameworkElement> rows, ScreenTimeConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header(Str.Widget_Screentime_Label.Text()));

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

        rows.Add(FluentLayout.Row(Glyphs.Calendar, Str.Screentime_Cfg_Period.Text(), Str.WidgetsC_ScreenTime_Settings_Period_Desc.Text(),
            FluentLayout.Choice(
                [(ScreenTimePeriod.Day, Str.Screentime_Cfg_PeriodDay.Text()), (ScreenTimePeriod.Week, Str.Screentime_Last7Days.Text()), (ScreenTimePeriod.Month, Str.Screentime_Last30Days.Text())],
                ScreenTimeRules.ClampPeriod(config.Period),
                value =>
                {
                    config.Period = value;
                    config.Style = ScreenTimeRules.ClampStyle(value, config.Style);
                    save();
                    FillStyles();
                })));

        rows.Add(FluentLayout.Row(Glyphs.Palette, Str.Screentime_Cfg_Style.Text(), Str.WidgetsC_ScreenTime_Settings_Style_Desc.Text(), style));

        // [S-1211] The list is a switch; its length is a ceiling over what the tile has room for.
        rows.Add(FluentLayout.Row(Glyphs.Apps, Str.WidgetsC_ScreenTime_Settings_List_Label.Text(), Str.WidgetsC_ScreenTime_Settings_List_Desc.Text(),
            FluentLayout.Toggle(config.ShowList, value => { config.ShowList = value; save(); }),
            "legend"));

        rows.Add(FluentLayout.Row(Glyphs.Count, Str.WidgetsC_ScreenTime_Settings_Most_Label.Text(),
            Str.WidgetsC_ScreenTime_Settings_Most_Desc.Text(),
            FluentLayout.Slider(ScreenTimeRules.EffectiveMaxApps(config.MaxApps), 0, ScreenTimeRules.MaxAppsLimit, 1, string.Empty,
                value => { config.MaxApps = (int)Math.Round(value); save(); }),
            "apps listed", "max apps"));

        rows.Add(FluentLayout.Header(Str.WidgetsC_ScreenTime_Settings_Figures.Text()));

        rows.Add(FluentLayout.Row(Glyphs.Hours, Str.Screentime_Metric_Average.Text(), Str.WidgetsC_ScreenTime_Settings_Average_Desc.Text(),
            FluentLayout.Toggle(config.ShowAverage, value => { config.ShowAverage = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Shuffle, Str.Screentime_Metric_Switches.Text(), Str.WidgetsC_ScreenTime_Settings_Switches_Desc.Text(),
            FluentLayout.Toggle(config.ShowSwitches, value => { config.ShowSwitches = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, Str.Screentime_Metric_Percent.Text(), Str.WidgetsC_ScreenTime_Settings_Share_Desc.Text(),
            FluentLayout.Toggle(config.ShowPercent, value => { config.ShowPercent = value; save(); })));

        AddLimits(rows, config, save);
        AddHiddenApps(rows, config, save);

        rows.Add(FluentLayout.Note(
            Str.WidgetsC_ScreenTime_Settings_Privacy.Text()));
    }

    private static string StyleName(ScreenTimeStyle style) => style switch
    {
        ScreenTimeStyle.Doughnut => Str.Screentime_Cfg_StyleDoughnut.Text(),
        ScreenTimeStyle.Bars => Str.Screentime_Cfg_StyleBars.Text(),
        ScreenTimeStyle.Heatmap => Str.WidgetsC_ScreenTime_Settings_StyleCalendar.Text(),
        _ => Str.Screentime_Cfg_StyleNone.Text(),
    };

    /// <summary>
    /// [S-457] Daily limits, edited in quarter hours and stored in seconds. A limit of zero is no
    /// limit at all, never one that everything instantly exceeds.
    /// </summary>
    private static void AddLimits(List<FrameworkElement> rows, ScreenTimeConfig config, Action save)
    {
        rows.Add(FluentLayout.Header(Str.WidgetsC_ScreenTime_Settings_Limits.Text()));

        rows.Add(FluentLayout.Row(Glyphs.Timer, Str.WidgetsC_ScreenTime_DailyLimit.Text(), Str.WidgetsC_ScreenTime_Settings_DailyLimit_Desc.Text(),
            FluentLayout.Slider(Math.Clamp(config.GoalSeconds / 60, 0, 1440), 0, 1440, 15, Str.WidgetsC_Pomodoro_MinutesUnit.Text(),
                value => { config.GoalSeconds = (int)Math.Round(value) * 60; save(); })));

        // The apps the history knows, so a limit is picked rather than typed and mistyped.
        var known = KnownApps();

        FluentLayout.ListEditor(rows, new ListEditorOptions<ScreenTimeAppLimit>
        {
            Items = config.AppLimits,
            Save = save,
            EmptyText = Str.WidgetsC_ScreenTime_Settings_NoAppLimits.Text(),
            AddTitle = Str.WidgetsC_ScreenTime_Settings_LimitApp.Text(),
            AddAction = Str.Common_Add.Text(),
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

        var minutes = FluentLayout.Slider(Math.Clamp(limit.Seconds / 60, 15, 720), 15, 720, 15, Str.WidgetsC_Pomodoro_MinutesUnit.Text(),
            value => { limit.Seconds = (int)Math.Round(value) * 60; save(); });

        return new StackPanel { Spacing = Fluent.Space1, Children = { app, minutes } };
    }

    /// <summary>
    /// [S-455] The apps hidden from this widget, each with a way back. The keys are all the config
    /// holds — no durations, no dates.
    /// </summary>
    private static void AddHiddenApps(List<FrameworkElement> rows, ScreenTimeConfig config, Action save)
    {
        rows.Add(FluentLayout.Header(Str.Screentime_Cfg_Hidden.Text()));

        var host = new StackPanel { Spacing = Fluent.Space1 };
        rows.Add(host);

        var names = KnownApps().ToDictionary(a => a.App, a => a.Name, StringComparer.Ordinal);

        void Rebuild()
        {
            host.Children.Clear();

            if (config.HiddenApps.Count == 0)
            {
                host.Children.Add(FluentLayout.Note(Str.WidgetsC_ScreenTime_Settings_NoHidden.Text()));
                return;
            }

            foreach (var key in config.HiddenApps.ToList())
            {
                var show = FluentLayout.IconCommand(Glyphs.View, Str.WidgetsC_ScreenTime_Settings_ShowAgain.Text());
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
