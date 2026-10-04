using Microsoft.UI.Xaml;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>The rows every meter shares.</summary>
internal static partial class WidgetSettings
{
    /// <summary>
    /// [S-1122] What every meter's page ends with: whether it draws its history and how much of it,
    /// then how often it samples. A kind's own rows go above these.
    /// </summary>
    /// <param name="refresh">The refresh row's whole sentence: "How often the load is read".</param>
    private static void MeterRows(List<FrameworkElement> rows, GaugeConfig gauge, Action save, string refresh)
    {
        rows.Add(FluentLayout.Header(Str.WidgetsB_Settings_Meters_History.Text()));
        AddGraphToggle(rows, gauge, save);

        var spans = MeterTiming.SpanChoices.Select(span => (span, MeterTiming.SpanLabel(span))).ToList();

        rows.Add(FluentLayout.Row(Glyphs.Hours, Str.WidgetsB_Settings_Meters_ChartSpan_Label.Text(),
            Str.WidgetsB_Settings_Meters_ChartSpan_Desc.Text(),
            FluentLayout.Choice(spans, MeterTiming.NearestSpan(gauge.GraphSpanSeconds),
                value => { gauge.GraphSpanSeconds = value; save(); }),
            "graph span", "time span"));

        FluentLayout.RefreshSeconds(rows, refresh, gauge.EffectiveIntervalSeconds,
            MeterTiming.MinIntervalSeconds, gauge.MaxIntervalSeconds, 1,
            value => { gauge.IntervalSeconds = value; save(); });
    }

    /// <summary>The history switch alone, for a gauge whose page lays out its own refresh (the GPU).</summary>
    private static void AddGraphToggle(List<FrameworkElement> rows, GaugeConfig gauge, Action save)
        => rows.Add(FluentLayout.Row(Glyphs.Motion, Str.WidgetsB_Settings_Meters_ShowHistory_Label.Text(),
            Str.WidgetsB_Settings_Meters_ShowHistory_Desc.Text(),
            FluentLayout.Toggle(gauge.ShowGraph, value => { gauge.ShowGraph = value; save(); }),
            "graph", "chart", "sparkline"));
}
