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
/// <para>
/// [S-664] Its disclosure ladder, smallest tile first: the reading; its caption (beside it, or under
/// it when the line is too narrow); the sparkline (or, with the graph off, a bar); for the meters
/// whose value moves, the min / avg / max over the chart — a line once the tile is Regular in
/// height, three stat tiles once it is Expanded in width (beside the reading on a wide strip, under
/// it on a panel) or Expanded in height on a tall column (one under another) — and for a meter whose value does not move, the same tiles carry the reading's
/// own breakdown instead; a scale line naming the span the chart covers, once it is Expanded both
/// ways. A wider chart spans a longer history
/// rather than drawing the same minute with fatter slots. Whatever the rungs leave goes to the
/// reading up to the top of the ramp, and past that to the chart.
/// </para>
/// </summary>
internal sealed partial class MeterView : Grid, IWidgetView, IWidgetSizeAware
{
    private const string Scope = "meter";

    /// <summary>The graph never shrinks below this: under it a trace is a smudge, not a chart.</summary>
    private const double SparklineHeight = Fluent.Space6;

    /// <summary>What the fallback bar costs the reading in height, the bar being a thin Fluent one.</summary>
    private const double BarHeight = Fluent.Space1;

    private readonly WidgetKind _kind;
    private readonly MeterUnit _unit;
    private readonly TextBlock _primary = Fluent.Text(FluentType.Subtitle);
    private readonly TextBlock _secondary = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly TextBlock _stats = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
    private readonly TextBlock _span = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly TextBlock _top = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly Grid _scale = new();
    private readonly Grid _tiles = new();
    private readonly TextBlock[] _tileLabels = new TextBlock[3];
    private readonly TextBlock[] _tileValues = new TextBlock[3];

    // What the tiles show when the value does not move: the reading's own breakdown (a drive's
    // used / free / total), straight from the last sample.
    private IReadOnlyList<MeterFact>? _facts;
    private bool _tilesShowFacts;
    private readonly Grid _reading = new();
    private readonly ProgressBar _bar = new();
    private readonly Polygon _sparkFill = new();
    private readonly Polyline _sparkLine = new();
    private readonly Grid _sparkHost = new();
    private readonly StackPanel? _absentState;

    // Two histories side by side: the fraction the chart plots, and the value in the reading's own
    // unit that the summary line describes. Both hold the most a chart can span; how many of them
    // are drawn is the window, which follows the chart's width.
    private readonly Queue<double> _history = new(MeterHistory.Capacity);
    private readonly Queue<double> _values = new(MeterHistory.Capacity);

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

    // The last area the card gave the view, and what the ladder settled on there.
    private WidgetArea _area;
    private int _window = MeterHistory.MinimumWindow;
    private bool _compactStats;
    private TypeStep _tileStep = FluentType.BodyStrong;

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
        _kind = kind;
        _unit = kind switch
        {
            WidgetKind.Network => MeterUnit.Rate,
            WidgetKind.Ping => MeterUnit.Latency,
            _ => MeterUnit.Percent,
        };

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

        // The scale line: the span on the left, the top of the scale on the right — what Task
        // Manager prints round its charts, so a large chart says what it is a chart of.
        _scale.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _scale.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _top.HorizontalAlignment = HorizontalAlignment.Right;
        _top.Text = _unit == MeterUnit.Percent ? "100%" : string.Empty;
        Grid.SetColumn(_top, 1);
        _scale.Children.Add(_span);
        _scale.Children.Add(_top);

        BuildTiles();

        // One row per rung, top to bottom: the reading takes the star row and is centred in it; each
        // rung below is Auto and carries the gap above itself as a margin. Not RowSpacing: a
        // collapsed row has no height but still has its spacing, which put an 8px gap under a graph
        // that is supposed to reach the bottom edge of the card. The graph and the bar are
        // alternatives, so they share the last row.
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _reading.VerticalAlignment = VerticalAlignment.Center;
        _reading.Margin = WidgetLayout.SideInset;
        _stats.Margin = new Thickness(Fluent.Space4, Fluent.Space2, Fluent.Space4, 0);
        _scale.Margin = new Thickness(Fluent.Space4, Fluent.Space2, Fluent.Space4, 0);
        _tiles.Visibility = Visibility.Collapsed;
        _sparkHost.Margin = new Thickness(0, Fluent.Space2, 0, 0);
        _bar.Margin = new Thickness(0, Fluent.Space2, 0, 0);

