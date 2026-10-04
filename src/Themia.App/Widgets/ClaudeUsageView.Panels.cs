using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Shell;
using Themia.Core.Localization;
using Themia.Core.Developer;
using Themia.Core.Widgets;
using Windows.Foundation;

namespace Themia.App.Widgets;

/// <summary>The two layouts that are not a stack of bars: the XS hero and the Huge two-panel split.</summary>
internal sealed partial class ClaudeUsageView
{
    /// <summary>
    /// [S-792] The session's percentage as large as the tile allows, in its severity colour, over its
    /// bar, and the week as one caption line under it.
    /// </summary>
    private void Hero(IReadOnlyList<UsageBar> bars, double width, double height)
    {
        var session = bars[0];
        var percent = ClaudeUsageRules.Percent(session.Window.Utilization);
        var week = bars.Count > 1 ? bars[1] : null;

        // The reading takes what the bar and the week line leave.
        var below = ClaudeUsageRules.BarMin + Fluent.Space1 + (week is null ? 0 : FluentType.Caption.LineHeight + Fluent.Space1);
        var step = FluentType.Hero(width, Math.Max(0, height - below), 4);

        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var value = Fluent.Text(step);
        value.Text = Percent(percent);
        value.Foreground = SeverityText(percent);
        value.TextTrimming = TextTrimming.None;
        panel.Children.Add(value);

        var track = Track(percent, ClaudeUsageRules.BarMin, elapsed: null);
        track.Margin = new Thickness(0, Fluent.Space1, 0, 0);
        panel.Children.Add(track);

        if (week is not null)
        {
            var line = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
            line.Text = $"{week.Mini} {Percent(ClaudeUsageRules.Percent(week.Window.Utilization))}";
            line.Margin = new Thickness(0, Fluent.Space1, 0, 0);
            panel.Children.Add(line);
        }

        var now = DateTimeOffset.Now;
        ToolTipService.SetToolTip(panel, string.Join(Environment.NewLine, bars.Select(b => Tooltip(b, now))));
        _body.Children.Add(panel);
    }

