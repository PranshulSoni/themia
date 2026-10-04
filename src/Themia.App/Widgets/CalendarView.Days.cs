using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Themia.App.Shell;
using Themia.Core.Model;
using Themia.Core.Office;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>The week columns, the day timeline and the agenda list.</summary>
internal sealed partial class CalendarView
{
    /// <summary>The agenda's time column: "All day" at the caption step, on the 4px grid.</summary>
    private const double TimeColumn = 52;

    /// <summary>The all-day bar over the timeline: one chip line and its margin.</summary>
    private const double AllDayBar = 22;

    private const double HourLabelWidth = 48;

    // ---- week ([S-561]) ------------------------------------------------------------------------

    private void DrawWeek(double width, double height)
    {
        var start = CalendarRules.StartOfWeek(_anchor, _config.EffectiveWeekStart);
        var today = Today;
        var zone = TimeZoneInfo.Local;
        var showColors = ShowColors;

        var grid = new Grid { Padding = new Thickness(Fluent.Space1, 0, Fluent.Space1, Fluent.Space1) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // What a column can hold below its header; chips past it become "+N" ([S-572]).
        var available = height - 40 - Fluent.Space1;

        // [S-1265] A wide column spells the weekday and gives a timed chip its end and its place.
        var columnWidth = Math.Max(0, (width - (2 * Fluent.Space1)) / 7);
        var names = columnWidth >= CalendarFit.WeekdayNamesFrom;
        var detail = columnWidth >= CalendarFit.WeekDetailFrom;

        for (var c = 0; c < 7; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var day = start.AddDays(c);
            var header = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, Fluent.Space1) };

            var weekday = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            weekday.Text = names ? CalendarText.WeekdayName(day.DayOfWeek)[..3] : CalendarText.WeekdayInitial(day.DayOfWeek);
            weekday.HorizontalAlignment = HorizontalAlignment.Center;
            header.Children.Add(weekday);

            var number = Fluent.Text(FluentType.BodyStrong, day == today ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorPrimaryBrush");
            number.Text = day.Day.ToString(CultureInfo.InvariantCulture);
            number.HorizontalAlignment = HorizontalAlignment.Center;
            number.TextTrimming = TextTrimming.None;
            header.Children.Add(day == today
                ? new Border { Child = number, Background = Accent(), CornerRadius = new CornerRadius(10), MinWidth = 20, Padding = new Thickness(4, 0, 4, 0) }
                : number);

            SetColumn(header, c);
            grid.Children.Add(header);

            var column = new StackPanel { Spacing = 2, Margin = new Thickness(1, 0, 1, 0) };
            var items = CalendarRules.DayItems(_events, _birthdays, day, zone);
            var used = 0.0;
            var shown = 0;

            foreach (var item in items)
            {
                var chip = WeekChip(item, showColors, detail);
                var lines = item.Event is { AllDay: false } e ? 2 + (detail && e.Location is not null ? 1 : 0) : 1;
                var cost = (lines * FluentType.Caption.LineHeight) + 2 + 2;

                // Keep a line for "+N" when something will not fit.
                if (used + cost > available - (shown < items.Count - 1 ? 16 : 0))
                {
                    break;
                }

                column.Children.Add(chip);
                used += cost;
                shown++;
            }

            if (shown < items.Count)
            {
                var more = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
                more.Text = $"+{(items.Count - shown).ToString(CultureInfo.InvariantCulture)}";
                more.HorizontalAlignment = HorizontalAlignment.Center;
                column.Children.Add(more);
            }

            ToolTipService.SetToolTip(column, items.Count == 0 ? null : DayTooltip(day, items));
            SetRow(column, 1);
            SetColumn(column, c);
            grid.Children.Add(column);
        }

        _content.Children.Add(grid);
    }