        Grid.SetRow(_reading, 0);
        Grid.SetRow(_stats, 1);
        Grid.SetRow(_scale, 2);
        Grid.SetRow(_sparkHost, 3);
        Grid.SetRow(_bar, 3);

        Children.Add(_reading);
        Children.Add(_stats);
        Children.Add(_scale);
        Children.Add(_tiles);
        Children.Add(_sparkHost);
        Children.Add(_bar);

        if (_absentState is not null)
        {
            // Across every row, so the empty state is centred in the whole tile.
            Grid.SetRowSpan(_absentState, 4);
            Children.Add(_absentState);
        }
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
    /// Three stat tiles — a caption over a value, the shape Windows uses for a figure in a pane —
    /// for a tile wide enough that one summary line would leave most of its width empty.
    /// </summary>
    private void BuildTiles()
    {
        string[] labels = ["Average", "Peak", "Low"];
        _tiles.ColumnSpacing = Fluent.Space5;
        _tiles.VerticalAlignment = VerticalAlignment.Center;

        for (var i = 0; i < labels.Length; i++)
        {
            _tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _tiles.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
            label.Text = labels[i];
            _tileLabels[i] = label;

            var value = Fluent.Text(_tileStep);
            value.Text = "--";
            _tileValues[i] = value;

            var tile = new StackPanel { Children = { label, value } };
            Grid.SetColumn(tile, i);
            _tiles.Children.Add(tile);
        }
    }

    /// <summary>
    /// Lay the tiles out across (one row of three) or down (a column of three). The unused axis has
    /// no spacing, or its empty rows or columns would still add their gaps.
    /// </summary>
    private void ArrangeTiles(bool stacked)
    {
        _tiles.ColumnSpacing = stacked ? 0 : Fluent.Space5;
        _tiles.RowSpacing = stacked ? Fluent.Space2 : 0;

        for (var i = 0; i < _tiles.Children.Count; i++)
        {
            Grid.SetColumn((FrameworkElement)_tiles.Children[i], stacked ? 0 : i);
            Grid.SetRow((FrameworkElement)_tiles.Children[i], stacked ? i : 0);
        }
    }

    /// <summary>How wide the three tiles are at a step, for deciding whether they fit beside the reading.</summary>
    private double TilesWidth(TypeStep step)
    {
        var characters = _tilesShowFacts && _facts is { } facts
            ? facts.Max(f => f.Value.Length)
            : MeterHistory.ValueCharacters(_unit);

        var tile = Math.Max(
            FluentType.EstimateWidth(FluentType.Caption, 9),
            FluentType.EstimateWidth(step, characters));

        return (3 * tile) + (2 * Fluent.Space5);
    }

    /// <summary>[S-663] The card's word on how much room the meter has.</summary>
    public void ApplySize(WidgetArea area)
    {
        _area = area;
        Lay();
        RedrawSparkline();
    }

    /// <summary>Whether this kind's value moves enough for a min / avg / max to say anything.</summary>
    private bool Moves => _kind is WidgetKind.Cpu or WidgetKind.Memory or WidgetKind.Network or WidgetKind.Ping;

