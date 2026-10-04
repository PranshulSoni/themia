using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Shell;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.Foundation;

namespace Themia.App.Widgets;

/// <summary>The drawings: where each part goes at this size, and the ring, the bars and the calendar themselves.</summary>
internal sealed partial class ScreenTimeView
{
    /// <summary>The ring never shrinks below this: under it the total in its hole cannot be read.</summary>
    private const double MinRing = 64;

    /// <summary>From this width a bar, a chart of days or a calendar shares the row with the legend.</summary>
    private const double FlatSideBySideWidth = 420;

    /// <summary>A day's column needs this much width to carry its weekday under it.</summary>
    private const double LabelledColumn = 16;

    /// <summary>A calendar cell needs this much to have a weekday letter over its column.</summary>
    private const double LabelledCell = 12;

    private static readonly string[] DayLetters = ["S", "M", "T", "W", "T", "F", "S"];
    private static readonly string[] DayShort = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    private static readonly Brush AccentBrush = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);
    private static readonly Brush EmptyCellBrush = FluentLayout.Themed("ControlFillColorSecondaryBrush", Colors.Gray);
    private static readonly Brush TodayLabelBrush = FluentLayout.Themed("AccentTextFillColorPrimaryBrush", Colors.DodgerBlue);

    private TextBlock? _ringTotal;
    private TextBlock? _ringDetail;
    private (double Width, double Height)? _ringHole;

    /// <summary>
    /// Lay the widget out for its size and draw it. Called on every render: the drawing is a few
    /// dozen shapes, rebuilt when the data or the size changes, which is at most twice a minute.
    /// <para>
    /// The ladder: the drawing with its total always; the legend in whole rows; and on a tile large
    /// both ways a footer under them — the stats line, the limit bar and, for a day, the week it
    /// belongs to ([S-1213]..[S-1215]).
    /// </para>
    /// </summary>
    private void Lay()
    {
        if (!_area.IsMeasured)
        {
            return;
        }

        // The body keeps a small gap above the card's bottom edge, the way a list's padding does.
        var width = _area.Width;
        var height = _area.Height - Fluent.Space2;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        _parts.Clear();
        _drawing.Children.Clear();
        _drawing.RowDefinitions.Clear();
        _drawing.ColumnDefinitions.Clear();
        _ringTotal = null;
        _ringDetail = null;
        _ringHole = null;
        _body.Children.Clear();
        _body.RowDefinitions.Clear();
        _body.ColumnDefinitions.Clear();
        _body.ColumnSpacing = 0;
        _body.RowSpacing = 0;

        Unparent(_readout);
        Unparent(_legend);
        Unparent(_drawing);

        var period = ScreenTimeRules.ClampPeriod(_config.Period);
        var style = ScreenTimeRules.ClampStyle(period, _config.Style);

        // The footer first, from the bottom up: it only appears where the drawing keeps its room.
        var footer = LayFooter(width, height, style);
        height -= footer;
        _body.Margin = new Thickness(0, 0, 0, Fluent.Space2 + footer);

        // [S-1211] The legend asks for every app up to the ceiling; the layout grants whole rows.
        var wanted = _config.ShowList ? ScreenTimeRules.LegendWanted(_config.MaxApps, _model.Apps.Count) : 0;
        Segment(0);

        // A ring is square, so it shares a row with the legend early; a bar or a chart of days is
        // wide, and only gives up half the width once there is plenty of it.
        var sideFrom = style == ScreenTimeStyle.Doughnut ? SideBySideWidth : FlatSideBySideWidth;
        var side = wanted > 0 && width >= sideFrom && width >= height * 1.25;

        if (style == ScreenTimeStyle.Doughnut)
        {
            LayRing(width, height, wanted, side);
        }
        else
        {
            LayFlat(width, height, wanted, side, style);
        }

        ApplyFocus();
    }

    private static void Unparent(FrameworkElement element)
    {
        if (element.Parent is Panel panel)
        {
            panel.Children.Remove(element);
        }
    }

    /// <summary>
    /// [S-1212] Split the apps into the drawing's segments for a legend of <paramref name="legendRows"/>
    /// rows: every app named when they all fit, the last row "Other" when they do not.
    /// </summary>
    private void Segment(int legendRows)
    {
        var cap = ScreenTimeRules.DrawingCap(_config.MaxApps, legendRows, _model.Apps.Count);
        _segments = ScreenTimeRules.Segments(_model.Apps, _model.Total, cap, "Other");
    }

    /// <summary>[S-452] The ring with the total in its hole, and the legend beside or under it.</summary>
    private void LayRing(double width, double height, int wanted, bool side)
    {
        const double pad = Fluent.Space4;
        int rows;
        double ring;

        if (side)
        {
            ring = Math.Max(MinRing, Math.Min(height - Fluent.Space4, (width - (pad * 2)) * 0.42));
            rows = RowsThatFit(height - Fluent.Space2, wanted);

            _body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _body.ColumnSpacing = Fluent.Space2;
        }
        else
        {
            // The ring keeps at least half the tile; the legend takes the whole rows that are left,
            // and is dropped before the ring shrinks below readable.
            var maxRing = Math.Min(width - (pad * 2), height - Fluent.Space2);
            rows = RowsThatFit(height - Math.Max(MinRing, Math.Min(maxRing, height * 0.5)) - Fluent.Space3, wanted);
            ring = Math.Min(maxRing, height - (rows * RowHeight) - Fluent.Space3);

            while (rows > 0 && ring < Math.Min(MinRing * 1.5, maxRing))
            {
                rows--;
                ring = Math.Min(maxRing, height - (rows * RowHeight) - Fluent.Space3);
            }

            ring = Math.Max(Math.Min(MinRing, maxRing), ring);

            _body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _body.RowSpacing = Fluent.Space2;
        }

        Segment(rows);
        var legend = _segments;
        rows = Math.Min(rows, legend.Count);

        var host = Ring(ring);
        host.Margin = side ? new Thickness(pad, 0, 0, Fluent.Space2) : new Thickness(0, 0, 0, 0);
        host.HorizontalAlignment = HorizontalAlignment.Center;
        host.VerticalAlignment = side ? VerticalAlignment.Center : VerticalAlignment.Top;
        _body.Children.Add(host);

        if (rows > 0)
        {
            ShowLegend(legend, rows, side ? width - ring - pad - Fluent.Space2 : width, dots: true);
            _legend.VerticalAlignment = side ? VerticalAlignment.Center : VerticalAlignment.Top;
            SetRow(_legend, side ? 0 : 1);
            SetColumn(_legend, side ? 1 : 0);
            _body.Children.Add(_legend);
        }
    }

    /// <summary>The readout over a bar, a chart of days or a calendar — or nothing — with the legend.</summary>
    private void LayFlat(double width, double height, int wanted, bool side, ScreenTimeStyle style)
    {
        var drawingWanted = style != ScreenTimeStyle.None;
        var drawingFloor = !drawingWanted ? 0 : style == ScreenTimeStyle.Bars && !_model.Window ? 16 : 40;

        // The readout: a total big enough to be the point, not so big it crowds the drawing out.
        // Its text first, so whether the line under it shows is known before anything is measured.
        ApplyHeadline(_total, _detail);
        var readoutHeight = Math.Min(FluentType.Title.LineHeight, height * 0.3);
        var total = FluentType.Fit(width / (side ? 2 : 1) - (Fluent.Space4 * 2), readoutHeight, 7, FluentType.BodyStrong, FluentType.Title);
        _total.Apply(total);

        if (height < 100)
        {
            _detail.Visibility = Visibility.Collapsed;
        }

        var readout = total.LineHeight + (_detail.Visibility == Visibility.Visible ? FluentType.Caption.LineHeight : 0);

        _readout.Margin = new Thickness(Fluent.Space4, 0, Fluent.Space4, 0);

        var left = new Grid { RowSpacing = Fluent.Space2 };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        left.Children.Add(_readout);

        var rows = 0;
        double drawingHeight;

        if (side)
        {
            rows = RowsThatFit(height - Fluent.Space2, wanted);
            drawingHeight = height - readout - Fluent.Space2 - Fluent.Space2;
            _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _body.ColumnSpacing = Fluent.Space2;
        }
        else
        {
            var room = height - readout - Fluent.Space2 - (drawingWanted ? drawingFloor + Fluent.Space2 : 0);
            rows = RowsThatFit(room, wanted);

            // A drawing of days wants the height; a single bar does not.
            var flexible = _model.Window && drawingWanted;
            if (flexible)
            {
                rows = Math.Min(rows, RowsThatFit(height * 0.4, wanted));
            }

            drawingHeight = height - readout - Fluent.Space2 - (rows * RowHeight) - (rows > 0 ? Fluent.Space2 : 0) - Fluent.Space2;
            _body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        Segment(rows);
        var legend = _segments;
        rows = Math.Min(rows, legend.Count);

        if (drawingWanted && drawingHeight >= drawingFloor * 0.75)
        {
            var drawing = style switch
            {
                ScreenTimeStyle.Heatmap => Heatmap(width / (side ? 2 : 1), drawingHeight),
                ScreenTimeStyle.Bars when _model.Window => DayBars(width / (side ? 2 : 1), drawingHeight),
                _ => DayBar(),
            };

            SetRow(drawing, 1);
            left.Children.Add(drawing);
        }

        // Beside a legend, a readout over one thin bar (or over nothing) sits level with the list;
        // a chart of days keeps the full height it was given.
        if (side && !(_model.Window && drawingWanted))
        {
            left.VerticalAlignment = VerticalAlignment.Center;
        }

        _body.Children.Add(left);

        if (rows > 0)
        {
            ShowLegend(legend, rows, side ? width / 2 : width, dots: style == ScreenTimeStyle.Bars);
            _legend.VerticalAlignment = side ? VerticalAlignment.Center : VerticalAlignment.Bottom;
            SetRow(_legend, side ? 0 : 1);
            SetColumn(_legend, side ? 1 : 0);
            _body.Children.Add(_legend);
        }
    }

    private static int RowsThatFit(double height, int wanted) => Math.Min(Math.Max(0, wanted), Disclosure.WholeRows(height, RowHeight));

    // ---- the ring ---------------------------------------------------------------------------

    /// <summary>
    /// [S-452] The ring: one arc per segment, from cumulative seconds so the edges meet without a
    /// seam, and the total in its hole. Hovering a wedge lights it and names it in the hole.
    /// </summary>
    private Grid Ring(double size)
    {
        var thickness = Math.Max(Fluent.Space2, size * 0.14);
        var radius = (size - thickness) / 2;
        var centre = size / 2;
        var canvas = new Canvas { Width = size, Height = size };

        foreach (var segment in _segments)
        {
            Shape arc;

            if (segment.Fraction >= 0.9999)
            {
                arc = new Ellipse { Width = radius * 2, Height = radius * 2 };
                Canvas.SetLeft(arc, centre - radius);
                Canvas.SetTop(arc, centre - radius);
            }
            else
            {
                arc = Arc(centre, radius, segment.Start, segment.Start + segment.Fraction);
            }

            arc.Stroke = AppBrush(segment);
            arc.StrokeThickness = thickness;
            ToolTipService.SetToolTip(arc, $"{segment.Name} — {ScreenTimeRules.FormatDuration(segment.Seconds)} · {Math.Round(ScreenTimeRules.Share(segment.Seconds, _model.Total) * 100)}%");
            Hoverable(arc, segment.Key);
            AttachHideMenu(arc, segment);
            canvas.Children.Add(arc);
        }

        // The hole is the readout here: total, and under it what a readout row would carry. It
        // takes no pointer input, so the wedges under its box stay hoverable.
        var hole = size - (thickness * 2);
        _ringHole = (hole * 0.86, hole * 0.4);
        _ringTotal = Fluent.Text(FluentType.Caption);
        _ringTotal.TextTrimming = TextTrimming.None;
        _ringTotal.HorizontalAlignment = HorizontalAlignment.Center;
        _ringTotal.TextAlignment = TextAlignment.Center;

        _ringDetail = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
        _ringDetail.HorizontalAlignment = HorizontalAlignment.Center;
        _ringDetail.TextAlignment = TextAlignment.Center;
        _ringDetail.MaxWidth = hole * 0.86;

        ApplyHeadline(_ringTotal, _ringDetail, _ringHole);

        if (hole < 72)
        {
            _ringDetail.Visibility = Visibility.Collapsed;
        }

        var centreText = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            MaxWidth = hole * 0.9,
            Children = { _ringTotal, _ringDetail },
        };

        return new Grid { Width = size, Height = size, Children = { canvas, centreText } };
    }

    /// <summary>An arc of the ring between two shares of the circle, starting at twelve o'clock.</summary>
    private static Microsoft.UI.Xaml.Shapes.Path Arc(double centre, double radius, double from, double to)
    {
        static Point At(double centre, double radius, double share)
        {
            var angle = (share * 2 * Math.PI) - (Math.PI / 2);
            return new Point(centre + (radius * Math.Cos(angle)), centre + (radius * Math.Sin(angle)));
        }

        var figure = new PathFigure { StartPoint = At(centre, radius, from), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = At(centre, radius, to),
            Size = new Size(radius, radius),
            IsLargeArc = to - from > 0.5,
            SweepDirection = SweepDirection.Clockwise,
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return new Microsoft.UI.Xaml.Shapes.Path { Data = geometry };
    }

    // ---- one day as a bar ---------------------------------------------------------------------

    /// <summary>
    /// [S-452] A day as one bar across the widget, split into the ring's segments so it always fills
    /// the width and each app keeps its colour. No track behind it: the parts are the whole bar.
    /// </summary>
    private Grid DayBar()
    {
        var bar = new Grid
        {
            Height = Fluent.Space3,
            Margin = new Thickness(Fluent.Space4, 0, Fluent.Space4, 0),
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(Fluent.Space3 / 2),
            ColumnSpacing = 0,
        };

        for (var i = 0; i < _segments.Count; i++)
        {
            var segment = _segments[i];
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(segment.Fraction, 0.0001), GridUnitType.Star) });

            var part = new Border { Background = AppBrush(segment) };
            ToolTipService.SetToolTip(part, $"{segment.Name} — {ScreenTimeRules.FormatDuration(segment.Seconds)}");
            SetColumn(part, i);
            Hoverable(part, segment.Key);
            AttachHideMenu(part, segment);
            bar.Children.Add(part);
        }

        return bar;
    }

    // ---- a window as bars ---------------------------------------------------------------------

    /// <summary>
    /// [S-456] One bar per day, oldest left, today right, each split by app in the legend's order
    /// and scaled to the window's busiest day. A day without use draws nothing at all.
    /// </summary>
    private Grid DayBars(double width, double height)
    {
        var count = _model.Range.Count;
        var labels = count <= 7 && (width - (Fluent.Space4 * 2)) / Math.Max(1, count) >= LabelledColumn;
        var gap = labels ? Fluent.Space2 : 2;
        var labelHeight = labels ? FluentType.Caption.LineHeight + Fluent.Space1 : 0;
        var area = Math.Max(1, height - labelHeight);
        var today = _source?.Today.Key;

        var chart = new Grid
        {
            ColumnSpacing = gap,
            Margin = new Thickness(Fluent.Space4, 0, Fluent.Space4, 0),
            Height = height,
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        for (var i = 0; i < count; i++)
        {
            var day = _model.Range[i];
            var apps = ScreenTimeRules.Visible(day.Apps, _model.Hidden);
            var parts = ScreenTimeRules.Stack(apps, _segments, _model.Busiest);

            chart.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var column = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(area) });
            column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Parts stack up from the base, busiest app at the bottom.
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            for (var p = parts.Count - 1; p >= 0; p--)
            {
                var part = parts[p];
                var segment = _segments.FirstOrDefault(s => s.Key == part.Key);
                var block = new Border
                {
                    Height = Math.Max(1, part.Fraction * area),
                    Background = segment is null ? OtherBrush : AppBrush(segment),
                    CornerRadius = p == parts.Count - 1 ? new CornerRadius(2, 2, 0, 0) : new CornerRadius(0),
                };

                stack.Children.Add(block);
            }

            column.Children.Add(stack);

            if (labels && LocalDay.TryParse(day.Date, out var date))
            {
                var label = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
                label.Text = width / count >= 36 ? DayShort[(int)date.DayOfWeek] : DayLetters[(int)date.DayOfWeek];
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.TextTrimming = TextTrimming.None;

                // Today's column is marked the way the calendar marks today.
                if (day.Date == today)
                {
                    label.Foreground = TodayLabelBrush;
                }

                SetRow(label, 1);
                column.Children.Add(label);
            }

            var seconds = _model.Totals[i].Seconds;
            ToolTipService.SetToolTip(column, $"{TooltipDate(day.Date)} — {ScreenTimeRules.FormatDuration(seconds)}");
            SetColumn(column, i);
            Hoverable(column, day.Date);
            chart.Children.Add(column);
        }

        return chart;
    }

    // ---- a window as a calendar ---------------------------------------------------------------

    /// <summary>
    /// [S-456] Seven cells wide and padded so the window ends on the last cell: every column is one
    /// weekday. A cell's shade is its quartile of the busiest day, in the accent, so it re-themes.
    /// </summary>
    private Grid Heatmap(double width, double height)
    {
        var cells = ScreenTimeRules.HeatmapCells(_model.Totals.Count);
        var rows = Math.Max(1, cells.Count / ScreenTimeRules.HeatmapColumns);
        var gap = 3.0;
        var available = width - (Fluent.Space4 * 2);
        var header = FluentType.Caption.LineHeight + 2;
        var cell = Math.Min((available - (gap * 6)) / 7, (height - header - (gap * rows)) / rows);

        // Too small to letter: the weekday row goes, and the cells take its height.
        if (cell < LabelledCell)
        {
            header = 0;
            cell = Math.Min((available - (gap * 6)) / 7, (height - (gap * (rows - 1))) / rows);
        }

        // A week of cells stays a row of chips rather than stretching into slabs.
        cell = Math.Clamp(cell, 4, rows <= 2 ? 28 : 40);
        gap = cell < 8 ? 2 : gap;

        var grid = new Grid
        {
            ColumnSpacing = gap,
            RowSpacing = gap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };

        for (var c = 0; c < ScreenTimeRules.HeatmapColumns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cell) });
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var r = 0; r < rows; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cell) });
        }

        var today = _source?.Today.Key;
        var labelled = new bool[ScreenTimeRules.HeatmapColumns];

        for (var i = 0; i < cells.Count; i++)
        {
            if (cells[i] is not { } index)
            {
                continue;
            }

            var column = i % ScreenTimeRules.HeatmapColumns;
            var (date, seconds) = _model.Totals[index];
            var level = ScreenTimeRules.HeatLevel(seconds, _model.Busiest);

            // The shade is the inner fill's own opacity — the brush is the shared accent and must
            // stay whole — and the outer box is what hover dims, so the two multiply.
            var shade = new Border
            {
                CornerRadius = new CornerRadius(Math.Min(4, cell / 4)),
                Background = level == 0 ? EmptyCellBrush : AccentBrush,
                Opacity = level == 0 ? 1 : 0.25 + (0.75 * (level - 1) / 3),
            };

            var box = new Border { Child = shade, CornerRadius = shade.CornerRadius };

            if (date == today)
            {
                box.BorderBrush = PrimaryBrush;
                box.BorderThickness = new Thickness(Fluent.Hairline);
            }

            ToolTipService.SetToolTip(box, $"{TooltipDate(date)} — {ScreenTimeRules.FormatDuration(seconds)}");
            SetRow(box, (i / ScreenTimeRules.HeatmapColumns) + 1);
            SetColumn(box, column);
            Hoverable(box, date);
            grid.Children.Add(box);

            // The header names each column's weekday from the first day that lands in it.
            if (header > 0 && !labelled[column] && LocalDay.TryParse(date, out var day))
            {
                labelled[column] = true;
                var label = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
                label.Text = DayLetters[(int)day.DayOfWeek];
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.TextTrimming = TextTrimming.None;
                SetRow(label, 0);
                SetColumn(label, column);
                grid.Children.Add(label);
            }
        }

        return grid;
    }

    private string TooltipDate(string date)
        => LocalDay.TryParse(date, out var day)
            ? Formatting.DayHeading(day.Date, (_source?.Today ?? day).Date)
            : date;

    // ---- the legend ---------------------------------------------------------------------------

    private void ShowLegend(IReadOnlyList<ScreenTimeSegment> legend, int rows, double width, bool dots)
    {
        while (_rows.Count < rows)
        {
            var row = new LegendRow(this);
            _rows.Add(row);
            _legend.Children.Add(row.Host);
        }

        // The name is worth more than the average beside it: the average is the first to go.
        var showAverage = _model.Window && _config.ShowAverage && width >= 300;
        var showPercent = _config.ShowPercent && width >= 200;
        var limits = ScreenTimeRules.AppLimits(_config.AppLimits);

        for (var i = 0; i < _rows.Count; i++)
        {
            if (i >= rows)
            {
                _rows[i].Host.Visibility = Visibility.Collapsed;
                continue;
            }

            var segment = legend[i];
            var average = ScreenTimeRules.DailyAverage(segment.Seconds, _model.Days);
            var judged = _model.Window ? average : segment.Seconds;
            var goal = ScreenTimeRules.Goal(judged, limits.TryGetValue(segment.Key, out var limit) ? limit : null);

            _rows[i].Host.Visibility = Visibility.Visible;
            _rows[i].Show(
                segment,
                AppBrush(segment),
                dots && !segment.IsOther,
                showAverage ? $"{ScreenTimeRules.FormatDuration(average)}/day" : null,
                showPercent ? string.Create(CultureInfo.InvariantCulture, $"{Math.Round(ScreenTimeRules.Share(segment.Seconds, _model.Total) * 100)}%") : null,
                goal);
        }
    }
}
