using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Shell;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.Foundation;

namespace Themia.App.Widgets;

/// <summary>
/// [S-723] The next 24 hours on the current-conditions widget: the temperature as the meters' area
/// line, the chance of precipitation as quiet bars under it, and a row of hour labels.
/// <para>
/// Full-bleed, like every chart in the app: the trace runs to both edges and the bottom. Redrawn
/// only on new data or a resize of its own host — never on a tick.
/// </para>
/// </summary>
internal sealed partial class WeatherHourlyChart : Grid
{
    private readonly Canvas _canvas = new() { IsHitTestVisible = false };
    private readonly Polygon _fill = new();
    private readonly Polyline _line = new();
    private readonly Grid _axis = new();
    private readonly TextBlock _range = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly Brush _rainBrush = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);
    private readonly List<Rectangle> _bars = [];

    private IReadOnlyList<HourlyPoint> _hours = [];

    public WeatherHourlyChart()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var accent = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);
        _fill.Fill = accent;
        _fill.Opacity = Fluent.ChartWash;
        _line.Stroke = accent;
        _line.StrokeThickness = Fluent.ChartStroke;
        _line.StrokeLineJoin = PenLineJoin.Round;

        _canvas.Children.Add(_fill);
        _canvas.Children.Add(_line);

        // The day's span of the curve, top left over the chart, so the trace has a scale.
        _range.Margin = new Thickness(Fluent.Space4, 0, 0, 0);
        _range.VerticalAlignment = VerticalAlignment.Top;
        _range.HorizontalAlignment = HorizontalAlignment.Left;

        // No side margin: the labels sit under their points, which run edge to edge.
        _axis.Margin = new Thickness(0, Fluent.Space1, 0, Fluent.Space2);

        SetRow(_axis, 1);
        Children.Add(_canvas);
        Children.Add(_range);
        Children.Add(_axis);

        _canvas.SizeChanged += (_, _) => Redraw();
    }

    /// <summary>Show these hours (already trimmed to the ones still ahead).</summary>
    public void SetHours(IReadOnlyList<HourlyPoint> hours, TemperatureUnit unit)
    {
        _hours = hours;
        BuildAxis();

        double? low = null, high = null;
        foreach (var hour in hours)
        {
            if (hour.Temperature is { } t)
            {
                low = low is null ? t : Math.Min(low.Value, t);
                high = high is null ? t : Math.Max(high.Value, t);
            }
        }

        _range.Text = low is null ? string.Empty : $"{WeatherReport.Degrees(low)} – {WeatherReport.Temperature(high, unit)}";
        Redraw();
    }

    private void BuildAxis()
    {
        _axis.Children.Clear();
        _axis.ColumnDefinitions.Clear();

        if (_hours.Count < 2)
        {
            return;
        }

        // A label every six hours, each starting at its own point: the columns are six slots wide
        // (the last one whatever is left), so a label's left edge is its hour's x.
        const int every = 6;
        var slots = _hours.Count - 1;

        for (var start = 0; start < slots; start += every)
        {
            var width = Math.Min(every, slots - start);
            _axis.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Star) });

            // A label that would be cut by the right edge is left out rather than clipped.
            if (width < 3 && start > 0)
            {
                continue;
            }

            var label = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            label.Text = start == 0 ? Str.WidgetsC_Weather_Now.Text() : LocaleFormat.Time(TimeOnly.FromDateTime(_hours[start].At.DateTime));
            label.Margin = new Thickness(start == 0 ? Fluent.Space4 : 0, 0, 0, 0);
            SetColumn(label, _axis.ColumnDefinitions.Count - 1);
            _axis.Children.Add(label);
        }
    }

    private void Redraw()
    {
        foreach (var bar in _bars)
        {
            _canvas.Children.Remove(bar);
        }

        _bars.Clear();

        var width = _canvas.ActualWidth;
        var height = _canvas.ActualHeight;
        var temps = new List<(int Index, double Value)>();

        for (var i = 0; i < _hours.Count; i++)
        {
            if (_hours[i].Temperature is { } t)
            {
                temps.Add((i, t));
            }
        }

        if (width <= 0 || height <= 0 || temps.Count < 2)
        {
            _line.Points.Clear();
            _fill.Points.Clear();
            return;
        }

        var low = temps.Min(p => p.Value);
        var high = temps.Max(p => p.Value);
        var span = Math.Max(1, high - low);

        // The curve keeps a band at the top for the range label and leaves the bottom quarter to
        // the rain bars, so neither draws over the other.
        var top = FluentType.Caption.LineHeight + Fluent.Space1;
        var bottom = height * 0.75;
        var usable = Math.Max(1, bottom - top);
        var slot = width / Math.Max(1, _hours.Count - 1);

        var line = new PointCollection();
        foreach (var (index, value) in temps)
        {
            line.Add(new Point(index * slot, top + ((1 - ((value - low) / span)) * usable)));
        }

        var fill = new PointCollection { new(line[0].X, height) };
        foreach (var point in line)
        {
            fill.Add(point);
        }

        fill.Add(new Point(line[^1].X, height));
        _line.Points = line;
        _fill.Points = fill;

        // Chance of precipitation: a thin bar per hour from the baseline, a quarter of the chart at
        // 100%. Nothing at 0, so a dry day shows no bars at all.
        var barWidth = Math.Max(2, Math.Min(6, slot * 0.4));
        var barRoom = height * 0.25;

        for (var i = 0; i < _hours.Count; i++)
        {
            if (_hours[i].PrecipitationChance is not { } chance || chance <= 0)
            {
                continue;
            }

            var barHeight = Math.Max(2, barRoom * Math.Clamp(chance, 0, 100) / 100);
            var bar = new Rectangle
            {
                Width = barWidth,
                Height = barHeight,
                Fill = _rainBrush,
                Opacity = 0.45,
                RadiusX = 1,
                RadiusY = 1,
            };

            Canvas.SetLeft(bar, Math.Clamp((i * slot) - (barWidth / 2), 0, width - barWidth));
            Canvas.SetTop(bar, height - barHeight);
            _bars.Add(bar);
            _canvas.Children.Add(bar);
        }
    }
}