    /// <summary>
    /// [S-561] A timed chip stacks its start time above the title, so the title keeps the column's
    /// width; a wide column ([S-1265]) shows the end time too and puts the place under the title.
    /// </summary>
    private Border WeekChip(DayItem item, bool showColors, bool detail)
    {
        var stack = new StackPanel { Spacing = 0 };
        var chip = new Border
        {
            Child = stack,
            Padding = new Thickness(3, 1, 2, 1),
            CornerRadius = new CornerRadius(2),
            Background = FluentLayout.Themed("SubtleFillColorSecondaryBrush", Colors.Gray),
        };

        if (item.Birthday is { } b)
        {
            var name = Fluent.Text(FluentType.Caption);
            name.Text = BirthdayRules.FirstName(b.Name);
            stack.Children.Add(name);
            chip.BorderThickness = new Thickness(2, 0, 0, 0);
            chip.BorderBrush = BirthdayBrush();
            return chip;
        }

        var e = item.Event!;
        if (!e.AllDay)
        {
            var time = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
            time.Text = detail && e.End > e.Start ? $"{Clock(e.Start)}–{Clock(e.End)}" : Clock(e.Start);
            Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
            stack.Children.Add(time);
        }

        var title = Fluent.Text(FluentType.Caption);
        title.Text = Title(e);
        stack.Children.Add(title);

        if (detail && !e.AllDay && e.Location is { } place)
        {
            var where = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            where.Text = place;
            stack.Children.Add(where);
        }

        if (showColors)
        {
            chip.BorderThickness = new Thickness(2, 0, 0, 0);
            chip.BorderBrush = ColorOf(e);
        }

        MakeOpenable(chip, e);
        return chip;
    }

    // ---- day ([S-573]) -------------------------------------------------------------------------

    private void DrawDay(double width, double height)
    {
        var day = _anchor;
        var zone = TimeZoneInfo.Local;
        var showColors = ShowColors;
        var items = CalendarRules.DayItems(_events, _birthdays, day, zone);
        var allDay = items.Where(i => i.Birthday is not null || i.Event!.AllDay).ToList();
        var timed = items.Where(i => i.Event is { AllDay: false }).Select(i => i.Event!).ToList();

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        if (allDay.Count > 0)
        {
            // The all-day bar: chips side by side, sharing the width, each trimmed to its share.
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Fluent.Space1, Margin = new Thickness(Fluent.Space2, 0, Fluent.Space2, Fluent.Space1) };
            var share = Math.Min(160, (width - (2 * Fluent.Space2) - (Fluent.Space1 * (allDay.Count - 1))) / allDay.Count);

            foreach (var item in allDay)
            {
                var chip = AllDayChip(item, day, showColors);
                chip.MaxWidth = Math.Max(24, share);
                bar.Children.Add(chip);
            }

            SetRow(bar, 0);
            root.Children.Add(bar);
        }