    /// <summary>
    /// Climb the ladder for the current area: decide which rungs are up, then size the reading and
    /// the chart from what they leave.
    /// <para>
    /// The reading is the only thing that grows in type: the caption stays where Fluent puts it,
    /// because a meter whose label is as big as its value reads as two headlines. It grows by ramp
    /// step, Subtitle → Title → TitleLarge → Display, and with a chart below it stops there — a
    /// meter is not a clock, and past that the room is worth more to the chart as a longer, taller
    /// history. With no chart (a battery, or the graph turned off) the reading fills the tile.
    /// </para>
    /// </summary>
    private void Lay()
    {
        var absent = _absent;
        _reading.Visibility = absent ? Visibility.Collapsed : Visibility.Visible;

        if (_absentState is not null)
        {
            _absentState.Visibility = absent ? Visibility.Visible : Visibility.Collapsed;
        }

        // The text is inset even though the view is not, so the width it has is the tile less both
        // insets. The height is the whole slot: the graph runs to the bottom edge.
        var width = Math.Max(0, _area.Width - (Fluent.Space4 * 2));
        var room = new Disclosure(width, _area.Height, Fluent.Space2);

        var hasCaption = _secondary.Visibility == Visibility.Visible;
        var characters = ReadingCharacters(_kind);

        // Rung 1, always: the reading at its smallest, with its caption under it if even then the
        // line cannot hold both.
        var captionUnder = hasCaption && !ReadoutLayout.CaptionFitsBeside(
            width, FluentType.Subtitle, characters, FluentType.Caption, CaptionCharacters(), Fluent.Space2);
        var readingFloor = FluentType.Subtitle.LineHeight + (captionUnder ? FluentType.Caption.LineHeight : 0);
        room.Reserve(readingFloor);

        // Rung 2: the chart at its minimum, or the bar when the graph is off.
        var graph = !absent && _graph && room.Take(SparklineHeight);
        var bar = !absent && !_graph && room.Take(BarHeight);

        // Rung 3: min / avg / max. On a wide tile as stat tiles — beside the reading on a strip,
        // where they cost no height, or under it on a panel — otherwise as one line, in the long
        // form when the line holds it.
        var summarise = graph && Moves;
        _tilesShowFacts = !absent && !summarise && _facts is { Count: 3 };
        var wideStats = (summarise || _tilesShowFacts) && _area.WidthClass >= SizeClass.Expanded;
        _tileStep = _area.Least >= SizeClass.Huge ? FluentType.Subtitle : FluentType.BodyStrong;

        var tilesWidth = TilesWidth(_tileStep);
        var tilesBeside = wideStats && _area.Shape == AreaShape.Wide
            && width >= tilesWidth + Fluent.Space6 + FluentType.EstimateWidth(FluentType.Subtitle, characters);
        var tilesBelow = wideStats && !tilesBeside && _area.HeightClass >= SizeClass.Regular
            && room.Take(FluentType.Caption.LineHeight + _tileStep.LineHeight, tilesWidth);

        // A tall column has the height and not the width: the same three tiles, one under another.
        var tileHeight = FluentType.Caption.LineHeight + _tileStep.LineHeight;
        var tilesStacked = (summarise || _tilesShowFacts) && !wideStats
            && _area.Shape == AreaShape.Tall && _area.HeightClass >= SizeClass.Expanded
            && room.Take((3 * tileHeight) + (2 * Fluent.Space2), (tilesWidth - (2 * Fluent.Space5)) / 3);

        _compactStats = width < FluentType.EstimateWidth(FluentType.Caption, MeterHistory.SummaryCharacters(_unit, compact: false));
        var stats = summarise && !wideStats && !tilesStacked && _area.HeightClass >= SizeClass.Regular && room.Take(
            FluentType.Caption.LineHeight,
            FluentType.EstimateWidth(FluentType.Caption, MeterHistory.SummaryCharacters(_unit, compact: true)));

        _tiles.Visibility = tilesBeside || tilesBelow || tilesStacked ? Visibility.Visible : Visibility.Collapsed;
        ArrangeTiles(tilesStacked);
        Grid.SetRow(_tiles, tilesBeside ? 0 : 1);
        _tiles.HorizontalAlignment = tilesBeside ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _tiles.Margin = tilesBeside
            ? new Thickness(0, 0, Fluent.Space4, 0)
            : new Thickness(Fluent.Space4, Fluent.Space2, Fluent.Space4, 0);

        foreach (var value in _tileValues)
        {
            value.Apply(_tileStep);
        }

        // Beside, the tiles have their own column: the reading and its caption stop short of it.
        var readingWidth = tilesBeside ? width - tilesWidth - Fluent.Space6 : width;
        _reading.Margin = tilesBeside
            ? new Thickness(Fluent.Space4, 0, Fluent.Space4 + tilesWidth + Fluent.Space6, 0)
            : WidgetLayout.SideInset;

        // Rung 4: the scale line, on a tile large both ways.
        var scale = graph && _area.AtLeast(SizeClass.Expanded, SizeClass.Expanded) && room.Take(FluentType.Caption.LineHeight);

        _sparkHost.Visibility = graph ? Visibility.Visible : Visibility.Collapsed;
        _bar.Visibility = bar ? Visibility.Visible : Visibility.Collapsed;
        _stats.Visibility = stats ? Visibility.Visible : Visibility.Collapsed;
        _scale.Visibility = scale ? Visibility.Visible : Visibility.Collapsed;

        // What the rungs left goes to the reading first, up to its ceiling, then to the chart.
        var readingBox = readingFloor + room.Remaining;

        if (graph)
        {
            readingBox = Math.Min(readingBox, FluentType.Display.LineHeight + (captionUnder ? FluentType.Body.LineHeight : 0));
        }

        var used = ApplyReading(readingWidth, readingBox, characters, hasCaption);

        if (graph)
        {
            _sparkHost.Height = SparklineHeight + Math.Max(0, readingFloor + room.Remaining - used);
        }

        // The chart spans as many samples as its width has slots for.
        _window = MeterHistory.Window(_area.Width);
        _span.Text = MeterHistory.Span(_window, _subscribedInterval);
        UpdateStats();
    }

