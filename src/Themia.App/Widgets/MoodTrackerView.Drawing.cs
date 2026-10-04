using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Shell;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>The Mood widget's drawing: the map, the day menu, the insights, the faces.</summary>
internal sealed partial class MoodTrackerView
{
    // -----------------------------------------------------------------------------------------
    // The map
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Lay the squares out for the plan, then colour them. The grid is rebuilt only when its
    /// <i>shape</i> changed (orientation, weeks, square size, labels, first day); otherwise the
    /// squares already drawn are re-lettered.
    /// </summary>
    private void DrawMap(IReadOnlyList<MoodEntry> entries, IReadOnlyDictionary<LocalDay, MoodDay> byDay)
    {
        // [S-1573] A month is drawn whole, or — on a tile too small for it — replaced by the most
        // recent weeks (the plan says which, and the header says so). Never a month with weeks missing.
        var grid = _config.Span == MoodSpan.Month && !_plan.Recent
            ? MoodGrid.Month(_today.Year, _today.Month, _config.FirstDayOfWeek)
            : MoodGrid.Rolling(_today, _plan.Weeks, _config.FirstDayOfWeek);

        _grid = grid;
        DrawCaption();

        if (!_area.IsMeasured)
        {
            return; // Not measured yet; ApplySize brings us straight back.
        }

        var split = _plan.Arrangement == MoodArrangement.Split;
        var shape = string.Create(CultureInfo.InvariantCulture,
            $"{split}|{grid.Rows.Count}|{grid.WeekStart}|{grid.First.Key}|{_plan.Cell:0.0}|{_plan.Labels}");

        if (shape != _shape)
        {
            _shape = shape;
            BuildCells(grid, split);
        }

        // [S-1450] Every entry by day, oldest first, for the tooltips.
        var perDay = new Dictionary<LocalDay, List<MoodEntry>>();
        foreach (var entry in entries)
        {
            var day = LocalDay.From(entry.At);
            if (!perDay.TryGetValue(day, out var list))
            {
                perDay[day] = list = [];
            }

            list.Add(entry);
        }

        foreach (var cell in _dayCells)
        {
            var day = grid.Rows[cell.Week][cell.Weekday];
            cell.Day = day;

            if (day is not { } shown)
            {
                cell.ShowBlank();
                continue;
            }

            var mood = MoodRules.Read(byDay, shown);
            var logged = perDay.TryGetValue(shown, out var dayEntries) ? dayEntries : [];
            logged.Sort(static (left, right) => left.At.CompareTo(right.At));

            var tooltip = MoodInsights.Tooltip(mood, logged);
            cell.Show(mood, shown == _today, shown == _today ? Str.WidgetsB_Mood_TodayTip.Format(("tooltip", tooltip)) : tooltip);
        }
    }

    /// <summary>
    /// [S-1573] The header's note on what the map shows: the month's name, or "This week" / "Last N
    /// weeks" when the month did not fit. Also the map's tooltip and accessible name, because a card
    /// header too narrow for a label (XS) draws none.
    /// </summary>
    private void DrawCaption()
    {
        var caption = MoodLayout.Caption(_plan, _config, _today.Month);

        _caption.Text = caption ?? string.Empty;
        _caption.Visibility = caption is null ? Visibility.Collapsed : Visibility.Visible;

        var named = _plan.Recent && caption is not null
            ? Str.WidgetsB_Mood_MonthDoesNotFit.Format(("caption", caption))
            : caption;

        ToolTipService.SetToolTip(_cells, named);
        AutomationProperties.SetName(_cells, named ?? Str.WidgetsB_Mood_Map.Text());
    }

