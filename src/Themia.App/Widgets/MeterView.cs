using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Services;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Windows.Foundation;

namespace Themia.App.Widgets;

/// <summary>
/// The readout shared by every system meter: a title, a big number, a bar and a history trace.
/// <para>
/// One view for four widgets rather than four near-identical ones. What differs between CPU, memory,
/// disk and battery is only where the number comes from, so that is the only thing the kind selects.
/// </para>
/// </summary>
internal sealed partial class MeterView : Grid, IWidgetView
{
    private const string Scope = "meter";
    private const int HistoryLength = 60;

    private readonly WidgetKind _kind;
    private readonly TextBlock _title = new();
    private readonly FontIcon _icon = new() { FontSize = 12 };
    private readonly TextBlock _primary = new();
    private readonly TextBlock _secondary = new();
    private readonly ProgressBar _bar = new();
    private readonly Polyline _trace = new();
    private readonly Grid _traceHost = new();
    private readonly Queue<double> _history = new(HistoryLength);

    private Ticker? _ticker;
    private WidgetConfig _config;
    private IDisposable? _subscription;

    public MeterView(WidgetKind kind)
    {
        _kind = kind;
        _config = WidgetConfig.CreateDefault(kind);

        var definition = WidgetCatalog.Definition(kind);
        _icon.Glyph = definition.Glyph;
        _title.Text = definition.Label;

        _title.FontSize = 12;
        _title.Foreground = ThemeBrush("TextFillColorSecondaryBrush", Colors.Gray);
        _title.VerticalAlignment = VerticalAlignment.Center;
        _icon.Foreground = _title.Foreground;

        _primary.FontSize = 34;
        _primary.FontWeight = FontWeights.SemiBold;
        _primary.Foreground = ThemeBrush("TextFillColorPrimaryBrush", Colors.White);
        _primary.Text = "--";

        _secondary.FontSize = 11;
        _secondary.Foreground = ThemeBrush("TextFillColorTertiaryBrush", Colors.Gray);
        _secondary.TextTrimming = TextTrimming.CharacterEllipsis;

        _bar.Minimum = 0;
        _bar.Maximum = 100;
        _bar.HorizontalAlignment = HorizontalAlignment.Stretch;
        _bar.Margin = new Thickness(0, 2, 0, 4);

        _trace.Stroke = ThemeBrush("AccentFillColorDefaultBrush", Colors.DodgerBlue);
        _trace.StrokeThickness = 1.5;
        _trace.Opacity = 0.35;
        _trace.IsHitTestVisible = false;

        _traceHost.Children.Add(_trace);
        _traceHost.IsHitTestVisible = false;
        _traceHost.SizeChanged += (_, _) => RedrawTrace();

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { _icon, _title },
        };

        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { header, _primary, _bar, _secondary },
        };

        Children.Add(_traceHost);
        Children.Add(stack);
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config.Kind == _kind ? config : WidgetConfig.CreateDefault(_kind);

        var showGraph = _config is GaugeConfig { ShowGraph: true };
        _traceHost.Visibility = showGraph ? Visibility.Visible : Visibility.Collapsed;

        if (_config is BatteryConfig)
        {
            // Battery has no meaningful history at a one-minute cadence, and a flat line reads as
            // a broken graph rather than a stable charge.
            _traceHost.Visibility = Visibility.Collapsed;
        }

        Resubscribe();
    }

    private void Resubscribe()
    {
        _subscription?.Dispose();

        var interval = _config switch
        {
            GaugeConfig gauge => gauge.TickInterval,
            _ => TimeSpan.FromSeconds(1),
        };

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
            RedrawTrace();
        }
    }

    /// <summary>
    /// Draw the history as a single polyline. The trace is decoration, so it is cheap by
    /// construction: one visual, at most <see cref="HistoryLength"/> points, redrawn only on a new
    /// sample or a resize.
    /// </summary>
    private void RedrawTrace()
    {
        if (_traceHost.Visibility != Visibility.Visible || _history.Count < 2)
        {
            _trace.Points.Clear();
            return;
        }

        var width = _traceHost.ActualWidth;
        var height = _traceHost.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var points = new PointCollection();
        var samples = _history.ToArray();
        var step = width / Math.Max(1, samples.Length - 1);

        for (var i = 0; i < samples.Length; i++)
        {
            points.Add(new Point(i * step, height - (Math.Clamp(samples[i], 0, 1) * height)));
        }

        _trace.Points = points;
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
