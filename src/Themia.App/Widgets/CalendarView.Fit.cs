using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Themia.App.Shell;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Office;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// The layouts the classic calendar did not have, which let a tile of any size be a calendar
/// rather than a squashed one ([S-1260]): the next thing on an XS tile, a strip of days on a wide
/// short one, and the month with the upcoming list below it (tall) or beside it (XL).
/// </summary>
internal sealed partial class CalendarView
{
    /// <summary>One strip entry on one line ("09:00 Standup") from this column width; narrower stacks the time above.</summary>
    private const double StripOneLineFrom = 100;

    // ---- next ([S-1261]) ------------------------------------------------------------------------

    /// <summary>
    /// [S-1261] The XS tile: when, what, and how many more that day — the one glance a 120 px tile is
    /// for. With nothing coming up, today's weekday and date, so the tile still says something true.
    /// </summary>
    private void DrawNext(double width, double height)
    {
        var today = Today;
        var next = CalendarFit.Next(_events, _birthdays, today, TimeZoneInfo.Local, DateTimeOffset.Now);
        var box = new WidgetArea(width, height).Deflate(Fluent.Space3 * 2, Fluent.Space1 * 2);
        var room = new Disclosure(box);
        var stack = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };

        if (next is null)
        {
            var weekday = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
            weekday.Text = CalendarText.WeekdayName(today.DayOfWeek);
            stack.Children.Add(weekday);
            room.Reserve(FluentType.Caption.LineHeight);

            var number = Fluent.Text(FluentType.Fit(box.Width, room.Remaining - FluentType.Caption.LineHeight, 6, FluentType.BodyStrong, FluentType.Title));
            // Day and month in the language's own order ("Oct 4", "4. Okt."), never parsed out of other text.
            number.Text = LocaleFormat.Date(Strings.Current, today, DateStyle.MonthDayShort);
            number.TextTrimming = TextTrimming.None;
            stack.Children.Add(number);

            var none = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            none.Text = Str.WidgetsA_Calendar_NothingComingUp.Text();
            stack.Children.Add(none);
            _content.Children.Add(new Border { Child = stack, Padding = new Thickness(Fluent.Space3, Fluent.Space1, Fluent.Space3, Fluent.Space1) });
            return;
        }

        var item = next.Item;
        var now = DateTimeOffset.Now;
        var running = item.Event is { } ev && AgendaRules.IsInProgress(ev.AllDay, ev.Start, ev.End, now);
        var when = item.Birthday is not null ? Str.WidgetsA_Calendar_Birthday.Text()
            : item.Event!.AllDay ? Str.Calendar_AllDay.Text()
            : running ? Str.WidgetsA_Calendar_Now.Text()
            : Clock(item.Event.Start);

        // The day is said whenever it is not today: an XS tile has no heading to carry it.
        var whenLine = Fluent.Text(FluentType.Strong(FluentType.Caption), running ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
        whenLine.Text = next.Day == today ? when : $"{Formatting.DayHeading(next.Day, today)} · {when}";
        Typography.SetNumeralAlignment(whenLine, FontNumeralAlignment.Tabular);
        stack.Children.Add(whenLine);
        room.Reserve(FluentType.Caption.LineHeight + FluentType.BodyStrong.LineHeight);

        // The title before the count: a second line for it first, else a smaller step that still
        // gets the whole of a title too long for one line, before "+N more" gets a line at all.
        var titleText = item.Birthday is { } b ? BirthdayRules.Label(b, next.Day) : Title(item.Event!);
        var step = FluentType.BodyStrong;
        var lines = 1;
        if (FluentType.EstimateWidth(step, titleText.Length) > box.Width)
        {
            if (room.Take(FluentType.BodyStrong.LineHeight))
            {
                lines = 2;
            }
            else if (room.Remaining + FluentType.BodyStrong.LineHeight + Disclosure.Tolerance >= 2 * FluentType.Caption.LineHeight)
            {
                step = FluentType.Strong(FluentType.Caption);
                lines = 2;
            }
        }

        var more = next.MoreThatDay > 0 && room.Take(FluentType.Caption.LineHeight);

        var title = Fluent.Text(step);
        title.Text = titleText;
        title.TextWrapping = lines > 1 ? TextWrapping.WrapWholeWords : TextWrapping.NoWrap;
        title.MaxLines = lines;
        stack.Children.Add(title);

        if (more)
        {
            var rest = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            rest.Text = next.Day == today
                ? Str.WidgetsA_Calendar_MoreToday.Format(("count", next.MoreThatDay))
                : Str.WidgetsA_Calendar_More.Format(("count", next.MoreThatDay));
            stack.Children.Add(rest);
        }

        // The calendar's colour (or the caution hue of a birthday) as the stripe the agenda rows carry.
        var stripe = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(1.5),
            Margin = new Thickness(0, 0, Fluent.Space2, 0),
            Background = item.Birthday is not null ? BirthdayBrush() : ShowColors ? ColorOf(item.Event!) : Accent(),
        };

