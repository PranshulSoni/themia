using Microsoft.UI;
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

    /// <summary>The graph's minimum height, and what the reading leaves room for when it is shown.</summary>
    /// <summary>The graph never shrinks below this: under it a trace is a smudge, not a chart.</summary>
    private const double SparklineHeight = Fluent.Space6;

    /// <summary>Nor grows past it: a chart taller than the number it annotates inverts the tile.</summary>
    private const double SparklineMaxHeight = 96;

    /// <summary>The share of the tile the graph takes between those two bounds.</summary>
    private const double SparklineShare = 0.32;

    /// <summary>What the fallback bar costs the reading in height, the bar being a thin Fluent one.</summary>
    private const double BarHeight = Fluent.Space1;

    private readonly WidgetKind _kind;
    private readonly TextBlock _primary = Fluent.Text(FluentType.Subtitle);
    private readonly TextBlock _secondary = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly Grid _reading = new();
    private readonly ProgressBar _bar = new();
    private readonly Polygon _sparkFill = new();
    private readonly Polyline _sparkLine = new();
    private readonly Grid _sparkHost = new();
    private readonly StackPanel? _absentState;
    private readonly Queue<double> _history = new(HistoryLength);

    // Looked up once. The reading swaps between them on every sample, and a resource lookup per
    // sample per widget is a dictionary hit and a possible log line for nothing.
    private readonly Brush _readingBrush = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White);
    private readonly Brush _unavailableBrush = FluentLayout.Themed("TextFillColorDisabledBrush", Colors.Gray);
    private readonly Brush _barBrush = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);
    private readonly Brush _barUnavailableBrush = FluentLayout.Themed("AccentFillColorDisabledBrush", Colors.Gray);

    private Ticker? _ticker;
    private WidgetConfig _config;
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;

    // The ramp steps currently applied. Kept so a sample that lands on the same step does not touch
    // the text blocks at all: Present runs once a second, and re-applying a font size is a layout
    // invalidation the tile does not need.
    private TypeStep _heroStep;
    private TypeStep _captionStep;

    private bool _graph;
    private bool _absent;

    // Ping is the one reading that cannot be taken synchronously: a probe takes up to two seconds
    // and Sample() runs on the UI thread. The token source is both the cancel handle and the
    // "a probe is already out" flag — one piece of state, so the two can never disagree.
    private CancellationTokenSource? _probe;
    private string? _probeHost;
    private string? _probeDetail;
    private bool _detached;

    public MeterView(WidgetKind kind)
    {
        // No padding on the view: the graph is a full-bleed band and has to reach the card edge on
        // three sides. Text carries the inset instead, exactly as a list row does — the chart is
        // the widget's surface, not something sitting on it, and a chart floating inside a margin
        // reads as a picture of a chart.
        RowSpacing = Fluent.Space2;

        _kind = kind;
        _config = WidgetConfig.CreateDefault(kind);

        var definition = WidgetCatalog.Definition(kind);

        _heroStep = FluentType.Subtitle;
        _captionStep = FluentType.Caption;

        _primary.Text = "--";
        _primary.Foreground = _readingBrush;
        _primary.VerticalAlignment = VerticalAlignment.Bottom;

        // Lifted off the baseline of the reading it sits beside: bottom-aligning a 12px caption
        // against a 68px number puts it below the number's baseline, which reads as a slipped line.
        _secondary.VerticalAlignment = VerticalAlignment.Bottom;
        _secondary.Margin = new Thickness(0, 0, 0, Fluent.Space1);

        _bar.Minimum = 0;
        _bar.Maximum = 100;
        _bar.HorizontalAlignment = HorizontalAlignment.Stretch;
        _bar.VerticalAlignment = VerticalAlignment.Bottom;
        _bar.Foreground = _barBrush;

        BuildSparkline();

        // Only a battery can be absent — every other meter that cannot be read is a meter that could
        // be read a moment ago, which is [S-82]'s dimmed "--" rather than an empty state.
        _absentState = kind == WidgetKind.Battery ? BuildAbsentState(definition.Glyph) : null;

        // The reading on its own line, the sparkline below it. Drawing the trace *behind* the number
        // meant the graph read as decoration and the number was read against a moving background;
        // neither was easy to look at.
        //
        // A Grid, not a StackPanel: a horizontal StackPanel measures its children with unbounded
        // width, so the caption never learns how much room it has and TextTrimming never fires. On a
        // narrow tile it simply ran past the edge of the card and was cut mid-word — "up 105 KB/".
        // The star column hands it exactly what is left over, so it ellipsises instead.
        _reading.ColumnSpacing = Fluent.Space2;
        _reading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _reading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // A second row for the caption when the tile is too narrow to keep it beside the reading
        // ([S-646]); empty, and so of no height, otherwise.
        _reading.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _reading.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetColumn(_primary, 0);
        Grid.SetColumn(_secondary, 1);
        _reading.Children.Add(_primary);
        _reading.Children.Add(_secondary);

        // The READING takes the slack, not the graph. With the star on the graph row, a tall tile
        // gave the sparkline a 350px band it had no way to fill — a young trace occupies the
        // right-hand slots and the bottom half of the scale, so the tile read as mostly empty
        // with a number stranded at the top. The graph is a band of bounded height at the foot,
        // which is the proportion Windows gives a chart under a readout.
        // Two rows, not three: the graph and the bar are alternatives - one is showing exactly
        // when the other is not - so they share the last row. As separate rows, the grid's row
        // spacing still sat between them, which put an 8px gap under a graph that is supposed to
        // reach the bottom edge of the card. A collapsed row has no height; it still has a gap.
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _reading.VerticalAlignment = VerticalAlignment.Center;
        _reading.Margin = WidgetLayout.SideInset;
        Grid.SetRow(_reading, 0);
        Grid.SetRow(_sparkHost, 1);
        Grid.SetRow(_bar, 1);

        Children.Add(_reading);
        Children.Add(_sparkHost);
        Children.Add(_bar);

        if (_absentState is not null)
        {
            // The star row, so the empty state is centred in what the graph leaves.
            Grid.SetRow(_absentState, 0);
            Children.Add(_absentState);
        }

        SizeChanged += (_, _) => ApplyTypeScale();
    }

    /// <summary>
    /// A filled area under a stroked line — the shape a sparkline is expected to have. The fill
    /// carries the trend at a glance; the line keeps the latest value legible when the tile is small.
    /// </summary>
    private void BuildSparkline()
    {
        var accent = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);

        // The wash under the trace is the accent at low alpha. It is the one opacity in the view
        // that is not standing in for a colour: an area fill under a line is drawn translucent by
        // every chart Windows ships, and there is no named brush for "the accent, quietly".
        _sparkFill.Fill = accent;
        _sparkFill.Opacity = 0.18;
        _sparkFill.Stretch = Stretch.None;

        _sparkLine.Stroke = accent;
        _sparkLine.StrokeThickness = Fluent.ChartStroke;
        _sparkLine.StrokeLineJoin = PenLineJoin.Round;
        _sparkLine.Stretch = Stretch.None;

        _sparkHost.IsHitTestVisible = false;
        _sparkHost.MinHeight = SparklineHeight;
        _sparkHost.Children.Add(_sparkFill);
        _sparkHost.Children.Add(_sparkLine);
        _sparkHost.SizeChanged += (_, _) => RedrawSparkline();
    }

    /// <summary>
    /// The state a machine with no battery shows: the widget's own icon and one line, centred, the
    /// shape Windows uses when a pane has nothing to show. Not a dimmed number and an empty bar,
    /// which read as a broken widget rather than as a desktop.
    /// </summary>
    private static StackPanel BuildAbsentState(string glyph)
    {
        var icon = Fluent.Icon(glyph, "TextFillColorTertiaryBrush");
        icon.HorizontalAlignment = HorizontalAlignment.Center;

        var text = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
        text.Text = "No battery";
        text.HorizontalAlignment = HorizontalAlignment.Center;

        var panel = new StackPanel
        {
            Spacing = Fluent.Space2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };

        panel.Children.Add(icon);
        panel.Children.Add(text);
        return panel;
    }

    /// <summary>
    /// Step the readout to the tile. The reading is the only thing that grows: the caption stays
    /// where Fluent puts it, because a meter whose label is as big as its value reads as two
    /// headlines.
    /// <para>
    /// A step, not a fraction of the tile. The reading moves Subtitle → Title → TitleLarge → Display
    /// as the tile grows and stops there: the ramp is where Windows stops having anything larger to
    /// say, and a meter is not a clock.
    /// </para>
    /// </summary>
    private void ApplyTypeScale()
    {
        // The text is inset even though the view is not, so the width it has is still the tile
        // less both insets. The height is the whole tile: the graph runs to the bottom edge.
        var width = ActualWidth - (Fluent.Space4 * 2);
        var height = ActualHeight;

        // The graph is a proportion of the tile between a readable floor and a ceiling, rather
        // than whatever is left over. Left over is how it ended up taller than the number it is
        // annotating.
        var band = Math.Clamp(height * SparklineShare, SparklineHeight, SparklineMaxHeight);
        _sparkHost.Height = band;

        // What is left for the reading once the gap below it and whichever of the graph and the bar
        // is showing have had their fixed share. The header is the card's now and sits outside this
        // box, so it no longer takes anything out of it.
        var reserved = Fluent.Space2
            + (_sparkHost.Visibility == Visibility.Visible ? band : BarHeight);

        // The caption shares the line with the reading, so the reading gets at most half of it.
        // Letting the number take the whole line is how a tall narrow tile ended up with a 68px
        // reading and a caption trimmed to a single ellipsis.
        var hasCaption = _secondary.Visibility == Visibility.Visible;
        var readingWidth = hasCaption ? width / 2 : width;
        var readingHeight = Math.Min(height - reserved, FluentType.Display.LineHeight);
        var characters = ReadingCharacters(_kind);

        var hero = Reading(readingWidth, readingHeight, characters);

        // [S-646] And when even half the line leaves the caption too little, the caption moves
        // under the reading and the reading has the whole width — rather than "30 G…". It moves
        // there too when that simply draws the reading larger, which is what a tall, narrow tile
        // with height to spare wants.
        var under = Reading(width, readingHeight - FluentType.Body.LineHeight, characters);
        var stacked = hasCaption && (under.FontSize > hero.FontSize || !ReadoutLayout.CaptionFitsBeside(
            width,
            hero,
            characters,
            CaptionFor(hero),
            CaptionCharacters(),
            Fluent.Space2));

        if (stacked)
        {
            hero = under;
        }

        var caption = CaptionFor(hero);

        Grid.SetRow(_secondary, stacked ? 1 : 0);
        Grid.SetColumn(_secondary, stacked ? 0 : 1);
        Grid.SetColumnSpan(_secondary, stacked ? 2 : 1);
        _secondary.VerticalAlignment = stacked ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        _secondary.Margin = stacked ? new Thickness(0) : new Thickness(0, 0, 0, Fluent.Space1);

        if (hero != _heroStep)
        {
            _heroStep = hero;
            _primary.Apply(hero);
        }

        if (caption != _captionStep)
        {
            _captionStep = caption;
            _secondary.Apply(caption);
        }
    }

    /// <summary>
    /// How wide the reading has to be able to get, in characters — the widest string this kind can
    /// produce, not the one currently on screen.
    /// <para>
    /// Fitting to the live text would mean "9%" and "100%" choose different steps, so the number
    /// would change size between two samples a second apart. The kind's own worst case is stable.
    /// </para>
    /// </summary>
    private static double ReadingCharacters(WidgetKind kind) => kind switch
    {
        WidgetKind.Network => 8,  // "1.2 MB/s"
        WidgetKind.Ping => 6,     // "999 ms"
        _ => 4,                   // "100%"
    };

    /// <summary>
    /// The reading's step for a box. Never below Subtitle: the reading is the point of the tile and
    /// must not end up smaller than the card's own header label.
    /// </summary>
    private static TypeStep Reading(double width, double height, double characters)
    {
        var hero = FluentType.Hero(width, height, characters);
        return hero.FontSize < FluentType.Subtitle.FontSize ? FluentType.Subtitle : hero;
    }

    /// <summary>
    /// The caption steps once, with the reading, rather than sliding: next to a 28px number a 12px
    /// caption is right, next to a 68px one it is a footnote.
    /// </summary>
    private static TypeStep CaptionFor(TypeStep reading)
        => reading.FontSize >= FluentType.Title.FontSize ? FluentType.Body : FluentType.Caption;

    /// <summary>
    /// How long the caption is, for deciding where it goes. The throughput caption is the one that
    /// changes length from second to second ("up 0 B/s", "up 105 KB/s"), so it is held at its
    /// worst case — otherwise the caption would hop between beside and under on every sample.
    /// </summary>
    private double CaptionCharacters() => _kind == WidgetKind.Network
        ? Math.Max(11, _secondary.Text.Length)
        : _secondary.Text.Length;

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
        _graph = _config is GaugeConfig { ShowGraph: true } && _config is not BatteryConfig;

        UpdateVisibility();

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

    /// <summary>
    /// Which of the reading, the graph, the fallback bar and the empty state are on screen. One
    /// place, so the two things that move them — the config and an absent battery — cannot disagree.
    /// </summary>
    private void UpdateVisibility()
    {
        _reading.Visibility = _absent ? Visibility.Collapsed : Visibility.Visible;
        _sparkHost.Visibility = _graph && !_absent ? Visibility.Visible : Visibility.Collapsed;

        // The bar is the fallback readout: with no sparkline there would otherwise be nothing
        // showing the value against its range.
        _bar.Visibility = !_graph && !_absent ? Visibility.Visible : Visibility.Collapsed;

        if (_absentState is not null)
        {
            _absentState.Visibility = _absent ? Visibility.Visible : Visibility.Collapsed;
        }

        ApplyTypeScale();
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
        // A desktop with no battery is not a battery meter that failed, so it gets an empty state
        // rather than a dimmed "--". The reading says which it is; matching on the caption text
        // would make a display decision depend on the exact wording of a string in another file.
        var absent = reading.Absent;

        if (absent != _absent)
        {
            _absent = absent;
            UpdateVisibility();
        }

        _primary.Text = reading.Primary;
        _secondary.Text = reading.Secondary ?? string.Empty;
        _secondary.Visibility = string.IsNullOrEmpty(reading.Secondary) ? Visibility.Collapsed : Visibility.Visible;

        // Unavailable is a colour, not an opacity: the disabled text and accent brushes are what
        // every other disabled thing on the desktop uses, and they follow the theme.
        _primary.Foreground = reading.Available ? _readingBrush : _unavailableBrush;

        _bar.Value = (reading.Fraction ?? 0) * 100;
        _bar.Foreground = reading.Available ? _barBrush : _barUnavailableBrush;

        // The caption can appear and disappear between samples, which changes how much of the line
        // the reading may take.
        ApplyTypeScale();

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