    /// <summary>
    /// Size the reading and place its caption for a box <paramref name="height"/> tall, and return
    /// the height they actually take.
    /// </summary>
    private double ApplyReading(double width, double height, double characters, bool hasCaption)
    {
        // The caption shares the line with the reading, so the reading gets at most half of it.
        // Letting the number take the whole line is how a tall narrow tile ended up with a 68px
        // reading and a caption trimmed to a single ellipsis.
        var hero = Reading(hasCaption ? width / 2 : width, height, characters);

        // [S-646] And when even half the line leaves the caption too little, the caption moves
        // under the reading and the reading has the whole width — rather than "30 G…". It moves
        // there too when that simply draws the reading larger, which is what a tall, narrow tile
        // with height to spare wants.
        var under = Reading(width, height - FluentType.Body.LineHeight, characters);
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

        return hero.LineHeight + (stacked ? caption.LineHeight : 0);
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

    /// <summary>The min / avg / max over the samples the chart is showing.</summary>
    private void UpdateStats()
    {
        var line = _stats.Visibility == Visibility.Visible;
        var tiles = _tiles.Visibility == Visibility.Visible;

        if (!line && !tiles)
        {
            return;
        }

        var values = _values.ToArray();
        var summary = MeterHistory.Summarise(values.AsSpan(Math.Max(0, values.Length - _window)));

        if (line)
        {
            _stats.Text = MeterHistory.SummaryLine(_unit, summary, _compactStats);
        }

        if (tiles && _tilesShowFacts && _facts is { Count: 3 } facts)
        {
            for (var i = 0; i < facts.Count; i++)
            {
                _tileLabels[i].Text = facts[i].Label;
                _tileValues[i].Text = facts[i].Value;
            }
        }
        else if (tiles)
        {
            _tileLabels[0].Text = "Average";
            _tileLabels[1].Text = "Peak";
            _tileLabels[2].Text = "Low";
            _tileValues[0].Text = summary is { } a ? MeterHistory.Format(_unit, a.Average) : "--";
            _tileValues[1].Text = summary is { } b ? MeterHistory.Format(_unit, b.Max) : "--";
            _tileValues[2].Text = summary is { } c ? MeterHistory.Format(_unit, c.Min) : "--";
        }
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
        _graph = _config is GaugeConfig { ShowGraph: true } && _config is not BatteryConfig;

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
                _values.Clear();
                RedrawSparkline();
            }
        }

        var resubscribed = Resubscribe();

        // After Resubscribe, so the scale line names the span at the cadence now in force.
        Lay();

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
        // A desktop with no battery is not a battery meter that failed, so it gets an empty state
        // rather than a dimmed "--". The reading says which it is; matching on the caption text
        // would make a display decision depend on the exact wording of a string in another file.
        var absent = reading.Absent;

        _absent = absent;
        _facts = reading.Facts;

        _primary.Text = reading.Primary;
        _secondary.Text = reading.Secondary ?? string.Empty;
        _secondary.Visibility = string.IsNullOrEmpty(reading.Secondary) ? Visibility.Collapsed : Visibility.Visible;

        // Unavailable is a colour, not an opacity: the disabled text and accent brushes are what
        // every other disabled thing on the desktop uses, and they follow the theme.
        _primary.Foreground = reading.Available ? _readingBrush : _unavailableBrush;

        _bar.Value = (reading.Fraction ?? 0) * 100;
        _bar.Foreground = reading.Available ? _barBrush : _barUnavailableBrush;

        if (reading is { Available: true, Fraction: { } fraction })
        {
            if (_history.Count >= MeterHistory.Capacity)
            {
                _history.Dequeue();
                _values.Dequeue();
            }

            _history.Enqueue(fraction);
            _values.Enqueue(reading.Value ?? fraction);
        }

        // The caption can appear and disappear between samples, which changes how much of the line
        // the reading may take — and the absent state swaps the whole ladder out.
        Lay();
        RedrawSparkline();
    }

    /// <summary>
    /// Redraw the trace. Cheap by construction: two visuals, at most <see cref="MeterHistory.Capacity"/>
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

        // The newest samples, as many as this chart's window spans.
        var all = _history.ToArray();
        var samples = all.AsSpan(Math.Max(0, all.Length - _window));

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
            var x = Sparkline.SampleX(i, samples.Length, _window) * width;
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
