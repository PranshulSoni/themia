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
        _trace.Opacity = 0.45;
        _trace.IsHitTestVisible = false;

        // Stretch.None: the points are already computed against the host's measured size, so letting
        // the shape scale them again would distort the trace.
        _trace.Stretch = Stretch.None;

        _traceHost.Children.Add(_trace);
        _traceHost.IsHitTestVisible = false;
        _traceHost.SizeChanged += (_, _) => RedrawTrace();

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { _icon, _title },
        };

        // Rows: header and footer take what they need, the middle stretches. That is what makes the
        // meter fill its tile however it is resized — the same shape the original uses, where the
        // graph is `flex: 1` and the labels are `flex-shrink: 0`.
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(header, 0);

        // The reading sits over the trace rather than beside it, so neither has to give up width.
        _primary.HorizontalAlignment = HorizontalAlignment.Left;
        _primary.VerticalAlignment = VerticalAlignment.Center;

        var middle = new Grid { Children = { _traceHost, _primary } };
        Grid.SetRow(middle, 1);

        var footer = new StackPanel { Children = { _bar, _secondary } };
        Grid.SetRow(footer, 2);

        Children.Add(header);
        Children.Add(middle);
        Children.Add(footer);

        SizeChanged += (_, _) => ApplyTypeScale();
    }

    /// <summary>
    /// Size the readout to the tile. The number is the thing that should grow; the title and the
    /// caption stay close to their natural size, because a meter whose label is as big as its value
    /// reads as two competing headlines.
    /// </summary>
    private void ApplyTypeScale()
    {
        var width = ActualWidth;
        var height = ActualHeight;

        // Four characters at most ("100%"), so the width factor is generous compared to the clock's.
        _primary.FontSize = TypeScale.Fit(width, height, 0.30, 0.34, 16, 96);

        _title.FontSize = TypeScale.Fit(width, height, 0.10, 0.12, 10, 20);
        _icon.FontSize = _title.FontSize;
        _secondary.FontSize = TypeScale.Fit(width, height, 0.075, 0.09, 9, 16);

        // The bar is part of the readout, so it thickens with everything else rather than staying a
        // hairline on a large tile.
        _bar.Height = Math.Clamp(height * 0.04, 3, 10);
        _bar.Margin = new Thickness(0, 4, 0, 4);
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
