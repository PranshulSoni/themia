using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Shell;
using Themia.Core.Office;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>[S-572] The month grid: six weeks of days, each with as much of its events as fits.</summary>
internal sealed partial class CalendarView
{
    /// <summary>The weekday header row: one caption line.</summary>
    private const double WeekdayRow = 16;

    /// <summary>A day number's line, and one chip's line with its gap.</summary>
    private const double NumberLine = 16;

    private const double ChipLine = 17;

    /// <summary>How many coloured dots a cell of this render's width holds before the muted one.</summary>
    private int _dotLimit = 3;

    private void DrawMonth(double width, double height)
    {
        var start = _config.EffectiveWeekStart;
        var today = Today;
        var cells = CalendarRules.MonthCells(_anchor, start);
        var showColors = ShowColors;
        var zone = TimeZoneInfo.Local;

        var grid = new Grid { Padding = new Thickness(Fluent.Space1, 0, Fluent.Space1, Fluent.Space1) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(WeekdayRow) });

        for (var r = 0; r < 6; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        for (var c = 0; c < 7; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        // [S-564] The header follows the week start: Mo..Su or Su..Sa.
        var order = CalendarRules.WeekdayOrder(start);
        for (var c = 0; c < 7; c++)
        {
            var label = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            label.Text = CalendarText.WeekdayInitial(order[c]);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            SetColumn(label, c);
            grid.Children.Add(label);
        }

        var cellWidth = Math.Max(0, (width - (2 * Fluent.Space1)) / 7);
        var cellHeight = Math.Max(0, (height - WeekdayRow - Fluent.Space1) / 6);
        var density = CalendarRules.MonthCellDensity(cellWidth, cellHeight);
        var capacity = CalendarRules.ChipCapacity(cellHeight, NumberLine, ChipLine);

        // Four dots and their gaps are 26px; a narrower cell takes two and the muted one.
        _dotLimit = cellWidth >= 32 ? 3 : 2;

        for (var i = 0; i < cells.Count; i++)
        {
            var day = cells[i];
            var items = CalendarRules.DayItems(_events, _birthdays, day, zone);
            var cell = BuildDayCell(day, items, day.Month == _anchor.Month, day == today, density, capacity, showColors);

            SetRow(cell, 1 + (i / 7));
            SetColumn(cell, i % 7);
            grid.Children.Add(cell);
        }

        _content.Children.Add(grid);
    }

    private Border BuildDayCell(
        DateOnly day,
        IReadOnlyList<DayItem> items,
        bool inMonth,
        bool isToday,
        MonthDensity density,
        int capacity,
        bool showColors)
    {
        var stack = new StackPanel { Spacing = 1 };

        // The number row carries the overflow count on the right, so "+N" never costs a chip line.
        var numberRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };

        var number = Fluent.Text(
            FluentType.Caption,
            isToday ? "TextOnAccentFillColorPrimaryBrush" : inMonth ? "TextFillColorPrimaryBrush" : "TextFillColorDisabledBrush");
        number.Text = day.Day.ToString(CultureInfo.InvariantCulture);
        Typography.SetNumeralAlignment(number, FontNumeralAlignment.Tabular);
        number.TextTrimming = TextTrimming.None;

        // A day with something on it has its number in the strong weight: in the barest density
        // that is the one sign left, and it is not colour alone.
        if (items.Count > 0)
        {
            number.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        }

        FrameworkElement numberHost = number;
        if (isToday)
        {
            // [S-572] Today is the accent disc Windows' own calendar flyout uses.
            numberHost = new Border
            {
                Child = number,
                Background = Accent(),
                CornerRadius = new CornerRadius(NumberLine / 2),
                MinWidth = NumberLine,
                Height = NumberLine,
                Padding = new Thickness(3, 0, 3, 0),
            };
            number.HorizontalAlignment = HorizontalAlignment.Center;
        }

        numberHost.HorizontalAlignment = density == MonthDensity.Chips ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        numberRow.Children.Add(numberHost);

        if (density == MonthDensity.Chips && items.Count > capacity)
        {
            var more = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            more.Text = $"+{(items.Count - capacity).ToString(CultureInfo.InvariantCulture)}";
            more.HorizontalAlignment = HorizontalAlignment.Right;
            more.TextTrimming = TextTrimming.None;
            SetColumn(more, 1);
            numberRow.Children.Add(more);
        }

        if (density != MonthDensity.Chips)
        {
            // Without chips the number is the cell, so it is centred like a date picker's.
            numberRow.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        }

        stack.Children.Add(numberRow);

        switch (density)
        {
            case MonthDensity.Chips:
                foreach (var item in items.Take(capacity))
                {
                    stack.Children.Add(MonthChip(item, showColors));
                }

                break;

            case MonthDensity.Dots when items.Count > 0:
                stack.Children.Add(Dots(items, showColors));
                break;
        }

        var cell = new Border
        {
            Child = stack,

            // A bare number takes the whole cell: on a short tile the row is the number's own line,
            // and any padding would clip today's disc.
            Padding = density == MonthDensity.Number ? new Thickness(0) : new Thickness(2, 2, 2, 0),
            Margin = density == MonthDensity.Number ? new Thickness(1, 0, 1, 0) : new Thickness(1),
            CornerRadius = new CornerRadius(Fluent.ControlRadius),

            // Chips are clipped whole at the cell edge rather than squeezed ([S-572], classic S-45).
            Background = FluentLayout.Themed("SubtleFillColorTransparentBrush", Colors.Transparent),
        };

        ToolTipService.SetToolTip(cell, items.Count == 0 ? null : DayTooltip(day, items));
        return cell;
    }

