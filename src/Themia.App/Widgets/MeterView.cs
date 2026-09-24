using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Services;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.Foundation;

namespace Themia.App.Widgets;

/// <summary>
/// The readout shared by every system meter: a title, the reading, and a sparkline of its history.
/// <para>
/// One view for several widgets rather than several near-identical ones. What differs between CPU,
/// memory, disk and battery is only where the number comes from, so that is the only thing the kind
/// selects.
/// </para>
/// </summary>
internal sealed partial class MeterView : Grid, IWidgetView
{
    private const string Scope = "meter";
    private const int HistoryLength = 60;

    private readonly WidgetKind _kind;
    private readonly TextBlock _title = new();
    private readonly FontIcon _icon = new();
    private readonly TextBlock _primary = new();
    private readonly TextBlock _secondary = new();
    private readonly ProgressBar _bar = new();
    private readonly Polygon _sparkFill = new();
    private readonly Polyline _sparkLine = new();
    private readonly Grid _sparkHost = new();
    private readonly Queue<double> _history = new(HistoryLength);

    private Ticker? _ticker;
    private WidgetConfig _config;
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;

    public MeterView(WidgetKind kind)
    {
        _kind = kind;
        _config = WidgetConfig.CreateDefault(kind);

        var definition = WidgetCatalog.Definition(kind);
        _icon.Glyph = definition.Glyph;
        _title.Text = definition.Label;

        var muted = ThemeBrush("TextFillColorSecondaryBrush", Colors.Gray);
        _title.Foreground = muted;
        _title.VerticalAlignment = VerticalAlignment.Center;
        _icon.Foreground = muted;

        _primary.FontWeight = FontWeights.SemiBold;
        _primary.Foreground = ThemeBrush("TextFillColorPrimaryBrush", Colors.White);
        _primary.Text = "--";
        _primary.VerticalAlignment = VerticalAlignment.Bottom;

        _secondary.Foreground = ThemeBrush("TextFillColorTertiaryBrush", Colors.Gray);
        _secondary.TextTrimming = TextTrimming.CharacterEllipsis;
        _secondary.VerticalAlignment = VerticalAlignment.Bottom;
        _secondary.Margin = new Thickness(0, 0, 0, 2);

        _bar.Minimum = 0;
        _bar.Maximum = 100;
        _bar.HorizontalAlignment = HorizontalAlignment.Stretch;

        BuildSparkline();

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { _icon, _title },
        };

