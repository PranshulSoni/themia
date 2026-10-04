using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Shell;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.Foundation;

namespace Themia.App.Widgets;

/// <summary>
/// The chart and what sits around it: the trace (solid in session, dashed outside it [S-1308]),
/// the optional fill and high/low markers [S-1310], the price axis, the times under it in the
/// exchange's zone [S-1309], the facts, the range chips and the pointer readout [S-1311].
/// </summary>
internal sealed partial class StocksView
{
    private readonly Grid _chart = new() { MinHeight = StockLayout.MinChart };
    private readonly Canvas _canvas = new();
    private readonly Polygon _fill = new();
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly Line _rule = new() { StrokeThickness = Fluent.Hairline, Visibility = Visibility.Collapsed };
    private readonly Border _probe = new() { Visibility = Visibility.Collapsed, Padding = new Thickness(Fluent.Space1, 0, Fluent.Space1, 0) };
    private readonly TextBlock _probeText = Fluent.Text(FluentType.Caption);

    private readonly Grid _labels = new();
    private readonly TextBlock[] _labelTexts = [
        Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush"),
        Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush"),
        Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush"),
    ];

    private readonly Grid _stats = new();
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Spacing = Fluent.Space1 };
    private readonly List<(Button Button, QuoteRange Range)> _chipButtons = [];

    /// <summary>Where each bar was drawn, for the pointer readout.</summary>
    private readonly List<Point> _drawn = [];

    private void BuildChart()
    {
        _fill.Opacity = Fluent.ChartWash;
        _fill.IsHitTestVisible = false;
        _canvas.IsHitTestVisible = false;

        _rule.Stroke = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);
        _probe.Child = _probeText;
        _probe.CornerRadius = new CornerRadius(Fluent.ControlRadius);
        _probe.Background = FluentLayout.Themed("SolidBackgroundFillColorTertiaryBrush", Colors.Black);
        _overlay.Children.Add(_rule);
        _overlay.Children.Add(_probe);

        _chart.Children.Add(_canvas);
        _chart.Children.Add(_overlay);

        // The chart's own surface takes the pointer for the readout; the rest of the card is still
        // the card's to drag. A transparent fill is what makes an empty area hit-testable.
        _chart.Background = FluentLayout.Themed("SubtleFillColorTransparentBrush", Colors.Transparent);
        _chart.SizeChanged += OnChartSizeChanged;
        _chart.PointerMoved += OnChartPointerMoved;
        _chart.PointerExited += OnChartPointerGone;
        _chart.PointerCanceled += OnChartPointerGone;

        for (var i = 0; i < 3; i++)
        {
            _labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            SetColumn(_labelTexts[i], i);
            _labels.Children.Add(_labelTexts[i]);
        }

        _labelTexts[1].HorizontalAlignment = HorizontalAlignment.Center;
        _labelTexts[2].HorizontalAlignment = HorizontalAlignment.Right;
        _labels.Margin = new Thickness(StockLayout.Inset, StockLayout.Gap, StockLayout.Inset, Fluent.Space1);

        _stats.Visibility = Visibility.Collapsed;
    }

    private void BuildChips()
    {
        QuoteRange[] ranges =
        [
            QuoteRange.Day, QuoteRange.FiveDays, QuoteRange.Month, QuoteRange.ThreeMonths,
            QuoteRange.SixMonths, QuoteRange.Year, QuoteRange.FiveYears,
        ];

        foreach (var range in ranges)
        {
            var button = new Button
            {
                Content = ChipText(range),
                Padding = new Thickness(Fluent.Space2, 0, Fluent.Space2, 0),
                MinHeight = 24,
                Height = 24,
                FontSize = FluentType.Caption.FontSize,
                Tag = range,
            };

            button.Click += OnChipClick;
            _chips.Children.Add(button);
            _chipButtons.Add((button, range));
        }

        _chips.Margin = new Thickness(StockLayout.Inset, Fluent.Space1, StockLayout.Inset, 0);
        _chips.Visibility = Visibility.Collapsed;
        SetRow(_chips, 0);
        SetColumnSpan(_chips, 2);
    }

    /// <summary>The chips' short period names ("1D", "1T"), again after a language change.</summary>
    private void LabelChips()
    {
        foreach (var (button, range) in _chipButtons)
        {
            button.Content = ChipText(range);
        }
    }

    private static string ChipText(QuoteRange range) => (range switch
    {
        QuoteRange.Day => Str.WidgetsC_Stocks_Range_Day,
        QuoteRange.FiveDays => Str.WidgetsC_Stocks_Range_FiveDays,
        QuoteRange.Month => Str.WidgetsC_Stocks_Range_Month,
        QuoteRange.ThreeMonths => Str.WidgetsC_Stocks_Range_ThreeMonths,
        QuoteRange.SixMonths => Str.WidgetsC_Stocks_Range_SixMonths,
        QuoteRange.Year => Str.WidgetsC_Stocks_Range_Year,
        _ => Str.WidgetsC_Stocks_Range_FiveYears,
    }).Text();

    /// <summary>[S-1311] A range chip sets the period, as the settings' Period choice does, and saves it.</summary>
    private void OnChipClick(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: QuoteRange range } || range == _config.Range)
        {
            return;
        }

        _config.Range = range;
        _surface?.RequestSave();
        ApplyConfig(_config);
    }

    private void UpdateChips()
    {
        foreach (var (button, range) in _chipButtons)
        {
            var on = range == _config.Range;
            button.Style = on ? AccentStyle() : null;
        }
    }

    private static Style? AccentStyle()
        => Application.Current.Resources.TryGetValue("AccentButtonStyle", out var style) ? style as Style : null;

    /// <summary>[S-1309] Start and end (and middle) times under the chart, in the exchange's zone.</summary>
    private void UpdateLabels(TickerQuote quote, StockPlan plan)
    {
        var labels = ExchangeTime.Labels(quote.Bars, quote.ExchangeOffset, _config.Range, DateTimeOffset.Now.Offset, plan.MiddleLabel);

        _labelTexts[0].Text = labels.Count > 0 ? labels[0] : string.Empty;
        _labelTexts[1].Text = labels.Count == 3 ? labels[1] : string.Empty;
        _labelTexts[2].Text = labels.Count > 0 ? labels[^1] : string.Empty;

        ToolTipService.SetToolTip(_labels, ExchangeTime.ZoneNote(quote.ZoneName));
    }

    /// <summary>[S-1311] The facts: a stack, a row of tiles, or a column beside the chart.</summary>
    private void LayStats(IReadOnlyList<(string Label, string Value)> facts, StockPlan plan)
    {
        _stats.Children.Clear();
        _stats.RowDefinitions.Clear();
        _stats.ColumnDefinitions.Clear();

        var count = Math.Min(plan.StatCount, facts.Count);

        if (plan.Stats == StatsPlacement.None || count == 0)
        {
            _stats.Visibility = Visibility.Collapsed;
            return;
        }

        _stats.Visibility = Visibility.Visible;

        switch (plan.Stats)
        {
            case StatsPlacement.Tiles:
                _stats.ColumnSpacing = Fluent.Space2;
                _stats.RowSpacing = 0;
                _stats.Margin = new Thickness(StockLayout.Inset, StockLayout.Gap, StockLayout.Inset, Fluent.Space2);
                _stats.Width = double.NaN;

                for (var i = 0; i < count; i++)
                {
                    _stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    var tile = Fact(facts[i], stacked: true);
                    SetColumn(tile, i);
                    _stats.Children.Add(tile);
                }

                Place(_stats, 5, 0, 1, 2);
                break;

            case StatsPlacement.Stack:
                _stats.ColumnSpacing = 0;
                _stats.RowSpacing = 0;
                _stats.Margin = new Thickness(StockLayout.Inset, StockLayout.Gap, StockLayout.Inset, Fluent.Space2);
                _stats.Width = double.NaN;

                // [S-1109] A line whose label and value do not both fit the tile is shed rather than
                // trimmed: "52-Wochen-Spanne 344,79…" says less than the lines that do fit. Measured,
                // not estimated, because the words differ by language and a number is not a word.
                var room = _area.Width - (2 * StockLayout.Inset);
                var shown = 0;

                for (var i = 0; i < facts.Count && shown < count; i++)
                {
                    var line = Fact(facts[i], stacked: false);
                    line.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                    if (line.DesiredSize.Width > room)
                    {
                        continue;
                    }

                    _stats.RowDefinitions.Add(new RowDefinition { Height = new GridLength(StockLayout.StackLine) });
                    SetRow(line, shown++);
                    _stats.Children.Add(line);
                }

                if (shown == 0)
                {
                    _stats.Visibility = Visibility.Collapsed;
                    return;
                }

                Place(_stats, 5, 0, 1, 2);
                break;

            case StatsPlacement.Column:
                _stats.ColumnSpacing = 0;
                _stats.RowSpacing = 0;
                _stats.Margin = new Thickness(Fluent.Space2, 0, StockLayout.Inset, Fluent.Space2);
                _stats.Width = StockLayout.ColumnWidth;

                for (var i = 0; i < count; i++)
                {
                    _stats.RowDefinitions.Add(new RowDefinition { Height = new GridLength(StockLayout.ColumnLine) });
                    var tile = Fact(facts[i], stacked: true);
                    SetRow(tile, i);
                    _stats.Children.Add(tile);
                }

                _stats.VerticalAlignment = VerticalAlignment.Top;
                Place(_stats, 3, 1, 2, 1);
                break;

            default:
                break;
        }
    }

    /// <summary>One fact: label over value (a tile), or label left and value right (a line).</summary>
    private static FrameworkElement Fact((string Label, string Value) fact, bool stacked)
    {
        var label = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
        label.Text = fact.Label;
        label.TextTrimming = TextTrimming.CharacterEllipsis;

        var value = Fluent.Text(stacked ? FluentType.Body : FluentType.Caption, "TextFillColorPrimaryBrush");
        value.Text = fact.Value;
        value.TextTrimming = TextTrimming.CharacterEllipsis;

        if (stacked)
        {
            return new StackPanel { Children = { label, value }, VerticalAlignment = VerticalAlignment.Center };
        }

        var line = new Grid
        {
            ColumnSpacing = Fluent.Space2,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };

        value.HorizontalAlignment = HorizontalAlignment.Right;
        SetColumn(value, 1);
        line.Children.Add(label);
        line.Children.Add(value);
        return line;
    }

    private void OnChartSizeChanged(object sender, SizeChangedEventArgs args) => RedrawChart();

    /// <summary>
    /// Redraw the trace. Cheap by construction: a handful of shapes, rebuilt only on a new quote or
    /// a resize, over a series the service already downsampled to a few hundred points.
    /// </summary>
    private void RedrawChart()
    {
        _canvas.Children.Clear();
        _drawn.Clear();

        if (_chart.Visibility != Visibility.Visible || _quote?.Value is not { } quote)
        {
            return;
        }

        var width = _chart.ActualWidth;
        var height = _chart.ActualHeight;
        var bars = quote.Bars;

        if (width <= 0 || height <= 0 || bars.Count < 2)
        {
            return;
        }

        var scaled = MarketQuote.Normalise(MarketQuote.Closes(bars));

        // Inset by the stroke width so the period's high and low are not clipped in half at the
        // edges, and by a caption line when the high/low labels need room above and below.
        var markers = _plan.MinMax;
        var pad = markers ? FluentType.Caption.LineHeight : Fluent.ChartStroke;
        var usable = Math.Max(1, height - (2 * pad));

        for (var i = 0; i < scaled.Count; i++)
        {
            // The window is exactly as long as the series, which spreads the points edge to edge.
            var x = Sparkline.SampleX(i, scaled.Count, scaled.Count) * width;
            var y = pad + (Sparkline.SampleY(scaled[i]) * usable);
            _drawn.Add(new Point(x, y));
        }

        if (_config.ShowAreaFill)
        {
            var fill = new PointCollection { new(_drawn[0].X, height) };

            foreach (var point in _drawn)
            {
                fill.Add(point);
            }

            fill.Add(new Point(_drawn[^1].X, height));
            _fill.Points = fill;
            _fill.Fill = _direction;
            _canvas.Children.Add(_fill);
        }

        // [S-1308] Solid inside the regular session, dashed and fainter outside it.
        foreach (var (from, to, extended) in MarketQuote.Segments(bars, quote.HasPrePost ? quote.Sessions : null))
        {
            var line = new Polyline
            {
                Stroke = _direction,
                StrokeThickness = Fluent.ChartStroke,
                StrokeLineJoin = PenLineJoin.Round,
                Opacity = extended ? 0.55 : 1,
                Points = [],
            };

            if (extended)
            {
                line.StrokeDashArray = [2, 2];
            }

            // Each run starts at the last point of the one before, so the line has no gap.
            for (var i = Math.Max(0, from - 1); i < to; i++)
            {
                line.Points.Add(_drawn[i]);
            }

            _canvas.Children.Add(line);
        }

        if (markers && MarketQuote.Extremes(bars) is { } extremes)
        {
            Marker(_drawn[extremes.High], MarketQuote.Price(bars[extremes.High].Close, quote.PriceHint), above: true, width);
            Marker(_drawn[extremes.Low], MarketQuote.Price(bars[extremes.Low].Close, quote.PriceHint), above: false, width);
        }

        if (_plan.Axis)
        {
            Axis(quote, width, pad, usable);
        }
    }

    /// <summary>[S-1310] A dot on the extreme with its price beside it, kept inside the chart.</summary>
    private void Marker(Point at, string text, bool above, double width)
    {
        const double Dot = 6;
        var dot = new Ellipse { Width = Dot, Height = Dot, Fill = _direction };
        Canvas.SetLeft(dot, at.X - (Dot / 2));
        Canvas.SetTop(dot, at.Y - (Dot / 2));
        _canvas.Children.Add(dot);

        var label = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
        label.Text = text;
        var estimate = FluentType.EstimateWidth(FluentType.Caption, text.Length);
        var left = Math.Clamp(at.X - (estimate / 2), Fluent.Space1, Math.Max(Fluent.Space1, width - estimate - Fluent.Space1));
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, above ? at.Y - FluentType.Caption.LineHeight - 2 : at.Y + 2);
        _canvas.Children.Add(label);
    }

    /// <summary>[S-1311] High, middle and low of the period at the chart's right edge.</summary>
    private void Axis(TickerQuote quote, double width, double pad, double usable)
    {
        var closes = MarketQuote.Closes(quote.Bars);
        var low = closes.Min();
        var high = closes.Max();

        foreach (var (fraction, value) in new[] { (0.0, high), (0.5, (high + low) / 2), (1.0, low) })
        {
            var text = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            text.Text = MarketQuote.Price(value, quote.PriceHint);
            text.TextAlignment = TextAlignment.Right;
            text.Width = 96;
            Canvas.SetLeft(text, width - 96 - StockLayout.Inset);

            var y = pad + (fraction * usable) - (FluentType.Caption.LineHeight / 2);
            Canvas.SetTop(text, Math.Clamp(y, 0, Math.Max(0, _chart.ActualHeight - FluentType.Caption.LineHeight)));
            _canvas.Children.Add(text);
        }
    }

    /// <summary>[S-1311] On a large tile, the pointer over the chart reads the bar under it: time and price.</summary>
    private void OnChartPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_drawn.Count < 2 || _quote?.Value is not { } quote || _area.WidthClass < SizeClass.Expanded || _plan.Beside)
        {
            return;
        }

        var x = args.GetCurrentPoint(_chart).Position.X;
        var width = _chart.ActualWidth;
        var index = (int)Math.Round(Math.Clamp(x / Math.Max(1, width), 0, 1) * (_drawn.Count - 1));
        var point = _drawn[index];
        var bar = quote.Bars[index];

        _rule.X1 = _rule.X2 = point.X;
        _rule.Y1 = 0;
        _rule.Y2 = _chart.ActualHeight;
        _rule.Visibility = Visibility.Visible;

        var range = _config.Range is QuoteRange.Day or QuoteRange.FiveDays ? QuoteRange.FiveDays : QuoteRange.Year;
        _probeText.Text = $"{ExchangeTime.Label(bar.Time, quote.ExchangeOffset, range, DateTimeOffset.Now.Offset)} · {MarketQuote.Price(bar.Close, quote.PriceHint)}";
        _probe.Visibility = Visibility.Visible;

        var estimate = FluentType.EstimateWidth(FluentType.Caption, _probeText.Text.Length) + (2 * Fluent.Space1);
        Canvas.SetLeft(_probe, Math.Clamp(point.X - (estimate / 2), 0, Math.Max(0, width - estimate)));
        Canvas.SetTop(_probe, 0);
    }

    private void OnChartPointerGone(object sender, PointerRoutedEventArgs args) => HideProbe();

    private void HideProbe()
    {
        _rule.Visibility = Visibility.Collapsed;
        _probe.Visibility = Visibility.Collapsed;
    }

    private void DetachChart()
    {
        _canvas.Children.Clear();
        _drawn.Clear();
        HideProbe();
    }
}