    /// <summary>One event or birthday as a single-line chip, titled, coloured by its calendar when colours show.</summary>
    private Border MonthChip(DayItem item, bool showColors)
    {
        var text = Fluent.Text(FluentType.Caption);
        var chip = new Border
        {
            Child = text,
            Height = ChipLine - 1,
            Padding = new Thickness(3, 0, 2, 0),
            CornerRadius = new CornerRadius(2),
            Background = FluentLayout.Themed("SubtleFillColorSecondaryBrush", Colors.Gray),
        };

        if (item.Birthday is { } b)
        {
            text.Text = BirthdayRules.FirstName(b.Name);
            chip.BorderThickness = new Thickness(2, 0, 0, 0);
            chip.BorderBrush = BirthdayBrush();
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
    /// [S-572] Up to three dots, then a muted one for more — two and the muted one in a cell too
    /// narrow for four, so the row never runs past the cell.
    /// </summary>
    private StackPanel Dots(IReadOnlyList<DayItem> items, bool showColors)
    {
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
        var limit = _dotLimit;

        foreach (var item in items.Take(limit))
        {
            dots.Children.Add(Dot(item.Birthday is not null ? BirthdayBrush() : showColors ? ColorOf(item.Event!) : Accent()));
        }

        if (items.Count > limit)
        {
            dots.Children.Add(Dot(FluentLayout.Themed("TextFillColorDisabledBrush", Colors.Gray)));
        }

        return dots;
    }

    private static Ellipse Dot(Brush fill) => new() { Width = 5, Height = 5, Fill = fill };

    private static string DayTooltip(DateOnly day, IReadOnlyList<DayItem> items)
    {
        var lines = new List<string> { $"{CalendarText.WeekdayName(day.DayOfWeek)} {Formatting.ShortDate(day)}" };

        foreach (var item in items)
        {
            if (item.Birthday is { } b)
            {
                lines.Add($"Birthday: {BirthdayRules.Label(b, day)}");
            }
            else if (item.Event is { } e)
            {
                lines.Add(e.AllDay ? $"All day  {Title(e)}" : $"{Clock(e.Start)}  {Title(e)}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }
}
