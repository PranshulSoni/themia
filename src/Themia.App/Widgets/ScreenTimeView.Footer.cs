using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Shell;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// The footer a large tile adds under the drawing and its legend: a stats line, the daily limit as a
/// bar, and — under a day — the week that day belongs to as seven small bars.
/// </summary>
internal sealed partial class ScreenTimeView
{
    /// <summary>The drawing and its legend keep at least this much before a footer is considered.</summary>
    private const double BodyFloor = 200;

    /// <summary>The week strip's bars, without their weekday labels.</summary>
    private const double StripBars = 48;

    private static readonly Brush StripBrush = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);
    private static readonly Brush TrackBrush = FluentLayout.Themed("ControlStrongFillColorDisabledBrush", Colors.Gray);

    private readonly StackPanel _footer = new() { Spacing = Fluent.Space2 };

    /// <summary>
    /// [S-1213]..[S-1215] Build the footer for this size and return the height it takes from the
    /// body, its gap included; 0 when the tile is not large both ways or the body would be squeezed.
    /// </summary>
    private double LayFooter(double width, double height, ScreenTimeStyle style)
    {
        _footer.Children.Clear();
        _footer.Visibility = Visibility.Collapsed;

        if (_source is null || !_area.AtLeast(SizeClass.Expanded, SizeClass.Expanded) || height - BodyFloor <= 0)
        {
            return 0;
        }

        var room = new Disclosure(width - (WidgetLayout.ContentInset * 2), height - BodyFloor, Fluent.Space2);
        var day = !_model.Window;
        var today = _source.Today;
        var shown = day && LocalDay.TryParse(_pinned, out var pinned) ? pinned : today;

        // The week the shown day ends, read once for both the line and the strip.
        var week = day
            ? [.. ScreenTimeRules.DaysEnding(shown, 7).Select(d => (Day: d, Seconds: DayTotal(d, today)))]
            : _model.Totals.Select(t => (Day: LocalDay.TryParse(t.Date, out var d) ? d : today, t.Seconds)).ToList();

        // Rung 1: the stats line.
        var stats = ScreenTimeRules.StatsLine(week, _model.Apps.Count, _config.Period);
        if (stats.Length > 0 && room.Take(FluentType.Caption.LineHeight))
        {
            var line = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
            line.Text = stats;
            line.TextTrimming = TextTrimming.CharacterEllipsis;
            _footer.Children.Add(line);
        }

        // Rung 2: the daily limit as a bar — a window judges its daily average, as its headline does.
        var used = _model.Window ? ScreenTimeRules.DailyAverage(_model.Total, _model.Days) : _model.Total;
        if (ScreenTimeRules.Goal(used, DayLimit) is { } goal && room.Take(FluentType.Caption.LineHeight + 6))
        {
            _footer.Children.Add(GoalBar(goal, _model.Window));
        }

        // Rung 3: the week strip, under a day only (a window already draws its days).
        if (day && style != ScreenTimeStyle.None && room.Take(StripBars + FluentType.Caption.LineHeight + 2))
        {
            _footer.Children.Add(WeekStrip(week, shown, today));
        }

        if (_footer.Children.Count == 0)
        {
            return 0;
        }

        _footer.Visibility = Visibility.Visible;
        _footer.Margin = new Thickness(WidgetLayout.ContentInset, 0, WidgetLayout.ContentInset, Fluent.Space2);
        return height - BodyFloor - room.Remaining + Fluent.Space2;
    }

    /// <summary>A day's total with hidden apps left out, as every other figure here is.</summary>
    private long DayTotal(LocalDay day, LocalDay today)
    {
        if (_source is null)
        {
            return 0;
        }

        var record = _source.Day(day == today ? null : day.Key);
        return ScreenTimeRules.Sum(ScreenTimeRules.Visible(record.Apps, _model.Hidden));
    }

    /// <summary>[S-1213] "Daily limit … 2h 10m of 4h", over a bar that turns critical past the limit.</summary>
    private static Grid GoalBar(ScreenTimeGoal goal, bool perDay)
    {
        var label = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
        label.Text = Str.WidgetsC_ScreenTime_DailyLimit.Text();

        var value = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
        value.Text = perDay
            ? Str.WidgetsC_ScreenTime_UsedPerDayOf.Format(("used", ScreenTimeRules.FormatDuration(goal.Used)), ("limit", ScreenTimeRules.FormatLimit(goal.Limit)))
            : Str.WidgetsC_ScreenTime_UsedOf.Format(("used", ScreenTimeRules.FormatDuration(goal.Used)), ("limit", ScreenTimeRules.FormatLimit(goal.Limit)));
        value.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(value, 1);

        var bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Value = Math.Clamp(goal.Ratio, 0, 1),
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = goal.Over ? CriticalBrush : StripBrush,
        };
        Grid.SetRow(bar, 1);
        Grid.SetColumnSpan(bar, 2);

        return new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
            },
            Children = { label, value, bar },
        };
    }

    /// <summary>
    /// [S-1214] The week ending on the shown day: one bar per day scaled to the busiest, the shown day
    /// in the accent and the rest dimmed, weekday under each. Clicking a day shows it.
    /// </summary>
    private Grid WeekStrip(IReadOnlyList<(LocalDay Day, long Seconds)> week, LocalDay shown, LocalDay today)
    {
        var busiest = Math.Max(1, week.Max(d => d.Seconds));
        var strip = new Grid { ColumnSpacing = Fluent.Space2, Height = StripBars + FluentType.Caption.LineHeight + 2 };
        var wide = (_area.Width - (WidgetLayout.ContentInset * 2)) / Math.Max(1, week.Count) >= 36;

        for (var i = 0; i < week.Count; i++)
        {
            var (date, seconds) = week[i];
            strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var column = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(StripBars) });
            column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var track = new Border { Background = TrackBrush, Opacity = 0.35, CornerRadius = new CornerRadius(2) };
            column.Children.Add(track);

            if (seconds > 0)
            {
                column.Children.Add(new Border
                {
                    Height = Math.Max(2, (double)seconds / busiest * StripBars),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Background = StripBrush,
                    Opacity = date == shown ? 1 : 0.45,
                    CornerRadius = new CornerRadius(2),
                });
            }

            var label = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            label.Text = wide ? DayShort(date.DayOfWeek) : DayLetter(date.DayOfWeek);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.TextTrimming = TextTrimming.None;
            label.Margin = new Thickness(0, 2, 0, 0);

            if (date == shown)
            {
                label.Foreground = TodayLabelBrush;
            }

            Grid.SetRow(label, 1);
            column.Children.Add(label);

            ToolTipService.SetToolTip(column, $"{Formatting.DayHeading(date.Date, today.Date)} — {ScreenTimeRules.FormatDuration(seconds)}");

            // [S-454] The strip is another way through the days: today unpins, any other day pins.
            // Days past the navigation's reach stay a tooltip.
            var key = date == today ? null : date.Key;
            if (date.DaysUntil(today) <= ScreenTimeRules.MaxHistoryOffset)
            {
                column.Tapped += (_, e) =>
                {
                    e.Handled = true;
                    _pinned = key;
                    _focus = null;
                    Render();
                };
            }

            Grid.SetColumn(column, i);
            strip.Children.Add(column);
        }

        return strip;
    }
}