    /// <summary>
    /// [S-794] The session as a gauge (percentage, countdown, pace) and the weekly windows as bars
    /// with their pace ticks: side by side on a panel, the bars spread over the full height; or, in a
    /// tall column, the gauge over the week.
    /// </summary>
    private void Panels(IReadOnlyList<UsageBar> bars, double width, double height, DateTimeOffset now, UsageGaugePlace place)
    {
        var rest = bars.Skip(1).ToList();

        if (place == UsageGaugePlace.Above)
        {
            // The week gets exactly its Full rows; the ring takes the rest.
            var rowsHeight = ClaudeUsageRules.StackHeight(rest.Count, UsageDensity.Full);
            var gap = rest.Count == 0 ? 0 : Fluent.Space4;
            var ring = Gauge(bars[0], width, Math.Max(0, height - rowsHeight - gap), now);
            ring.VerticalAlignment = VerticalAlignment.Top;
            _body.Children.Add(ring);

            if (rest.Count > 0)
            {
                var week = Rows(rest, width, rowsHeight, now, extras: false);
                week.VerticalAlignment = VerticalAlignment.Bottom;
                _body.Children.Add(week);
            }

            return;
        }

        _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Fluent.Space6) });
        _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var column = Math.Max(0, (width - Fluent.Space6) / 2);
        _body.Children.Add(Gauge(bars[0], column, height, now));

        if (rest.Count > 0)
        {
            var weeks = Rows(rest, column, height, now, extras: true);
            SetColumn(weeks, 2);
            _body.Children.Add(weeks);
        }
    }

    private static Grid Gauge(UsageBar bar, double width, double height, DateTimeOffset now)
    {
        var percent = ClaudeUsageRules.Percent(bar.Window.Utilization);
        var elapsed = ClaudeUsageRules.Elapsed(ClaudeUsageRules.WindowLength(bar), bar.Window.ResetsAt, now);

        // The label and the pace sit under the ring, which takes the rest as a square.
        var under = FluentType.Body.LineHeight + FluentType.Caption.LineHeight + Fluent.Space2;
        var side = Math.Max(0, Math.Min(width, height - under));
        var radius = Math.Max(0, (side / 2) - (GaugeStroke / 2));
        var centre = side / 2;

        var ring = new Grid { Width = side, Height = side, HorizontalAlignment = HorizontalAlignment.Center };

        if (radius > 0)
        {
            ring.Children.Add(Arc(centre, radius, 1, FluentLayout.Themed("ControlStrongFillColorDisabledBrush", Colors.Gray)));

            if (percent > 0)
            {
                ring.Children.Add(Arc(centre, radius, percent / 100, SeverityBrush(percent)));
            }

            if (elapsed is { } share)
            {
                // The pace mark crosses the ring where an even spend would have reached.
                var (ix, iy) = UsageGauge.Point(centre, centre, radius - GaugeStroke, share);
                var (ox, oy) = UsageGauge.Point(centre, centre, radius + (GaugeStroke / 2), share);
                var tick = new Line
                {
                    X1 = ix,
                    Y1 = iy,
                    X2 = ox,
                    Y2 = oy,
                    StrokeThickness = Fluent.Hairline * 2,
                    Stroke = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White),
                };
                ToolTipService.SetToolTip(tick, Str.WidgetsA_Claude_PaceTick.Text());
                ring.Children.Add(tick);
            }
        }

        // The reading in the middle, as large as the ring's opening allows.
        var reading = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var value = Fluent.Text(FluentType.Hero(radius * 1.2, radius * 0.7, 4));
        value.Text = Percent(percent);
        value.Foreground = SeverityText(percent);
        value.HorizontalAlignment = HorizontalAlignment.Center;
        value.TextTrimming = TextTrimming.None;
        reading.Children.Add(value);

        if (ClaudeUsageRules.ResetLabel(bar.Window.ResetsAt, now, Strings.Current) is { Length: > 0 } reset)
        {
            var countdown = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
            countdown.Text = side >= 240
                ? $"{reset} · {ClaudeUsageRules.ResetClock(bar.Window.ResetsAt!.Value, now, TimeZoneInfo.Local, Strings.Current)}"
                : ClaudeUsageRules.ResetLabel(bar.Window.ResetsAt, now, Strings.Current, brief: true);
            countdown.HorizontalAlignment = HorizontalAlignment.Center;
            reading.Children.Add(countdown);
        }

        ring.Children.Add(reading);

        var label = Fluent.Text(FluentType.Body);
        label.Text = bar.Label;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.Margin = new Thickness(0, Fluent.Space2, 0, 0);

        var pace = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
        // The share of the window gone only where the line has the width for it.
        var roomy = width >= 240;
        pace.Text = elapsed is { } e
            ? roomy
                ? $"{ClaudeUsageRules.PaceText(ClaudeUsageRules.Pace(percent, e), Strings.Current)} · {WindowGone(e)}"
                : ClaudeUsageRules.PaceText(ClaudeUsageRules.Pace(percent, e), Strings.Current)
            : (roomy ? Str.WidgetsA_Claude_NothingUsed : Str.WidgetsA_Claude_Unused).Text();
        pace.HorizontalAlignment = HorizontalAlignment.Center;

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { ring, label, pace } };
        var host = new Grid();
        host.Children.Add(stack);
        ToolTipService.SetToolTip(host, Tooltip(bar, now));
        return host;
    }

    /// <summary>[S-794] The gauge's arc from its start to <paramref name="fraction"/> of its 270°.</summary>
    private static Microsoft.UI.Xaml.Shapes.Path Arc(double centre, double radius, double fraction, Brush stroke)
    {
        var (sx, sy) = UsageGauge.Point(centre, centre, radius, 0);
        var (ex, ey) = UsageGauge.Point(centre, centre, radius, fraction);

        var figure = new PathFigure { StartPoint = new Point(sx, sy), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = new Point(ex, ey),
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = UsageGauge.IsLargeArc(fraction),
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        return new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geometry,
            Stroke = stroke,
            StrokeThickness = GaugeStroke,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
    }
}
