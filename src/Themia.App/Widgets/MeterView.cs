using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Services;
using Themia.App.Shell;
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

    // Ping is the one reading that cannot be taken synchronously: a probe takes up to two seconds
    // and Sample() runs on the UI thread. The token source is both the cancel handle and the
    // "a probe is already out" flag — one piece of state, so the two can never disagree.
    private CancellationTokenSource? _probe;
    private string? _probeHost;
    private string? _probeDetail;
    private bool _detached;

    public MeterView(WidgetKind kind)
    {
        _kind = kind;
        _config = WidgetConfig.CreateDefault(kind);

        var definition = WidgetCatalog.Definition(kind);
        _icon.Glyph = definition.Glyph;
        _title.Text = definition.Label;

        var muted = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray);
        _title.Foreground = muted;
        _title.VerticalAlignment = VerticalAlignment.Center;
        _icon.Foreground = muted;

        _primary.FontWeight = FontWeights.SemiBold;
        _primary.Foreground = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White);
        _primary.Text = "--";
        _primary.VerticalAlignment = VerticalAlignment.Bottom;

        _secondary.Foreground = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);
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
        //
        // A Grid, not a StackPanel: a horizontal StackPanel measures its children with unbounded
        // width, so the caption never learns how much room it has and TextTrimming never fires. On a
        // narrow tile it simply ran past the edge of the card and was cut mid-word — "up 105 KB/".
        // The star column hands it exactly what is left over, so it ellipsises instead.
        var reading = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };

        Grid.SetColumn(_primary, 0);
        Grid.SetColumn(_secondary, 1);
        reading.Children.Add(_primary);
        reading.Children.Add(_secondary);

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
        var accent = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);

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
        _detached = false;
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

        var previousHost = _probeHost;
        _probeHost = (_config as PingConfig)?.EffectiveHost;

        var hostChanged = _probeHost is not null && _probeHost != previousHost;

        // Before Resubscribe, which fires a tick of its own and would start a probe on the new
        // host — cancelling afterwards would kill exactly that probe and leave the tile waiting out
        // a whole interval for the next one.
        if (hostChanged)
        {
            CancelProbe();

            // A different host is a different measurement, so the samples already on the sparkline
            // belong to something else. On the first config there is nothing to clear.
            if (previousHost is not null)
            {
                _history.Clear();
                RedrawSparkline();
            }
        }

        var resubscribed = Resubscribe();

        // Only a host change at an unchanged cadence still needs a push. Without it, editing the
        // host would show nothing until the old interval elapsed, which at five minutes reads as a
        // broken widget.
        if (hostChanged && !resubscribed)
        {
            Sample();
        }
    }

    /// <summary>True when the subscription was rebuilt, which also fires an immediate tick.</summary>
    private bool Resubscribe()
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
            return false;
        }

        _subscription?.Dispose();
        _subscribedInterval = interval;
        _subscription = _ticker?.Subscribe(interval, Sample);
        return _subscription is not null;
    }

    private void Sample()
    {
        // Ping cannot answer inside the tick — it is a network round trip. Blocking on it here
        // would block the dispatcher that its own continuation needs, so this starts the probe and
        // the reading lands later.
        if (_kind == WidgetKind.Ping)
        {
            StartProbe();
            return;
        }

        MeterReading reading;

        try
        {
            reading = _kind switch
            {
                WidgetKind.Cpu => SystemMetrics.Cpu(),
                WidgetKind.Memory => SystemMetrics.Memory(),
                WidgetKind.Disk => SystemMetrics.Disk((_config as DiskConfig)?.Drive ?? string.Empty),
                WidgetKind.Battery => SystemMetrics.Battery((_config as BatteryConfig)?.ShowTimeRemaining ?? true),
                WidgetKind.Network => NetworkMetrics.Network(),
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

        Present(reading);
    }

    /// <summary>
    /// Send one probe, unless the last one is still out.
    /// <para>
    /// A dead host takes the full two-second timeout to answer, which at a one-second cadence would
    /// mean two probes in flight, then three; the queue grows for as long as the host stays down and
    /// every reading the tile shows is older than the last tick. One at a time means a slow host
    /// simply updates more slowly, which is the truth.
    /// </para>
    /// </summary>
    private void StartProbe()
    {
        if (_probe is not null || _detached)
        {
            return;
        }

        var host = (_config as PingConfig)?.EffectiveHost ?? PingConfig.DefaultHost;
        var probe = new CancellationTokenSource();
        _probe = probe;

        _ = ProbeAsync(host, probe);
    }

    private async Task ProbeAsync(string host, CancellationTokenSource probe)
    {
        try
        {
            var outcome = await NetworkMetrics.PingAsync(host, probe.Token).ConfigureAwait(true);

            // A late answer from a detached view, or from a probe aimed at a host the user has
            // since changed, is not a reading — it is a stale fact about something else.
            if (_detached || !ReferenceEquals(_probe, probe))
            {
                return;
            }

            NoteProbeState(outcome.Detail);
            Present(outcome.Reading);
        }
        catch (OperationCanceledException)
        {
            // ignore: cancelled by Detach or by a host change, so there is nothing left to show to
        }
        catch (Exception ex)
        {
            Log.Error(Scope, $"could not probe {host}", ex);

            if (!_detached && ReferenceEquals(_probe, probe))
            {
                Present(MeterReading.Unavailable("error"));
            }
        }
        finally
        {
            // The task that created the source owns it, on every path: cancelling elsewhere only
            // clears the field, so exactly one Dispose can ever run for one source.
            if (ReferenceEquals(_probe, probe))
            {
                _probe = null;
            }

            probe.Dispose();
        }
    }

    /// <summary>
    /// Log a probe only when it says something the previous one did not.
    /// <para>
    /// An unreachable host answers identically on every tick, for as long as it stays unreachable.
    /// Logging each one would bury everything else in the file within an hour and tell the reader
    /// nothing they could not learn from the first line and the last.
    /// </para>
    /// </summary>
    private void NoteProbeState(string? detail)
    {
        if (detail == _probeDetail)
        {
            return;
        }

        var recovered = detail is null;
        _probeDetail = detail;

        if (recovered)
        {
            Log.Info(Scope, $"ping answering again: {_probeHost}");
            return;
        }

        Log.Warn(Scope, detail!);
    }

    private void CancelProbe()
    {
        var probe = _probe;
        _probe = null;

        // Cancel only. Disposal belongs to ProbeAsync's finally, which runs on the continuation
        // this cancel is about to release.
        probe?.Cancel();
    }

    /// <summary>Put one reading on screen, wherever it came from.</summary>
    private void Present(MeterReading reading)
    {
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

        // Inset by the stroke width so a reading of 0% or 100% is not clipped in half at the edge.
        var inset = _sparkLine.StrokeThickness;
        var usable = Math.Max(1, height - inset);

        // X comes from Sparkline, which pins the newest sample to the right edge and steps left one
        // fixed slot per sample. Spreading however many samples are held across the full width
        // instead would make the trace rescale every second for the first minute of the widget's
        // life, which reads as the data moving when only the buffer is filling.
        var line = new PointCollection();
        for (var i = 0; i < samples.Length; i++)
        {
            var x = Sparkline.SampleX(i, samples.Length, HistoryLength) * width;
            var y = (inset / 2) + (Sparkline.SampleY(samples[i]) * usable);
            line.Add(new Point(x, y));
        }

        // The fill is the same trace, closed down to the baseline at each end.
        var fill = new PointCollection { new(line[0].X, height) };
        foreach (var point in line)
        {
            fill.Add(point);
        }

        fill.Add(new Point(line[^1].X, height));

        _sparkLine.Points = line;
        _sparkFill.Points = fill;
    }

    public void Detach()
    {
        _detached = true;
        _subscription?.Dispose();
        _subscription = null;
        CancelProbe();
    }

}
