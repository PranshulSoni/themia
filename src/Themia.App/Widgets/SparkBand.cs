using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Shell;
using Themia.Core.Widgets;
using Windows.Foundation;

namespace Themia.App.Widgets;

/// <summary>
/// A meter's sparkline as a reusable band: a translucent area under a 2px line, newest sample at
/// the right edge, on a fixed-slot x-axis so the trace grows in from the right instead of
/// rescaling while the buffer fills.
/// <para>
/// The same drawing the CPU and memory tiles make, so a GPU tile beside them reads as one family.
/// It is a separate element because the GPU widget can show several adapters, each with its own
/// history, which the single-reading meter view has no place for.
/// </para>
/// </summary>
internal sealed partial class SparkBand : Grid
{
    private readonly Polygon _fill = new();
    private readonly Polyline _line = new();
    private readonly Queue<double> _history;
    private readonly int _capacity;

    public SparkBand(int capacity)
    {
        _capacity = Math.Max(2, capacity);
        _history = new Queue<double>(_capacity);

        var accent = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);

        // The wash is the stroke's colour at low alpha, applied to the shape and never to the shared
        // brush, which every other accent surface in the app also paints with.
        _fill.Fill = accent;
        _fill.Opacity = Fluent.ChartWash;
        _fill.Stretch = Stretch.None;

        _line.Stroke = accent;
        _line.StrokeThickness = Fluent.ChartStroke;
        _line.StrokeLineJoin = PenLineJoin.Round;
        _line.Stretch = Stretch.None;

        IsHitTestVisible = false;
        Children.Add(_fill);
        Children.Add(_line);
        SizeChanged += (_, _) => Redraw();
    }

    /// <summary>Paint the trace in another colour — a pressure colour, for instance.</summary>
    public void SetBrush(Brush brush)
    {
        if (ReferenceEquals(_line.Stroke, brush))
        {
            return;
        }

        _line.Stroke = brush;
        _fill.Fill = brush;
    }

    /// <summary>Add a reading, 0..1. Non-finite readings are not samples and are dropped.</summary>
    public void Push(double fraction)
    {
        if (!double.IsFinite(fraction))
        {
            return;
        }

        if (_history.Count >= _capacity)
        {
            _history.Dequeue();
        }

        _history.Enqueue(Math.Clamp(fraction, 0, 1));
        Redraw();
    }

    /// <summary>Forget the history: the next reading is of something else.</summary>
    public void Clear()
    {
        _history.Clear();
        Redraw();
    }

    private void Redraw()
    {
        var width = ActualWidth;
        var height = ActualHeight;

        if (width <= 0 || height <= 0 || _history.Count < 2)
        {
            _line.Points.Clear();
            _fill.Points.Clear();
            return;
        }

        var samples = _history.ToArray();

        // Inset by the stroke so 0% and 100% are not clipped in half at the edge.
        var inset = _line.StrokeThickness;
        var usable = Math.Max(1, height - inset);

        var line = new PointCollection();
        for (var i = 0; i < samples.Length; i++)
        {
            var x = Sparkline.SampleX(i, samples.Length, _capacity) * width;
            var y = (inset / 2) + (Sparkline.SampleY(samples[i]) * usable);
            line.Add(new Point(x, y));
        }

        var fill = new PointCollection { new(line[0].X, height) };
        foreach (var point in line)
        {
            fill.Add(point);
        }

        fill.Add(new Point(line[^1].X, height));

        _line.Points = line;
        _fill.Points = fill;
    }
}