        // The reading on its own line, the sparkline below it. Drawing the trace *behind* the number
        // meant the graph read as decoration and the number was read against a moving background;
        // neither was easy to look at.
        var reading = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _primary, _secondary },
        };

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(header, 0);
        Grid.SetRow(reading, 1);
        Grid.SetRow(_sparkHost, 2);
        Grid.SetRow(_bar, 3);

        Children.Add(header);
        Children.Add(reading);
        Children.Add(_sparkHost);
        Children.Add(_bar);

        SizeChanged += (_, _) => ApplyTypeScale();
    }

    /// <summary>
    /// A filled area under a stroked line — the shape a sparkline is expected to have. The fill
    /// carries the trend at a glance; the line keeps the latest value legible when the tile is small.
    /// </summary>
    private void BuildSparkline()
    {
        var accent = ThemeBrush("AccentFillColorDefaultBrush", Colors.DodgerBlue);

        _sparkFill.Fill = accent;
        _sparkFill.Opacity = 0.18;
        _sparkFill.Stretch = Stretch.None;

        _sparkLine.Stroke = accent;
        _sparkLine.StrokeThickness = 1.5;
        _sparkLine.StrokeLineJoin = PenLineJoin.Round;
        _sparkLine.Stretch = Stretch.None;

        _sparkHost.IsHitTestVisible = false;
        _sparkHost.MinHeight = 24;
        _sparkHost.Margin = new Thickness(0, 6, 0, 6);
        _sparkHost.Children.Add(_sparkFill);
        _sparkHost.Children.Add(_sparkLine);
        _sparkHost.SizeChanged += (_, _) => RedrawSparkline();
    }

    /// <summary>
    /// Size the readout to the tile. The number grows; the title and caption stay close to their
    /// natural size, because a meter whose label is as big as its value reads as two headlines.
    /// </summary>
    private void ApplyTypeScale()
    {
        var width = ActualWidth;
        var height = ActualHeight;

        _primary.FontSize = TypeScale.Fit(width, height, 0.26, 0.30, 16, 88);
        _title.FontSize = TypeScale.Fit(width, height, 0.10, 0.12, 10, 20);
        _icon.FontSize = _title.FontSize;
        _secondary.FontSize = TypeScale.Fit(width, height, 0.070, 0.085, 9, 16);

        _bar.Height = Math.Clamp(height * 0.035, 3, 8);
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config.Kind == _kind ? config : WidgetConfig.CreateDefault(_kind);

        // Battery has no meaningful history at a one-minute cadence, and a flat line reads as a
        // broken graph rather than as a stable charge.
        var graphable = _config is GaugeConfig { ShowGraph: true } && _config is not BatteryConfig;

        _sparkHost.Visibility = graphable ? Visibility.Visible : Visibility.Collapsed;

        // The bar is the fallback readout: with no sparkline there would otherwise be nothing
        // showing the value against its range.
        _bar.Visibility = graphable ? Visibility.Collapsed : Visibility.Visible;

        Resubscribe();
    }

    private void Resubscribe()
    {
        var interval = _config switch
        {
            GaugeConfig gauge => gauge.TickInterval,
            _ => TimeSpan.FromSeconds(1),
        };

        // Only when the cadence actually changed. Dropping and remaking the subscription on every
        // config touch made dragging a settings slider stutter, because each frame tore down the
        // timer subscription for every widget on the desktop and built it again.
        if (_subscription is not null && interval == _subscribedInterval)
        {
            return;
        }

        _subscription?.Dispose();
        _subscribedInterval = interval;
        _subscription = _ticker?.Subscribe(interval, Sample);
    }

    private void Sample()
    {
        MeterReading reading;

        try
        {
            reading = _kind switch
            {
                WidgetKind.Cpu => SystemMetrics.Cpu(),
                WidgetKind.Memory => SystemMetrics.Memory(),
                WidgetKind.Disk => SystemMetrics.Disk((_config as DiskConfig)?.Drive ?? string.Empty),
                WidgetKind.Battery => SystemMetrics.Battery((_config as BatteryConfig)?.ShowTimeRemaining ?? true),
                _ => MeterReading.Unavailable("unsupported"),
            };
        }
        catch (Exception ex)
        {
            // A meter that cannot read its counter shows that it cannot, rather than taking the
            // whole surface down from inside a tick.
            Log.Error(Scope, $"could not sample {_kind}", ex);
            reading = MeterReading.Unavailable("error");
        }

        _primary.Text = reading.Primary;
        _secondary.Text = reading.Secondary ?? string.Empty;
        _secondary.Visibility = string.IsNullOrEmpty(reading.Secondary) ? Visibility.Collapsed : Visibility.Visible;

        _bar.Value = (reading.Fraction ?? 0) * 100;
        _bar.Opacity = reading.Available ? 1 : 0.4;
        _primary.Opacity = reading.Available ? 1 : 0.5;

        if (reading is { Available: true, Fraction: { } fraction })
        {
            if (_history.Count >= HistoryLength)
            {
                _history.Dequeue();
            }

            _history.Enqueue(fraction);
            RedrawSparkline();
        }
    }

    /// <summary>
    /// Redraw the trace. Cheap by construction: two visuals, at most <see cref="HistoryLength"/>
    /// points, rebuilt only on a new sample or a resize.
    /// </summary>
    private void RedrawSparkline()
    {
        if (_sparkHost.Visibility != Visibility.Visible)
        {
            return;
        }

        var width = _sparkHost.ActualWidth;
        var height = _sparkHost.ActualHeight;

        if (width <= 0 || height <= 0 || _history.Count < 2)
        {
            _sparkLine.Points.Clear();
            _sparkFill.Points.Clear();
            return;
        }

        var samples = _history.ToArray();
        var step = width / Math.Max(1, samples.Length - 1);

        // Inset by the stroke width so a reading of 0% or 100% is not clipped in half at the edge.
        var inset = _sparkLine.StrokeThickness;
        var usable = Math.Max(1, height - inset);

        var line = new PointCollection();
        for (var i = 0; i < samples.Length; i++)
        {
            var y = (inset / 2) + (usable - (Math.Clamp(samples[i], 0, 1) * usable));
            line.Add(new Point(i * step, y));
        }

        // The fill is the same trace, closed along the bottom edge.
        var fill = new PointCollection { new(0, height) };
        foreach (var point in line)
        {
            fill.Add(point);
        }

        fill.Add(new Point((samples.Length - 1) * step, height));

        _sparkLine.Points = line;
        _sparkFill.Points = fill;
    }

    public void Detach()
    {
        _subscription?.Dispose();
        _subscription = null;
    }

    private static Brush ThemeBrush(string key, Windows.UI.Color fallback)
        => Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(fallback);
}
