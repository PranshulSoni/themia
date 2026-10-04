using Microsoft.UI.Xaml;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>The rows every meter shares, and the meters with nothing of their own yet.</summary>
internal static partial class WidgetSettings
{
    /// <summary>
    /// [S-1122] What every meter's page ends with: whether it draws its history and how much of it,
    /// then how often it samples. A kind's own rows go above these.
    /// </summary>
    /// <param name="what">Completes "How often …": "the load is read", "the host is probed".</param>
    private static void MeterRows(List<FrameworkElement> rows, GaugeConfig gauge, Action save, string what)
    {
        rows.Add(FluentLayout.Header("History"));
        AddGraphToggle(rows, gauge, save);

        var spans = MeterTiming.SpanChoices.Select(span => (span, MeterTiming.SpanLabel(span))).ToList();

        rows.Add(FluentLayout.Row(Glyphs.Hours, "Chart span",
            "How much history the chart covers; a narrow tile shows the most recent part",
            FluentLayout.Choice(spans, MeterTiming.NearestSpan(gauge.GraphSpanSeconds),
                value => { gauge.GraphSpanSeconds = value; save(); }),
            "graph span", "time span"));

        FluentLayout.RefreshSeconds(rows, what, gauge.EffectiveIntervalSeconds,
            MeterTiming.MinIntervalSeconds, gauge.MaxIntervalSeconds, 1,
            value => { gauge.IntervalSeconds = value; save(); });
    }

    /// <summary>The history switch alone, for a gauge whose page lays out its own refresh (the GPU).</summary>
    private static void AddGraphToggle(List<FrameworkElement> rows, GaugeConfig gauge, Action save)
        => rows.Add(FluentLayout.Row(Glyphs.Motion, "Show history",
            "Draw a chart of the recent samples",
            FluentLayout.Toggle(gauge.ShowGraph, value => { gauge.ShowGraph = value; save(); }),
            "graph", "chart", "sparkline"));

    public static void Cpu(List<FrameworkElement> rows, CpuConfig cpu, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(cpu);
        ArgumentNullException.ThrowIfNull(save);

        MeterRows(rows, cpu, save, "the load is read");
    }

    public static void Network(List<FrameworkElement> rows, NetworkConfig network, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(save);

        MeterRows(rows, network, save, "the adapters are read");
    }
}