        var tile = new Grid
        {
            Padding = new Thickness(Fluent.Space3, Fluent.Space1, Fluent.Space3, Fluent.Space1),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
            Background = FluentLayout.Themed("SubtleFillColorTransparentBrush", Colors.Transparent),
        };
        stripe.VerticalAlignment = VerticalAlignment.Stretch;
        stack.VerticalAlignment = VerticalAlignment.Center;
        SetColumn(stack, 1);
        tile.Children.Add(stripe);
        tile.Children.Add(stack);

        if (item.Event is { } openable)
        {
            MakeOpenable(tile, openable);
        }

        _content.Children.Add(tile);
    }

    // ---- strip ([S-1262]) -----------------------------------------------------------------------

    /// <summary>
    /// [S-1262] A wide, short tile: a row of day columns — today and the next days, or the week —
    /// each with its events as one-line entries ("09:00 Standup"), as many as fit, then "+N". The
    /// room a squashed month spent on bare day numbers goes to what is on those days.
    /// </summary>
    private void DrawStrip(double width, double height)
    {
        var days = StripDays();
        var first = CalendarFit.StripStart(_config.EffectiveView, _anchor, _config.EffectiveWeekStart);
        var today = Today;
        var zone = TimeZoneInfo.Local;
        var showColors = ShowColors;
        var columnWidth = Math.Max(0, (width - (2 * Fluent.Space1)) / days);
        var oneLine = columnWidth >= StripOneLineFrom;

        var grid = new Grid { Padding = new Thickness(Fluent.Space1, 0, Fluent.Space1, Fluent.Space1) };

        // What a column holds under its heading; entries past it become "+N".
        var available = height - FluentType.Caption.LineHeight - Fluent.Space1 - Fluent.Space1;

        for (var c = 0; c < days; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var day = first.AddDays(c);
            var column = new StackPanel { Spacing = 2, Margin = new Thickness(2, 0, 2, 0) };

            var heading = Fluent.Text(FluentType.Strong(FluentType.Caption), day == today ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
            heading.Text = day == today ? Str.Calendar_Today.Text() : Str.WidgetsA_Calendar_WeekdayDay.Format(("weekday", CalendarText.WeekdayShort(day.DayOfWeek)), ("day", day.Day));
            heading.Margin = new Thickness(Fluent.Space1, 0, 0, Fluent.Space1 - 2);
            column.Children.Add(heading);

            var items = CalendarRules.DayItems(_events, _birthdays, day, zone);
            var used = 0.0;
            var shown = 0;

            foreach (var item in items)
            {
                var cost = (oneLine || item.Event is not { AllDay: false } ? FluentType.Caption.LineHeight : 2 * FluentType.Caption.LineHeight) + 4;
                if (used + cost > available - (shown < items.Count - 1 ? FluentType.Caption.LineHeight : 0))
                {
                    break;
                }

                column.Children.Add(StripEntry(item, day, oneLine, showColors));
                used += cost;
                shown++;
            }

            if (shown < items.Count)
            {
                var more = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
                more.Text = Str.WidgetsA_Calendar_More.Format(("count", items.Count - shown));
                more.Margin = new Thickness(Fluent.Space1, 0, 0, 0);
                column.Children.Add(more);
            }

            ToolTipService.SetToolTip(column, items.Count == 0 ? null : DayTooltip(day, items));
            SetColumn(column, c);
            grid.Children.Add(column);
        }

        _content.Children.Add(grid);
    }

    /// <summary>One strip entry: the start time then the title, on one line or stacked in a narrow column.</summary>
    private Border StripEntry(DayItem item, DateOnly day, bool oneLine, bool showColors)
    {
        var text = Fluent.Text(FluentType.Caption);
        var entry = new Border
        {
            Child = text,
            Padding = new Thickness(3, 1, 2, 1),
            CornerRadius = new CornerRadius(2),
            Background = FluentLayout.Themed("SubtleFillColorSecondaryBrush", Colors.Gray),
            BorderThickness = new Thickness(2, 0, 0, 0),
        };

        if (item.Birthday is { } b)
        {
            text.Text = BirthdayRules.Label(b, day);
            entry.BorderBrush = BirthdayBrush();
            ToolTipService.SetToolTip(entry, Str.WidgetsA_Calendar_BirthdayOf.Format(("name", text.Text)));
            return entry;
        }

        var e = item.Event!;
        entry.BorderBrush = showColors ? ColorOf(e) : Accent();

        if (!e.AllDay)
        {
            text.Inlines.Add(new Run { Text = Clock(e.Start), Foreground = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray) });
            text.Inlines.Add(oneLine ? new Run { Text = " " } : new LineBreak());
            text.MaxLines = oneLine ? 1 : 2;
            Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
        }

        text.Inlines.Add(new Run { Text = Title(e) });
        MakeOpenable(entry, e);
        return entry;
    }

    // ---- month with the agenda ([S-1263]) -------------------------------------------------------

    /// <summary>
    /// [S-1263] The month and the upcoming list together. Tall: the month in near-square cells of
    /// dots on top, the list under it. XL: the month with chips, the list beside it. The list is the
    /// month's own events from today (or from the 1st when paged away), so it needs no other fetch.
    /// </summary>
    private void DrawMonthWithAgenda(double width, double height, bool beside)
    {
        var grid = new Grid();
        double monthWidth;
        double monthHeight;
        double listWidth;
        double listHeight;

        if (beside)
        {
            listWidth = CalendarFit.BesideAgendaWidth;
            monthWidth = Math.Max(0, width - listWidth - Fluent.Space2);
            monthHeight = height;
            listHeight = height;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(listWidth) });
            grid.ColumnSpacing = Fluent.Space2;
        }
        else
        {
            monthWidth = width;
            monthHeight = WeekdayRow + (6 * CalendarFit.SquareCell(width)) + Fluent.Space1;
            listWidth = width;
            listHeight = Math.Max(0, height - monthHeight);
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(monthHeight) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        var month = BuildMonth(monthWidth, monthHeight, squareCells: !beside);
        grid.Children.Add(month);

        var (_, last) = Range();
        var from = InView(Today) ? Today : new DateOnly(_anchor.Year, _anchor.Month, 1);
        var groups = CalendarRules.Agenda(_events, _birthdays, from, last < from ? from : last, TimeZoneInfo.Local, _config.EffectiveAgendaCount, DateTimeOffset.Now);

        FrameworkElement list;
        if (groups.Count == 0)
        {
            var none = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
            none.Text = (from == Today ? Str.WidgetsA_Calendar_NothingElseThisMonth : Str.WidgetsA_Calendar_NoEvents).Text();
            none.HorizontalAlignment = HorizontalAlignment.Center;
            none.VerticalAlignment = VerticalAlignment.Center;
            list = none;
        }
        else
        {
            list = BuildAgenda(groups, listWidth, listHeight);
        }

        if (beside)
        {
            // A hairline between the two, as Windows' own calendar flyout draws one.
            var host = new Border
            {
                Child = list,
                BorderThickness = new Thickness(Fluent.Hairline, 0, 0, 0),
                BorderBrush = FluentLayout.Themed("DividerStrokeColorDefaultBrush", Colors.Gray),
            };
            SetColumn(host, 1);
            grid.Children.Add(host);
        }
        else
        {
            var host = new Border
            {
                Child = list,
                BorderThickness = new Thickness(0, Fluent.Hairline, 0, 0),
                BorderBrush = FluentLayout.Themed("DividerStrokeColorDefaultBrush", Colors.Gray),
            };
            SetRow(host, 1);
            grid.Children.Add(host);
        }

        _content.Children.Add(grid);
    }
}