        // [S-1266] Compact hours when that shows the whole day unscrolled; list-row hours otherwise.
        var hourHeight = CalendarFit.HourHeight(height - (allDay.Count > 0 ? AllDayBar : 0) - Fluent.Space1 - Fluent.Space2);
        var timelineHeight = (CalendarRules.TimelineEndHour - CalendarRules.TimelineFirstHour) * hourHeight;
        var timeline = new Grid { Height = timelineHeight };
        timeline.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HourLabelWidth) });
        timeline.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var canvas = new Canvas();
        SetColumn(canvas, 1);

        for (var h = CalendarRules.TimelineFirstHour; h < CalendarRules.TimelineEndHour; h++)
        {
            var top = (h - CalendarRules.TimelineFirstHour) * hourHeight;

            var label = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            label.Text = h.ToString("00", CultureInfo.InvariantCulture) + ":00";
            label.Margin = new Thickness(Fluent.Space2, top + 1, 0, 0);
            label.VerticalAlignment = VerticalAlignment.Top;
            label.TextTrimming = TextTrimming.None;
            timeline.Children.Add(label);

            var rule = new Border
            {
                Height = Fluent.Hairline,
                Background = FluentLayout.Themed("DividerStrokeColorDefaultBrush", Colors.Gray),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, top, Fluent.Space2, 0),
            };
            SetColumn(rule, 1);
            timeline.Children.Add(rule);
        }

        var laneWidth = Math.Max(0, width - HourLabelWidth - Fluent.Space2);
        var lanes = CalendarRules.Lanes([.. timed.Select(e => (e.Start.UtcTicks, Math.Max(e.End.UtcTicks, e.Start.UtcTicks)))]);

        for (var i = 0; i < timed.Count; i++)
        {
            var e = timed[i];
            var (column, columns) = lanes[i];
            var (top, span) = CalendarRules.TimelineSpan(e.Start, e.End, day, zone);
            var blockHeight = Math.Max(18, (span * timelineHeight) - 1);
            var block = TimedBlock(e, showColors, blockHeight);

            block.Width = Math.Max(0, (laneWidth / columns) - 2);
            block.Height = blockHeight;
            Canvas.SetLeft(block, column * (laneWidth / columns));
            Canvas.SetTop(block, top * timelineHeight);
            canvas.Children.Add(block);
        }

        timeline.Children.Add(canvas);

        var scroll = new ScrollViewer
        {
            Content = timeline,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Padding = new Thickness(0, Fluent.Space1, 0, Fluent.Space2),
        };
        SetRow(scroll, 1);
        root.Children.Add(scroll);

        if (timed.Count == 0 && allDay.Count == 0)
        {
            var none = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
            none.Text = day == Today ? "No events today" : "No events";
            none.HorizontalAlignment = HorizontalAlignment.Center;
            none.VerticalAlignment = VerticalAlignment.Center;
            SetRow(none, 1);
            root.Children.Add(none);
        }

        // Open on the part of the day that matters: the first event, else now, else the morning.
        var focusHour = timed.Count > 0
            ? timed.Min(e => e.Start.ToLocalTime().Hour)
            : day == Today ? DateTime.Now.Hour : CalendarRules.TimelineFirstHour + 1;
        var offset = Math.Max(0, (focusHour - CalendarRules.TimelineFirstHour - 0.5) * hourHeight);
        scroll.Loaded += (_, _) => scroll.ChangeView(null, offset, null, disableAnimation: true);

        _content.Children.Add(root);
    }

    private Border AllDayChip(DayItem item, DateOnly day, bool showColors)
    {
        var text = Fluent.Text(FluentType.Caption);
        var chip = new Border
        {
            Child = text,
            Padding = new Thickness(Fluent.Space1, 1, Fluent.Space1, 1),
            CornerRadius = new CornerRadius(2),
            Background = FluentLayout.Themed("SubtleFillColorSecondaryBrush", Colors.Gray),
        };

        if (item.Birthday is { } b)
        {
            text.Text = $"Birthday: {BirthdayRules.Label(b, day)}";
            chip.BorderThickness = new Thickness(2, 0, 0, 0);
            chip.BorderBrush = BirthdayBrush();
            ToolTipService.SetToolTip(chip, text.Text);
            return chip;
        }

        var e = item.Event!;
        text.Text = Title(e);

        if (showColors)
        {
            chip.BorderThickness = new Thickness(2, 0, 0, 0);
            chip.BorderBrush = ColorOf(e);
        }

        MakeOpenable(chip, e);
        return chip;
    }

    /// <summary>
    /// [S-573] A timed event on the timeline: title, then times; a block tall enough for a third
    /// line ([S-1266]) adds where and who.
    /// </summary>
    private Border TimedBlock(CalendarEvent e, bool showColors, double height)
    {
        var stack = new StackPanel { Spacing = 0 };

        var title = Fluent.Text(FluentType.Caption);
        title.Text = Title(e);
        title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;

        var time = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
        time.Text = e.End > e.Start ? $"{Clock(e.Start)}–{Clock(e.End)}" : Clock(e.Start);
        Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);

        stack.Children.Add(title);
        stack.Children.Add(time);

        var meta = string.Join(" · ", new[] { e.Location, e.Organizer }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (meta.Length > 0 && height >= 3 * FluentType.Caption.LineHeight)
        {
            var where = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            where.Text = meta;
            stack.Children.Add(where);
        }

        var block = new Border
        {
            Child = stack,
            Padding = new Thickness(Fluent.Space1, 0, Fluent.Space1, 0),
            CornerRadius = new CornerRadius(Fluent.ControlRadius),
            Background = FluentLayout.Themed("SubtleFillColorSecondaryBrush", Colors.Gray),
            BorderThickness = new Thickness(2, 0, 0, 0),
            BorderBrush = showColors ? ColorOf(e) : Accent(),
        };

        MakeOpenable(block, e);
        return block;
    }


    // ---- agenda ([S-571]) ----------------------------------------------------------------------

    /// <summary>Below this width the time column folds into the line under the title.</summary>
    private const double AgendaNarrowBelow = 260;

    private void DrawAgenda(double width, double height)
    {
        var (first, last) = CalendarRules.ViewRange(CalendarViewMode.Agenda, _anchor, Today, _config.EffectiveWeekStart);
        var groups = CalendarRules.Agenda(_events, _birthdays, first, last, TimeZoneInfo.Local, _config.EffectiveAgendaCount, DateTimeOffset.Now);

        if (groups.Count == 0)
        {
            _state.Show("No upcoming events", offerAccounts: false);
            return;
        }

        _content.Children.Add(BuildAgenda(groups, width, height));
    }

    /// <summary>
    /// [S-571] The agenda list for a box, scrolling. A short box drops the first "Today" heading
    /// ([S-1267]); a wide one gives location and organizer columns of their own, and from
    /// <see cref="CalendarFit.AgendaTwoColumnsFrom"/> the days flow into two columns.
    /// </summary>
    private ScrollViewer BuildAgenda(IReadOnlyList<(DateOnly Day, IReadOnlyList<DayItem> Items)> groups, double width, double height)
    {
        var today = Today;
        var compact = height > 0 && height < Fluent.CompactBelow;
        var columns = width >= CalendarFit.AgendaTwoColumnsFrom ? 2 : 1;
        var columnWidth = columns == 1 ? width : (width - Fluent.Space2) / 2;
        var narrow = columnWidth > 0 && columnWidth < AgendaNarrowBelow;
        var metaColumns = columnWidth >= CalendarFit.AgendaColumnsFrom && !compact;
        var showColors = ShowColors;
        var now = DateTimeOffset.Now;

        // Split the days between the columns by what they hold (a heading and its rows), whole days only.
        var placement = columns == 1
            ? new int[groups.Count]
            : ColumnLayout.Partition([.. groups.Select(g => g.Items.Count + 1)], columns);

        var stacks = Enumerable.Range(0, columns).Select(_ => new StackPanel { Spacing = 0 }).ToArray();

        for (var g = 0; g < groups.Count; g++)
        {
            var (day, items) = groups[g];
            var list = stacks[placement[g]];

            if (CalendarFit.ShowsHeading(day, today, g == 0, height))
            {
                var heading = Fluent.Text(FluentType.BodyStrong, "TextFillColorSecondaryBrush");
                heading.Text = Formatting.DayHeading(day, today);
                list.Children.Add(new Border { Child = heading, Padding = new Thickness(Fluent.Space4, Fluent.Space2, Fluent.Space4, Fluent.Space1) });
            }

            foreach (var item in items)
            {
                var row = AgendaRow(item, day, now, compact, narrow, metaColumns, showColors);
                _hovers.Add(Fluent.Hover(row));
                list.Children.Add(row);
            }
        }

        UIElement content = stacks[0];
        if (columns > 1)
        {
            var grid = new Grid { ColumnSpacing = Fluent.Space2 };
            for (var c = 0; c < columns; c++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                SetColumn(stacks[c], c);
                grid.Children.Add(stacks[c]);
            }

            content = grid;
        }

        return new ScrollViewer
        {
            Padding = Fluent.ListPadding,
            Content = content,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
    }

    /// <summary>
    /// [S-571] One agenda row: the time column (start over end, "All day", or "Now" in the strong
    /// step for one under way), then the title over where and who. On a narrow tile ([S-574]) the
    /// time moves under the title, where it costs a line rather than half the width; on a wide one
    /// ([S-1267]) where and who get columns of their own beside the title.
    /// </summary>
    private Border AgendaRow(DayItem item, DateOnly day, DateTimeOffset now, bool compact, bool narrow, bool metaColumns, bool showColors)
    {
        string? location = null;
        string? organizer = null;
        var when = new StackPanel { Spacing = 0, Width = TimeColumn, Margin = new Thickness(0, 0, Fluent.Space2, 0), VerticalAlignment = VerticalAlignment.Top };
        var text = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        var title = Fluent.Text(FluentType.BodyStrong);
        text.Children.Add(title);

        Brush? stripe = null;
        string? second;

        if (item.Birthday is { } b)
        {
            var gift = Fluent.Icon(GiftGlyph, "SystemFillColorCautionBrush");
            gift.HorizontalAlignment = HorizontalAlignment.Left;
            when.Children.Add(gift);
            title.Text = BirthdayRules.Label(b, day);
            second = compact && !narrow ? null : "Birthday";
            stripe = BirthdayBrush();
        }
        else
        {
            var e = item.Event!;
            var running = AgendaRules.IsInProgress(e.AllDay, e.Start, e.End, now);
            var startText = e.AllDay ? "All day" : running ? "Now" : Clock(e.Start);
            var start = Fluent.Text(running ? FluentType.BodyStrong : FluentType.Body, "TextFillColorSecondaryBrush");
            start.Text = startText;
            start.TextTrimming = TextTrimming.None;
            Typography.SetNumeralAlignment(start, FontNumeralAlignment.Tabular);
            when.Children.Add(start);

            if (!e.AllDay && !compact && e.End > e.Start)
            {
                var end = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
                end.Text = Clock(e.End);
                Typography.SetNumeralAlignment(end, FontNumeralAlignment.Tabular);
                when.Children.Add(end);
            }

            title.Text = Title(e);

            var meta = string.Join(" · ", new[] { e.Location, e.Organizer }.Where(s => !string.IsNullOrWhiteSpace(s)));
            var span = e.AllDay || running || e.End <= e.Start ? startText : $"{startText}–{Clock(e.End)}";

            second = narrow
                ? (compact || meta.Length == 0 ? span : $"{span} · {meta}")
                : (compact || meta.Length == 0 || metaColumns ? null : meta);

            if (metaColumns)
            {
                location = e.Location;
                organizer = e.Organizer;
            }

            if (showColors)
            {
                stripe = ColorOf(e);
            }
        }

        if (second is not null)
        {
            var detail = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            detail.Text = second;
            Typography.SetNumeralAlignment(detail, FontNumeralAlignment.Tabular);
            text.Children.Add(detail);
        }

        var body = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
            Padding = new Thickness(0, Fluent.Space1, 0, Fluent.Space1),
        };

        if (metaColumns)
        {
            // [S-1267] Where, then who, each in its own column, tertiary like the line they replace.
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.6, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.45, GridUnitType.Star) });

            for (var i = 0; i < 2; i++)
            {
                var cell = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
                cell.Text = (i == 0 ? location : organizer) ?? string.Empty;
                cell.VerticalAlignment = VerticalAlignment.Center;
                cell.Margin = new Thickness(Fluent.Space2, 0, 0, 0);
                SetColumn(cell, 3 + i);
                body.Children.Add(cell);
            }
        }

        // [S-566] The calendar's colour as a stripe before the time, when colours show; a birthday's
        // is the caution hue, as its chips are.
        var bar = new Border
        {
            Width = 2,
            Margin = new Thickness(0, 2, Fluent.Space2 - 2, 2),
            CornerRadius = new CornerRadius(1),
            Background = stripe ?? FluentLayout.Themed("SubtleFillColorTransparentBrush", Colors.Transparent),
        };

        SetColumn(bar, 0);
        SetColumn(when, 1);
        SetColumn(text, 2);
        body.Children.Add(bar);
        body.Children.Add(text);

        if (!narrow)
        {
            body.Children.Add(when);
        }

        var row = Fluent.Row(body, compact);
        row.Padding = new Thickness(Fluent.Space4 - Fluent.Space2, 0, narrow ? Fluent.Space2 : Fluent.Space4, 0);

        if (item.Event is { } ev)
        {
            MakeOpenable(row, ev);
        }

        return row;
    }
}