    /// <summary>
    /// Build the squares. Stacked: weeks are rows, the weekdays columns, headers on top. Split: weeks
    /// are columns, weekdays rows, the weekday names down the left and month names along the top.
    /// </summary>
    private void BuildCells(MoodGrid grid, bool split)
    {
        ClearCells();

        var cell = _plan.Cell;
        var labels = _plan.Labels;
        var weeks = grid.Rows.Count;
        var rows = split ? MoodGrid.Columns : weeks;
        var columns = split ? weeks : MoodGrid.Columns;
        var rowOffset = labels ? 1 : 0;
        var columnOffset = split && labels ? 1 : 0;

        if (labels)
        {
            _cells.RowDefinitions.Add(new RowDefinition { Height = new GridLength(MoodLayout.Line) });
        }

        if (columnOffset == 1)
        {
            _cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MoodLayout.WeekdayLabelWidth) });
        }

        for (var r = 0; r < rows; r++)
        {
            _cells.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cell) });
        }

        for (var c = 0; c < columns; c++)
        {
            _cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cell) });
        }

        if (labels)
        {
            if (split)
            {
                for (var r = 0; r < MoodGrid.Columns; r++)
                {
                    AddLabel(grid.Headers[r], rowOffset + r, 0, 1, TextAlignment.Left);
                }

                // A month's name over the week its first day falls in (and over the first week).
                for (var w = 0; w < weeks; w++)
                {
                    var first = grid.Rows[w].FirstOrDefault(d => d is not null);
                    var monthStart = grid.Rows[w].FirstOrDefault(d => d is { Day: 1 });
                    var named = monthStart ?? (w == 0 ? first : null);

                    if (named is { } day)
                    {
                        AddLabel(MoodInsights.MonthName(day.Month), 0, columnOffset + w, Math.Min(3, weeks - w), TextAlignment.Left);
                    }
                }
            }
            else
            {
                for (var c = 0; c < MoodGrid.Columns; c++)
                {
                    AddLabel(grid.Headers[c], 0, c, 1, TextAlignment.Center);
                }
            }
        }

        var digits = cell >= MoodLayout.LabelCell;

        for (var w = 0; w < weeks; w++)
        {
            for (var d = 0; d < MoodGrid.Columns; d++)
            {
                var dayCell = new DayCell(w, d, cell, digits, _accent, _emptyFill, _onAccent, _onCard, _ring, DayMenu);
                _dayCells.Add(dayCell);

                SetRow(dayCell.Host, rowOffset + (split ? d : w));
                SetColumn(dayCell.Host, columnOffset + (split ? w : d));
                _cells.Children.Add(dayCell.Host);
            }
        }
    }

    private void AddLabel(string text, int row, int column, int span, TextAlignment alignment)
    {
        var label = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
        label.Text = text;
        label.TextAlignment = alignment;
        label.VerticalAlignment = VerticalAlignment.Center;
        SetRow(label, row);
        SetColumn(label, column);
        SetColumnSpan(label, span);
        _cells.Children.Add(label);
    }

    private void ClearCells()
    {
        foreach (var cell in _dayCells)
        {
            cell.Detach();
        }

        _dayCells.Clear();
        _cells.Children.Clear();
        _cells.RowDefinitions.Clear();
        _cells.ColumnDefinitions.Clear();
    }

    /// <summary>
    /// [S-1451] A logged day's menu: its date, then one entry per mood (time, level, thought), each
    /// with "Delete this entry" one level down so a stray click on the list cannot destroy one. A day
    /// with nothing on it opens no menu (the card's opens instead).
    /// </summary>
    private IEnumerable<ItemMenuEntry> DayMenu(LocalDay? day)
    {
        if (_detached || day is not { } shown)
        {
            return [];
        }

        var logged = MoodInsights.EntriesOn(MoodRules.Sanitise(_config.Entries), shown);

        if (logged.Count == 0)
        {
            return [];
        }

        var menu = new List<ItemMenuEntry>
        {
            new(null, CalendarText.FullDate(shown), static () => { }, Disabled: true),
            ItemMenuEntry.Separator,
        };

        foreach (var entry in logged)
        {
            var id = entry.Id;
            menu.Add(new(null, MoodInsights.EntryLine(entry), static () => { },
                Children: [new(Glyphs.Delete, Str.WidgetsB_Mood_DeleteEntry.Text(), () => DeleteEntry(id), Danger: true)]));
        }

        return menu;
    }

    // -----------------------------------------------------------------------------------------
    // The insights ([S-1454])
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The name column of the insight bars: wide enough for the longest level name in the current
    /// language ("Schrecklich" is twice "Awful"), never narrower than the English layout's 44.
    /// </summary>
    private static double InsightNameWidth
        => Math.Max(44, FluentType.EstimateWidth(FluentType.Caption, MoodRules.LevelNames.Max(name => name.Length)));

    private const double InsightValueWidth = 28;

    /// <summary>
    /// The split tile's extras: how often each level was logged and the average per weekday (left
    /// pane), and the latest thoughts under the map — all over the window the map shows.
    /// </summary>
    private void DrawInsights(IReadOnlyList<MoodEntry> entries)
    {
        _insights.Children.Clear();
        _recent.Children.Clear();

        if (_plan.Arrangement != MoodArrangement.Split || _grid is not { } grid)
        {
            return;
        }

        var barRoom = Math.Max(0, _plan.LeftWidth - InsightNameWidth - InsightValueWidth - (2 * Fluent.Space2));

        if (_plan.Distribution)
        {
            var counts = MoodInsights.Distribution(entries, grid.First, grid.Last);
            var most = Math.Max(1, counts.Max());
            var section = Section(Str.WidgetsB_Mood_HowItWent.Text(), spacing: 2);

            for (var i = 0; i < counts.Length; i++)
            {
                var level = MoodRules.MinLevel + i;
                section.Children.Add(BarRow(MoodRules.LevelName(level), barRoom * counts[i] / most, Wash[level],
                    LocaleFormat.Integer(counts[i])));
            }

            _insights.Children.Add(section);
        }

        if (_plan.Weekdays)
        {
            var averages = MoodInsights.WeekdayAverages(entries, grid.First, grid.Last, _config.FirstDayOfWeek);
            var section = Section(Str.WidgetsB_Mood_ByWeekday.Text(), spacing: 0);

            for (var i = 0; i < averages.Length; i++)
            {
                var average = averages[i];
                section.Children.Add(BarRow(grid.Headers[i],
                    average is { } a ? barRoom * a / MoodRules.MaxLevel : 0,
                    average is { } b ? Wash[Math.Clamp(MoodRules.Bucket(b), 1, MoodRules.MaxLevel)] : 0,
                    average is { } c ? LocaleFormat.Number(c, 1) : "–"));
            }

            _insights.Children.Add(section);
        }

        if (_plan.Summary)
        {
            var summary = MoodInsights.Summary(entries, grid.First, grid.Last, _today);
            var section = Section(Str.WidgetsB_Mood_InThisWindow.Text(), spacing: 0);
            section.Children.Add(SummaryLine(Str.WidgetsB_Mood_LoggedDays.Plural(summary.Days, ("logged", summary.LoggedDays))));
            section.Children.Add(SummaryLine(Str.WidgetsB_Mood_BestRunDays.Plural(summary.BestRun)));
            section.Children.Add(SummaryLine(Str.WidgetsB_Mood_ThoughtsWritten.Plural(summary.Thoughts)));
            _insights.Children.Add(section);
        }

        if (_plan.RecentNotes > 0)
        {
            _recent.Margin = new Thickness(0, MoodLayout.Gap, 0, 0);
            _recent.Width = _plan.MapWidth;

            var title = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
            title.Text = Str.WidgetsB_Mood_RecentThoughts.Text();
            _recent.Children.Add(title);

            var notes = MoodInsights.RecentNotes(entries, _plan.RecentNotes);

            if (notes.Count == 0)
            {
                var none = Fluent.Text(FluentType.Body, "TextFillColorTertiaryBrush");
                none.Text = Str.WidgetsB_Mood_NoThoughts.Text();
                none.MaxLines = 1;
                none.TextTrimming = TextTrimming.CharacterEllipsis;
                _recent.Children.Add(none);
            }

            foreach (var note in notes)
            {
                var line = Fluent.Text(FluentType.Body);
                line.Text = MoodInsights.RecentLine(note);
                line.MaxLines = 1;
                line.TextTrimming = TextTrimming.CharacterEllipsis;
                ToolTipService.SetToolTip(line, line.Text);
                _recent.Children.Add(line);
            }
        }
    }

    private static TextBlock SummaryLine(string text)
    {
        var line = Fluent.Text(FluentType.Caption);
        line.Text = text;
        line.Height = MoodLayout.Line;
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(line, FontNumeralAlignment.Tabular);
        return line;
    }

    private static StackPanel Section(string title, double spacing)
    {
        var panel = new StackPanel { Spacing = spacing };
        var heading = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
        heading.Text = title;
        heading.Height = MoodLayout.Line;
        panel.Children.Add(heading);
        return panel;
    }

    /// <summary>One line of an insight: a name, a bar in the heatmap's own wash, a value.</summary>
    private Grid BarRow(string name, double barWidth, double wash, string value)
    {
        var label = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
        label.Text = name;
        label.MaxLines = 1;

        var bar = new Rectangle
        {
            Fill = _accent,
            Opacity = Math.Max(wash, 0.22),
            Width = Math.Max(0, barWidth),
            Height = Fluent.Space1 + 2,
            RadiusX = 2,
            RadiusY = 2,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var number = Fluent.Text(FluentType.Caption);
        number.Text = value;
        number.TextAlignment = TextAlignment.Right;
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(number, FontNumeralAlignment.Tabular);

        var row = new Grid
        {
            Height = MoodLayout.Line,
            ColumnSpacing = Fluent.Space2,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(InsightNameWidth) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(InsightValueWidth) },
            },
        };

        SetColumn(label, 0);
        SetColumn(bar, 1);
        SetColumn(number, 2);
        row.Children.Add(label);
        row.Children.Add(bar);
        row.Children.Add(number);

        AutomationProperties.SetName(row, Str.WidgetsB_Mood_BarName.Format(("name", name), ("value", value)));
        return row;
    }

    // -----------------------------------------------------------------------------------------
    // Parts
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// [S-1449] One point of the scale: a swatch from the heatmap's ramp with a drawn face on it — a
    /// circle, two eyes and a mouth curving from a frown (awful) through flat (OK) to a smile (great).
    /// The level's number and name are the tooltip and the accessible name.
    /// </summary>
    private sealed class LevelButton
    {
        private const double FaceSize = 18;

        private readonly Action<int> _picked;

        public LevelButton(int level, Brush accent, Brush emptyFill, Brush onAccent, Brush onCard, double wash, Action<int> picked)
        {
            Level = level;
            _picked = picked;

            var fill = new Rectangle
            {
                Fill = accent,

                // The opacity is on the shape: the accent brush is shared with every accent surface.
                Opacity = wash,
            };

            var face = new Viewbox
            {
                Child = Face(level, wash >= 0.5 ? onAccent : onCard),

                // At most 18px, and smaller when the button is (an XS tile): never squashed.
                MaxHeight = FaceSize,
                MaxWidth = FaceSize,
                Margin = new Thickness(2),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            Host = new Button
            {
                Content = new Grid { Children = { fill, face } },
                Background = emptyFill,
                Padding = new Thickness(0),
                MinWidth = 0,
                MinHeight = MoodLayout.PickerHeight,
                Height = MoodLayout.PickerHeight,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                CornerRadius = new CornerRadius(Fluent.ControlRadius),
                BorderThickness = new Thickness(Fluent.Hairline),
            };

            var name = Str.WidgetsB_Mood_LevelButton.Format(("level", MoodRules.LevelName(level)), ("value", level), ("max", MoodRules.MaxLevel));
            AutomationProperties.SetName(Host, name);
            ToolTipService.SetToolTip(Host, name);

            Host.Click += OnClick;
        }

        public int Level { get; }

        public Button Host { get; }

        /// <summary>
        /// Which level today already holds (a thicker accent outline, never a different fill: the fill
        /// is the scale), and where the keyboard cursor is (the focus stroke).
        /// </summary>
        public void SetState(bool selected, bool cursor, Brush ring, Brush noRing, Brush focus)
        {
            Host.BorderBrush = cursor ? focus : selected ? ring : noRing;
            Host.BorderThickness = new Thickness(cursor || selected ? Fluent.Space1 / 2 : Fluent.Hairline);
            AutomationProperties.SetItemStatus(Host, selected ? Str.WidgetsB_Mood_LoggedToday.Text() : string.Empty);
        }

        private void OnClick(object sender, RoutedEventArgs args) => _picked(Level);

        public void Detach() => Host.Click -= OnClick;

        /// <summary>A 16-unit face; the Viewbox scales it to the button.</summary>
        private static Canvas Face(int level, Brush ink)
        {
            const double stroke = 1.4;
            var canvas = new Canvas { Width = 16, Height = 16 };

            var head = new Ellipse { Width = 14, Height = 14, Stroke = ink, StrokeThickness = stroke };
            Canvas.SetLeft(head, 1);
            Canvas.SetTop(head, 1);
            canvas.Children.Add(head);

            foreach (var x in (double[])[4.9, 9.1])
            {
                var eye = new Ellipse { Width = 2, Height = 2, Fill = ink };
                Canvas.SetLeft(eye, x);
                Canvas.SetTop(eye, 5.2);
                canvas.Children.Add(eye);
            }

            // Curvature from -3 (frown) to +3 (smile); OK is a straight line.
            var bend = (level - 3) * 1.5;
            var figure = new PathFigure { StartPoint = new Windows.Foundation.Point(5, 10.6 - (bend / 2)), IsClosed = false, IsFilled = false };
            figure.Segments.Add(new QuadraticBezierSegment
            {
                Point1 = new Windows.Foundation.Point(8, 10.6 + bend),
                Point2 = new Windows.Foundation.Point(11, 10.6 - (bend / 2)),
            });

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = geometry,
                Stroke = ink,
                StrokeThickness = stroke,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });

            return canvas;
        }
    }

    /// <summary>
    /// One square of the heatmap: a day, and every channel it says its value in — the wash, a bar
    /// whose height is the level, a digit when the square is big enough, and the whole day (every
    /// entry with its thought, [S-1450]) in the tooltip and the accessible name. Right-click opens
    /// the day menu ([S-1451]).
    /// </summary>
    private sealed class DayCell
    {
        private readonly Rectangle _wash;
        private readonly Rectangle _bar;
        private readonly TextBlock? _digit;
        private readonly Brush _onAccent;
        private readonly Brush _onCard;
        private readonly Brush _ring;
        private readonly double _size;
        private readonly IDisposable _hover;
        private readonly IDisposable _menu;

        public DayCell(int week, int weekday, double size, bool digits, Brush accent, Brush emptyFill, Brush onAccent, Brush onCard, Brush ring,
            Func<LocalDay?, IEnumerable<ItemMenuEntry>> menu)
        {
            Week = week;
            Weekday = weekday;
            _size = size;
            _onAccent = onAccent;
            _onCard = onCard;
            _ring = ring;

            _wash = new Rectangle { Fill = accent };

            _bar = new Rectangle
            {
                Fill = accent,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Height = 0,
            };

            var face = new Grid();
            face.Children.Add(_wash);
            face.Children.Add(_bar);

            if (digits)
            {
                _digit = Fluent.Text(FluentType.Caption);
                _digit.HorizontalAlignment = HorizontalAlignment.Center;
                _digit.VerticalAlignment = VerticalAlignment.Center;
                face.Children.Add(_digit);
            }

            Host = new Border
            {
                Child = face,
                Width = size,
                Height = size,
                Background = emptyFill,
                CornerRadius = new CornerRadius(Fluent.ControlRadius / 2),
                BorderThickness = new Thickness(0),
            };

            _hover = Fluent.Hover(Host);
            _menu = ItemMenu.Attach(Host, () => menu(Day));
        }

        /// <summary>Which week (row of the <see cref="MoodGrid"/>) and weekday (column) this square shows.</summary>
        public int Week { get; }

        public int Weekday { get; }

        /// <summary>The day it shows now, or null for a gap.</summary>
        public LocalDay? Day { get; set; }

        public Border Host { get; }

        /// <summary>A square outside the window: a gap, so the weekday lines still line up.</summary>
        public void ShowBlank()
        {
            Host.Visibility = Visibility.Collapsed;
            ToolTipService.SetToolTip(Host, null);
            AutomationProperties.SetName(Host, string.Empty);
        }

        public void Show(MoodDay day, bool isToday, string description)
        {
            Host.Visibility = Visibility.Visible;

            var bucket = Math.Clamp(day.Bucket, 0, MoodRules.MaxLevel);

            _wash.Opacity = Wash[bucket];
            _bar.Height = _size * bucket / MoodRules.MaxLevel;
            _bar.Opacity = bucket == 0 ? 0 : 1;

            if (_digit is not null)
            {
                _digit.Text = bucket == 0 ? string.Empty : bucket.ToString(CultureInfo.InvariantCulture);
                _digit.Foreground = bucket >= 3 ? _onAccent : _onCard;
            }

            // Today is outlined rather than tinted: tinting it would be a sixth value on a
            // five-value scale.
            Host.BorderBrush = isToday ? _ring : null;
            Host.BorderThickness = new Thickness(isToday ? Fluent.Hairline : 0);

            ToolTipService.SetToolTip(Host, description);
            AutomationProperties.SetName(Host, description);
        }

        public void Detach()
        {
            _hover.Dispose();
            _menu.Dispose();
            Host.Child = null;
        }
    }
}
