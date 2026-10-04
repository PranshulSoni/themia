using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.App.Widgets.Meters;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// The readout shared by every system meter: the reading, its history, and whatever the kind has
/// to say about it. Where the number comes from, and what extras the kind offers, is the kind's
/// <see cref="MeterPart"/> (<c>Meters/&lt;Kind&gt;Meter.cs</c>); this file is the layout every meter
/// shares, so the six meters grow and shrink the same way.
/// <para>
/// [S-664] [S-1129] Its disclosure ladder, smallest tile first: the reading, with its caption beside
/// it or under it; the chart (or, with the graph off, a bar); the kind's detail lines, two at
/// Regular height and all of them at Expanded; for a meter whose value moves, min / avg / max — a
/// line, or three stat tiles once the tile is Expanded in width — and for one that does not, the
/// reading's own breakdown in the tiles; the usage bar beside the chart, which becomes the
/// composition bar (in use / cached / free) on a tile Expanded in height; the kind's breakdown list
/// (top processes, cores, drives) in whole rows; and a scale line naming the span the chart covers,
/// once the tile is Expanded both ways. Whatever the rungs leave goes to the reading up to the top
/// of the ramp, and past that to the chart.
/// </para>
/// <para>
/// Three arrangements of the same rungs: <b>stacked</b> (one column, the chart full-bleed along the
/// bottom); <b>side</b> on a wide strip (reading and details on the left, the chart filling the
/// right at full height, so a strip shows a real chart instead of a sliver); <b>panels</b> on a huge
/// tile with a list (the meter on the left, the list down the right).
/// </para>
/// </summary>
internal sealed partial class MeterView : Grid, IWidgetView, IWidgetSizeAware, IWidgetSurfaceAware, IMeterHost, IWidgetKeys
{
    private const string Scope = "meter";

    /// <summary>The graph never shrinks below this: under it a trace is a smudge, not a chart.</summary>
    private const double SparklineHeight = Fluent.Space6;

    /// <summary>What the fallback bar costs the reading in height, the bar being a thin Fluent one.</summary>
    private const double BarHeight = Fluent.Space1;

    /// <summary>The composition bar is twice a usage bar: three colours in a 4px bar are not legible.</summary>
    private const double CompositionHeight = Fluent.Space2;

    /// <summary>
    /// From Regular height the chart is promised this share of the tile before any extra is asked
    /// in: the history is the meter's face, and a tile of detail lines over a 24px sliver reads as a
    /// list that happens to have a chart.
    /// </summary>
    private const double ChartShare = 0.22;

    /// <summary>A breakdown row: Windows' compact list row.</summary>
    private const double ListRowHeight = Fluent.RowHeightCompact;

    /// <summary>
    /// [S-1572] The breakdown rows promised right after the chart's floor on a tile Expanded in
    /// height (the original's "top 3 processes" on a tall tile), before the details, stats and the
    /// per-core strip ask. Asked last, the list got the sliver they left: one process under a chart.
    /// </summary>
    private const int ListPromise = 3;

    /// <summary>Characters a composition legend needs ("In use 12.3 GB  Cached 10.1 GB  Free 3.4 GB").</summary>
    private const double LegendCharacters = 44;

    private enum Arrangement
    {
        Stacked,
        Side,
        Panels,
    }

    private readonly MeterPart _part;
    private readonly WidgetKind _kind;
    private readonly MeterUnit _unit;

    private readonly Grid _main = new();
    private readonly Grid _side = new();

    private readonly Grid _reading = new();
    private readonly TextBlock _primary = Fluent.Text(FluentType.Subtitle);
    private readonly FontIcon _badge = new();
    private readonly TextBlock _secondary = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");

    private readonly StackPanel _details = new();
    private readonly List<TextBlock> _detailLines = [];

    private readonly TextBlock _stats = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
    private readonly Grid _tiles = new();
    private readonly TextBlock[] _tileLabels = new TextBlock[3];
    private readonly TextBlock[] _tileValues = new TextBlock[3];

    private readonly Grid _scale = new();
    private readonly TextBlock _span = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly TextBlock _top = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");

    private readonly ProgressBar _bar = new();
    private readonly ProgressBar _usageBar = new();
    private readonly StackPanel _composition = new();
    private readonly Grid _compositionBar = new();
    private readonly Border[] _segments = new Border[3];
    private readonly TextBlock _legend = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");

    private readonly StackPanel _list = new();
    private readonly TextBlock _listTitle = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly List<MeterListRow> _listRows = [];

    private readonly MeterCellStrip _cells = new();

    private readonly SparkBand _band;
    private readonly StackPanel? _absentState;

    // The value in the reading's own unit that min / avg / max describe, as long as the chart's
    // history. The chart keeps its own series, in the terms it plots.
    private readonly Queue<double> _values = new();

    // [S-1191] The second series' values (upload), aligned with _values, so the scale line names the
    // top the chart is actually drawn against when the second series is the busier one.
    private readonly Queue<double> _seconds = new();

    // Looked up once. The reading swaps between them on every sample, and a resource lookup per
    // sample per widget is a dictionary hit and a possible log line for nothing.
    private readonly Brush _readingBrush = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White);
    private readonly Brush _unavailableBrush = FluentLayout.Themed("TextFillColorDisabledBrush", Colors.Gray);
    private readonly Brush _labelBrush = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);
    private readonly Brush _accentBrush = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);
    private readonly Brush _cautionBrush = FluentLayout.Themed("SystemFillColorCautionBrush", Colors.Orange);
    private readonly Brush _criticalBrush = FluentLayout.Themed("SystemFillColorCriticalBrush", Colors.Red);
    private readonly Brush _successBrush = FluentLayout.Themed("SystemFillColorSuccessBrush", Colors.Green);
    private readonly Brush _barUnavailableBrush = FluentLayout.Themed("AccentFillColorDisabledBrush", Colors.Gray);
    private readonly Brush _restBrush = FluentLayout.Themed("ControlStrongFillColorDisabledBrush", Colors.Gray);

    private Ticker? _ticker;
    private WidgetConfig _config;
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;
    private int _capacity = 60;

    // The ramp steps currently applied. Kept so a sample that lands on the same step does not touch
    // the text blocks at all: re-applying a font size is a layout invalidation the tile does not need.
    private TypeStep _heroStep;
    private TypeStep _captionStep;
    private TypeStep _detailStep = FluentType.Caption;

    // The last area the card gave the view, and what the ladder settled on there.
    private WidgetArea _area;
    private Arrangement _arrangement = Arrangement.Stacked;
    private int _window = 2;
    private bool _compactStats;
    private TypeStep _tileStep = FluentType.BodyStrong;
    private bool _tilesShowFacts;
    private MeterDemand _demand = MeterDemand.None;
    private bool _resampleQueued;

    private MeterReading _last = new(null, "--", null, false);
    private Brush _pressureBrush;
    private bool _graph;
    private bool _absent;
    private bool _detached;

    public MeterView(WidgetKind kind)
    {
        // No padding on the view: the graph is a full-bleed band and has to reach the card edge on
        // three sides. Text carries the inset instead, exactly as a list row does — the chart is
        // the widget's surface, not something sitting on it.
        _kind = kind;
        _part = MeterPart.For(kind);
        _unit = _part.Unit;
        _config = WidgetConfig.CreateDefault(kind);
        _pressureBrush = _accentBrush;

        _heroStep = FluentType.Subtitle;
        _captionStep = FluentType.Caption;

        _band = new SparkBand(_capacity, _part.Scale) { MinHeight = SparklineHeight };

        BuildReading();
        BuildScale();
        BuildTiles();
        BuildBars();
        BuildList();

        _details.Margin = new Thickness(Fluent.Space4, Fluent.Space2, Fluent.Space4, 0);
        _stats.Margin = new Thickness(Fluent.Space4, Fluent.Space2, Fluent.Space4, 0);
        _scale.Margin = new Thickness(Fluent.Space4, Fluent.Space2, Fluent.Space4, 0);
        _band.Margin = new Thickness(0, Fluent.Space2, 0, 0);
        _bar.Margin = new Thickness(Fluent.Space4, Fluent.Space2, Fluent.Space4, 0);
        _usageBar.Margin = new Thickness(Fluent.Space4, Fluent.Space2, Fluent.Space4, 0);
        _composition.Margin = new Thickness(Fluent.Space4, Fluent.Space2, Fluent.Space4, 0);

        // One row per rung, top to bottom: the reading takes the star row and is centred in it;
        // each rung below is Auto and carries the gap above itself as a margin. Not RowSpacing: a
        // collapsed row has no height but still has its spacing, which put a gap under a graph that
        // is supposed to reach the bottom edge of the card. The graph and the bar are alternatives,
        // so they share the last row.
        _main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 8; i++)
        {
            _main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        Place(_reading, 0);
        Place(_details, 1);
        Place(_stats, 2);
        Place(_tiles, 3);
        Place(_cells.Host, 4);
        Place(_usageBar, 5);
        Place(_composition, 5);
        Place(_list, 6);
        Place(_scale, 7);
        Place(_band, 8);
        Place(_bar, 8);

        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        Grid.SetColumn(_side, 1);
        Children.Add(_main);
        Children.Add(_side);

        // Only a battery can be absent — every other meter that cannot be read is a meter that could
        // be read a moment ago, which is [S-82]'s dimmed "--" rather than an empty state.
        _absentState = _part.CanBeAbsent ? BuildAbsentState(WidgetCatalog.Definition(kind).Glyph) : null;

        if (_absentState is not null)
        {
            Grid.SetColumnSpan(_absentState, 2);
            Children.Add(_absentState);
        }
    }

    private void Place(FrameworkElement element, int row)
    {
        Grid.SetRow(element, row);
        _main.Children.Add(element);
    }

    private void BuildReading()
    {
        _primary.Text = "--";
        _primary.Foreground = _readingBrush;
        _primary.VerticalAlignment = VerticalAlignment.Bottom;

        // Lifted off the baseline of the reading it sits beside: bottom-aligning a 12px caption
        // against a 68px number puts it below the number's baseline, which reads as a slipped line.
        _secondary.VerticalAlignment = VerticalAlignment.Bottom;
        _secondary.Margin = new Thickness(0, 0, 0, Fluent.Space1);

        // A Grid, not a StackPanel: a horizontal StackPanel measures its children with unbounded
        // width, so the caption never learns how much room it has and TextTrimming never fires. The
        // star column hands it exactly what is left over, so it ellipsises instead.
        _reading.ColumnSpacing = Fluent.Space2;
        _reading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _reading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // A second row for the caption when the tile is too narrow to keep it beside the reading
        // ([S-646]); empty, and so of no height, otherwise.
        _reading.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _reading.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // [S-1187] The kind's badge (the battery icon) leads the reading, in the same cell, so the
        // caption logic never has to know it is there; its width is in the kind's ReadingCharacters.
        _badge.Visibility = Visibility.Collapsed;
        _badge.VerticalAlignment = VerticalAlignment.Center;
        var lead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Fluent.Space2, VerticalAlignment = VerticalAlignment.Bottom };
        lead.Children.Add(_badge);
        lead.Children.Add(_primary);

        Grid.SetColumn(lead, 0);
        Grid.SetColumn(_secondary, 1);
        _reading.Children.Add(lead);
        _reading.Children.Add(_secondary);
        _reading.VerticalAlignment = VerticalAlignment.Center;
        _reading.Margin = WidgetLayout.SideInset;
    }

    /// <summary>
    /// The scale line: the span on the left, the top of the scale on the right — what Task Manager
    /// prints round its charts, so a large chart says what it is a chart of.
    /// </summary>
    private void BuildScale()
    {
        _scale.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _scale.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _top.HorizontalAlignment = HorizontalAlignment.Right;
        _top.Text = "100%";
        Grid.SetColumn(_top, 1);
        _scale.Children.Add(_span);
        _scale.Children.Add(_top);
    }

    private void BuildBars()
    {
        foreach (var bar in new[] { _bar, _usageBar })
        {
            bar.Minimum = 0;
            bar.Maximum = 100;
            bar.HorizontalAlignment = HorizontalAlignment.Stretch;
            bar.VerticalAlignment = VerticalAlignment.Bottom;
            bar.Foreground = _accentBrush;
        }

        // The composition bar: three parts of one whole, weighted by star columns, so the widths are
        // exact to the pixel with no arithmetic here.
        _compositionBar.Height = CompositionHeight;
        _compositionBar.CornerRadius = new CornerRadius(Fluent.ControlRadius / 2);
        for (var i = 0; i < _segments.Length; i++)
        {
            _compositionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _segments[i] = new Border();
            Grid.SetColumn(_segments[i], i);
            _compositionBar.Children.Add(_segments[i]);
        }

        _legend.Margin = new Thickness(0, Fluent.Space1, 0, 0);
        _composition.Children.Add(_compositionBar);
        _composition.Children.Add(_legend);
    }

    private void BuildList()
    {
        // The rows carry the keyboard ring inside their edge ([S-1567]): the heading steps in with them.
        _listTitle.Margin = new Thickness(Fluent.Space4 + RowFocus.Ring, 0, Fluent.Space4 + RowFocus.Ring, 0);
        _list.Children.Add(_listTitle);
        _list.Margin = new Thickness(0, Fluent.Space2, 0, 0);
    }

    /// <summary>
    /// The state a machine with no battery shows: the widget's own icon and one line, centred, the
    /// shape Windows uses when a pane has nothing to show.
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
        _tiles.VerticalAlignment = VerticalAlignment.Center;
        _tiles.Visibility = Visibility.Collapsed;

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
        var characters = _tilesShowFacts && _last.Facts is { } facts
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
    }

    /// <summary>
    /// Move the chart and the list to where this arrangement puts them. Only on a change: moving an
    /// element between panels re-templates it.
    /// </summary>
    private void Arrange(Arrangement arrangement)
    {
        if (arrangement == _arrangement)
        {
            return;
        }

        _arrangement = arrangement;

        Move(_band, arrangement == Arrangement.Side ? _side : _main, 8);
        Move(_list, arrangement == Arrangement.Panels ? _side : _main, 6);

        // On a strip the left column is centred as a group, rather than the reading floating in a
        // star row with its details pinned under it.
        _main.RowDefinitions[0].Height = arrangement == Arrangement.Side ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        _main.VerticalAlignment = arrangement == Arrangement.Side ? VerticalAlignment.Center : VerticalAlignment.Stretch;

        // Beside the text the chart runs the full height of the tile; in the stacked column it
        // carries the rung gap above itself.
        _band.Margin = arrangement == Arrangement.Side ? new Thickness(0) : new Thickness(0, Fluent.Space2, 0, 0);
        _band.VerticalAlignment = arrangement == Arrangement.Side ? VerticalAlignment.Stretch : VerticalAlignment.Bottom;
        _list.Margin = arrangement == Arrangement.Panels ? new Thickness(0) : new Thickness(0, Fluent.Space2, 0, 0);
        _list.VerticalAlignment = VerticalAlignment.Top;
    }

    /// <summary>Re-parent a rung into the main column (at its row) or the side column (alone).</summary>
    private void Move(FrameworkElement element, Grid target, int row)
    {
        if (element.Parent is Grid current && !ReferenceEquals(current, target))
        {
            current.Children.Remove(element);
            target.Children.Add(element);
        }

        Grid.SetRow(element, ReferenceEquals(target, _main) ? row : 0);
    }

    /// <summary>
    /// Climb the ladder for the current area: decide which rungs are up, then size the reading and
    /// the chart from what they leave.
    /// <para>
    /// The reading is the only thing that grows in type: the caption stays where Fluent puts it,
    /// because a meter whose label is as big as its value reads as two headlines. It grows by ramp
    /// step, Subtitle → Title → TitleLarge → Display, and with a chart below it stops there — past
    /// that the room is worth more to the chart. With no chart the reading fills the tile.
    /// </para>
    /// </summary>
    private void Lay()
    {
        var absent = _absent;

        if (_absentState is not null)
        {
            _absentState.Visibility = absent ? Visibility.Visible : Visibility.Collapsed;
        }

        _main.Visibility = absent ? Visibility.Collapsed : Visibility.Visible;
        _side.Visibility = absent ? Visibility.Collapsed : Visibility.Visible;

        if (absent)
        {
            SetDemand(MeterDemand.None);
            return;
        }

        var reading = _last;
        var graph = _graph;
        var listWanted = _part.ListWanted;
        var characters = _part.ReadingCharacters;

        var arrangement = !_area.IsMeasured ? Arrangement.Stacked
            : graph && _area.Shape == AreaShape.Wide && _area.WidthClass >= SizeClass.Expanded ? Arrangement.Side
            // [S-1166] Only a list that fills at least half the side panel earns one: three drives
            // down the right of an XL tile left most of it empty, and read better under the chart.
            : listWanted > 0 && _area.WidthClass >= SizeClass.Huge && _area.HeightClass >= SizeClass.Expanded
                && (listWanted * ListRowHeight) + FluentType.Caption.LineHeight >= _area.Height / 2 ? Arrangement.Panels
            : Arrangement.Stacked;

        Arrange(arrangement);

        // The left column's outer width: the whole tile when stacked; on a strip enough for the
        // reading at Title and a short caption; half the tile beside a list.
        var mainOuter = arrangement switch
        {
            Arrangement.Side => Math.Round(Math.Clamp(
                Math.Max(_area.Width * 0.42, FluentType.EstimateWidth(FluentType.Title, characters) + (Fluent.Space4 * 2)),
                0,
                _area.Width * 0.6)),
            Arrangement.Panels => Math.Round(_area.Width * 0.5),
            _ => _area.Width,
        };

        ColumnDefinitions[0].Width = arrangement == Arrangement.Stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(Math.Max(0, mainOuter));
        ColumnDefinitions[1].Width = arrangement == Arrangement.Stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);

        // The text is inset even though the view is not, so the width it has is the column less
        // both insets. The height is the whole slot: the graph runs to the bottom edge.
        var width = Math.Max(0, mainOuter - (Fluent.Space4 * 2));
        var room = new Disclosure(width, _area.Height, Fluent.Space2);

        var hasCaption = !string.IsNullOrEmpty(_secondary.Text);

        // Rung 1, always: the reading at its smallest, with its caption under it if even then the
        // line cannot hold both.
        var captionUnder = hasCaption && !ReadoutLayout.CaptionFitsBeside(
            width, FluentType.Subtitle, characters, FluentType.Caption, CaptionCharacters(), Fluent.Space2);
        // On a tile Expanded in height the reading is promised a step more before the extras are
        // asked in, so a list that takes every row it can never leaves the number at its smallest.
        var heroFloor = _area.HeightClass >= SizeClass.Expanded && _area.Least >= SizeClass.Regular ? FluentType.Title : FluentType.Subtitle;
        var readingFloor = heroFloor.LineHeight + (captionUnder ? FluentType.Caption.LineHeight : 0);
        room.Reserve(readingFloor);

        // Rung 2: the chart at its floor in this column (on a strip it has a column of its own), or
        // the bar when the graph is off.
        var chartHere = graph && arrangement != Arrangement.Side;
        var chartFloor = chartHere && _area.HeightClass >= SizeClass.Regular
            ? Math.Max(SparklineHeight, Math.Round(_area.Height * ChartShare))
            : SparklineHeight;
        var chart = chartHere && room.Take(chartFloor);
        var bar = !graph && room.Take(BarHeight);
        var chartShown = chart || (graph && arrangement == Arrangement.Side);

        // Rung 2b [S-1572]: the list's first rows, promised now so the chart and the list both
        // have room; the list grows into what the later rungs leave at rung 6.
        var listHeader = FluentType.Caption.LineHeight;
        var listPromised = 0;

        if (arrangement == Arrangement.Stacked && listWanted > 0 && _area.HeightClass >= SizeClass.Expanded)
        {
            var promise = Math.Min(Math.Min(listWanted, ListPromise),
                Disclosure.WholeRows(room.Remaining - listHeader - (2 * Fluent.Space2), ListRowHeight));

            if (promise > 0 && room.Take(listHeader))
            {
                listPromised = room.TakeRows(ListRowHeight, promise);
            }
        }

        // Rung 3: the kind's detail lines, first first. Two until the tile is Expanded in height,
        // then all of them; on a strip the left column has the whole height, so they start there.
        _detailStep = _area.Least >= SizeClass.Expanded ? FluentType.Body : FluentType.Caption;
        var detailsAllowed = arrangement == Arrangement.Side || _area.HeightClass >= SizeClass.Regular;
        var detailCap = _area.HeightClass >= SizeClass.Expanded ? int.MaxValue : 2;
        var detailCount = detailsAllowed ? Math.Min(detailCap, reading.Details?.Count ?? 0) : 0;
        var details = detailCount > 0 ? room.TakeRows(_detailStep.LineHeight, detailCount) : 0;

        // Rung 4: min / avg / max. On a wide tile as stat tiles — beside the reading on a strip with
        // no chart, where they cost no height, or under it on a panel — otherwise as one line, in
        // the long form when the line holds it. A still meter's tiles carry its own breakdown.
        var summarise = chartShown && _part.Moves;
        _tilesShowFacts = !summarise && reading.Facts is { Count: 3 };
        var tileable = summarise || _tilesShowFacts;
        var wideStats = tileable && arrangement != Arrangement.Side && WidgetArea.Classify(mainOuter, WidgetArea.WidthBreakpoints) >= SizeClass.Expanded;
        _tileStep = _area.Least >= SizeClass.Huge ? FluentType.Subtitle : FluentType.BodyStrong;

        var tilesWidth = TilesWidth(_tileStep);
        var tileHeight = FluentType.Caption.LineHeight + _tileStep.LineHeight;
        var tilesBeside = wideStats && arrangement == Arrangement.Stacked && _area.Shape == AreaShape.Wide
            && width >= tilesWidth + Fluent.Space6 + FluentType.EstimateWidth(FluentType.Subtitle, characters);
        var tilesBelow = wideStats && !tilesBeside && _area.HeightClass >= SizeClass.Regular && room.Take(tileHeight, tilesWidth);

        // A tall column has the height and not the width: the same three tiles, one under another —
        // unless a list wants that height, which says more.
        var tilesStacked = tileable && !wideStats && listWanted == 0
            && arrangement == Arrangement.Stacked && _area.Shape == AreaShape.Tall && _area.HeightClass >= SizeClass.Expanded
            && room.Take((3 * tileHeight) + (2 * Fluent.Space2), (tilesWidth - (2 * Fluent.Space5)) / 3);

        _compactStats = width < FluentType.EstimateWidth(FluentType.Caption, MeterHistory.SummaryCharacters(_unit, compact: false));
        var stats = summarise && !tilesBelow && !tilesBeside && !tilesStacked
            && (_area.HeightClass >= SizeClass.Regular || arrangement == Arrangement.Side)
            && room.Take(FluentType.Caption.LineHeight, FluentType.EstimateWidth(FluentType.Caption, MeterHistory.SummaryCharacters(_unit, compact: true)));

        // Rung 4b [S-1150][S-1622]: the kind's per-part strip (a bar per core), in balanced rows
        // across the column; taller bars, and each one's figure under it, on a tile Expanded in
        // height. When that full strip does not fit, it is not dropped for the list: beside the stat
        // tiles a short strip costs no height at all, and failing that the shortest form that fits
        // goes under the stats (short bars, then short bars without their caption).
        var cellsWanted = _part.CellsWanted;
        var cellsAllowed = cellsWanted > 0 && (_area.HeightClass >= SizeClass.Regular || arrangement == Arrangement.Side);
        var tallCells = _area.HeightClass >= SizeClass.Expanded;
        var cellLabelWidth = FluentType.EstimateWidth(FluentType.Caption, MeterCellStrip.LabelCharacters);
        var cellForms = cellsAllowed ? MeterCellForms.Ladder(cellsWanted, width, tallCells, cellLabelWidth) : [];
        var besideWidth = width - tilesWidth - Fluent.Space6;
        var besideForm = cellForms.Count > 0 && tilesBelow && besideWidth > 0
            ? MeterCellForms.Ladder(cellsWanted, besideWidth, tallCells, cellLabelWidth)
                .FirstOrDefault(f => f.Height <= tileHeight + Disclosure.Tolerance)
            : default;
        var cellForm = default(CellStripForm);
        var cellsBeside = false;

        if (cellForms.Count > 0)
        {
            if (besideForm.IsEmpty || room.Fits(cellForms[0].Height))
            {
                var pick = room.TakeFirst([.. cellForms.Select(f => f.Height)]);
                cellForm = pick >= 0 ? cellForms[pick] : default;
            }
            else
            {
                cellForm = besideForm;
                cellsBeside = true;
            }
        }

        var cells = !cellForm.IsEmpty;

        // Rung 5: the usage bar beside a chart (without one the bar is rung 2 already), which on a
        // tile Expanded in height becomes the composition bar with its legend.
        var wantSegments = _part.UsageBar && chartShown && arrangement != Arrangement.Side
            && _area.HeightClass >= SizeClass.Expanded && width >= FluentType.EstimateWidth(FluentType.Caption, LegendCharacters);
        var composition = wantSegments && reading.Segments is { Count: 3 }
            && room.Take(CompositionHeight + Fluent.Space1 + FluentType.Caption.LineHeight);
        var usageBar = !composition && _part.UsageBar && chartShown && detailsAllowed && room.Take(BarHeight);

        // Rung 6: the breakdown list, in whole rows — down the right on panels, else under the rest.
        var listRows = 0;

        if (arrangement == Arrangement.Panels)
        {
            listRows = Math.Min(listWanted, Disclosure.WholeRows(_area.Height - listHeader, ListRowHeight));
        }
        else if (listPromised > 0)
        {
            listRows = listPromised + room.TakeRows(ListRowHeight, listWanted - listPromised);
        }
        else if (listWanted > 0 && _area.HeightClass >= SizeClass.Expanded
            && Disclosure.WholeRows(room.Remaining - listHeader - (2 * Fluent.Space2), ListRowHeight) >= 1
            && room.Take(listHeader))
        {
            listRows = room.TakeRows(ListRowHeight, listWanted);
        }

        // Rung 7: the scale line, on a tile large both ways.
        var scale = chart && _area.AtLeast(SizeClass.Expanded, SizeClass.Expanded) && room.Take(FluentType.Caption.LineHeight);

        _band.Visibility = chartShown ? Visibility.Visible : Visibility.Collapsed;
        _bar.Visibility = bar ? Visibility.Visible : Visibility.Collapsed;
        _details.Visibility = details > 0 ? Visibility.Visible : Visibility.Collapsed;
        _stats.Visibility = stats ? Visibility.Visible : Visibility.Collapsed;
        _scale.Visibility = scale ? Visibility.Visible : Visibility.Collapsed;
        _composition.Visibility = composition ? Visibility.Visible : Visibility.Collapsed;
        _usageBar.Visibility = usageBar ? Visibility.Visible : Visibility.Collapsed;
        _list.Visibility = listRows > 0 ? Visibility.Visible : Visibility.Collapsed;
        _cells.Host.Visibility = cells ? Visibility.Visible : Visibility.Collapsed;

        if (cells)
        {
            _cells.Arrange(cellForm, cellsWanted);
            _cells.Show(reading.Cells, Pressure);
        }

        // Beside the tiles the strip shares their row, starting where they end.
        Grid.SetRow(_cells.Host, cellsBeside ? 3 : 4);
        _cells.Host.Margin = cellsBeside
            ? new Thickness(Fluent.Space4 + tilesWidth + Fluent.Space6, Fluent.Space2, Fluent.Space4, 0)
            : new Thickness(Fluent.Space4, Fluent.Space2, Fluent.Space4, 0);

        _tiles.Visibility = tilesBeside || tilesBelow || tilesStacked ? Visibility.Visible : Visibility.Collapsed;
        ArrangeTiles(tilesStacked);
        Grid.SetRow(_tiles, tilesBeside ? 0 : 3);
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

        // What the rungs left goes to the reading first, up to its ceiling, then to the chart.
        var readingBox = readingFloor + room.Remaining;

        if (chartShown)
        {
            readingBox = Math.Min(readingBox, FluentType.Display.LineHeight + (captionUnder ? FluentType.Body.LineHeight : 0));
        }

        var used = ApplyReading(readingWidth, readingBox, characters, hasCaption);

        _band.Height = chart
            ? chartFloor + Math.Max(0, readingFloor + room.Remaining - used)
            : double.NaN;

        // The chart spans the history the span setting asks for, as much of it as its width has
        // slots for.
        var chartWidth = arrangement == Arrangement.Side ? _area.Width - mainOuter : mainOuter;
        _window = MeterTiming.Window(_capacity, chartWidth);
        _span.Text = MeterHistory.Span(_window, _subscribedInterval);

        ShowDetails(reading, details);
        ShowComposition(reading, composition);
        ShowList(reading, listRows, arrangement == Arrangement.Panels);
        UpdateStats();

        SetDemand(new MeterDemand(listRows, detailsAllowed, wantSegments, cells));
    }

    /// <summary>
    /// Remember what the layout has room for, and when it now wants something the last reading did
    /// not carry (the list just got room, a tile grew into its details), ask for a fresh one at once
    /// rather than leaving the space empty for a whole interval. Queued, not called: this runs inside
    /// a layout pass that a reading started.
    /// </summary>
    private void SetDemand(MeterDemand demand)
    {
        var grew = demand.ListRows > _demand.ListRows
            || (demand.Details && !_demand.Details)
            || (demand.Segments && !_demand.Segments)
            || (demand.Cells && !_demand.Cells);

        _demand = demand;

        if (!grew || _resampleQueued || _detached || _subscription is null)
        {
            return;
        }

        _resampleQueued = DispatcherQueue.TryEnqueue(() =>
        {
            _resampleQueued = false;

            if (!_detached)
            {
                Sample();
            }
        });
    }

    private void ShowDetails(MeterReading reading, int count)
    {
        if (count == 0 || reading.Details is not { } lines)
        {
            return;
        }

        while (_detailLines.Count < count)
        {
            var line = Fluent.Text(_detailStep, "TextFillColorSecondaryBrush");
            line.MaxLines = 1;
            _detailLines.Add(line);
            _details.Children.Add(line);
        }

        for (var i = 0; i < _detailLines.Count; i++)
        {
            var line = _detailLines[i];
            line.Visibility = i < count ? Visibility.Visible : Visibility.Collapsed;

            if (i >= count)
            {
                continue;
            }

            line.Apply(_detailStep);

            // The label quieter than its figure, the way a Settings row's description sits under
            // its title: one line, two weights of grey.
            line.Inlines.Clear();
            var fact = lines[i];

            if (fact.Label.Length > 0)
            {
                line.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = fact.Label + "  ", Foreground = _labelBrush });
            }

            if (fact.Open is { } open)
            {
                // [S-1613] A line that is a link (the Wi-Fi name's "Allow location" hint).
                var link = new Microsoft.UI.Xaml.Documents.Hyperlink();
                link.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = fact.Value });
                link.Click += (_, _) => open();
                line.Inlines.Add(link);
                ToolTipService.SetToolTip(line, fact.Value);
            }
            else
            {
                line.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = fact.Value });

                if (ToolTipService.GetToolTip(line) is not null)
                {
                    ToolTipService.SetToolTip(line, null);
                }
            }
        }
    }

    private void ShowComposition(MeterReading reading, bool shown)
    {
        if (!shown || reading.Segments is not { Count: 3 } segments)
        {
            return;
        }

        _legend.Inlines.Clear();

        for (var i = 0; i < 3; i++)
        {
            var segment = segments[i];
            _compositionBar.ColumnDefinitions[i].Width = new GridLength(Math.Max(0, segment.Fraction), GridUnitType.Star);
            _segments[i].Background = segment.Tone == MeterTone.Rest ? _restBrush : _pressureBrush;

            // Cached is the in-use colour, quieter — the same part of the machine, not a third hue.
            _segments[i].Opacity = segment.Tone == MeterTone.Secondary ? 0.45 : 1;

            _legend.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = segment.Label + " " });
            _legend.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = segment.Value + (i < 2 ? "   " : string.Empty), Foreground = _readingBrush });
        }
    }

    private void ShowList(MeterReading reading, int count, bool bars)
    {
        if (count == 0)
        {
            ShowListCursor([], 0);
            return;
        }

        _listTitle.Text = reading.List?.Title ?? string.Empty;
        var rows = reading.List?.Rows ?? [];

        while (_listRows.Count < count)
        {
            var row = new MeterListRow();
            RowFocus.Prepare(row.Host);
            _listRows.Add(row);
            _list.Children.Add(row.Host);
        }

        for (var i = 0; i < _listRows.Count; i++)
        {
            var row = _listRows[i];
            var shown = i < count && i < rows.Count;
            row.Host.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

            if (shown)
            {
                row.Show(rows[i], bars, _pressureBrush);
            }
        }

        ShowListCursor(rows, count);
    }

    /// <summary>
    /// Size the reading and place its caption for a box <paramref name="height"/> tall, and return
    /// the height they actually take.
    /// </summary>
    private double ApplyReading(double width, double height, double characters, bool hasCaption)
    {
        // The caption shares the line with the reading, so the reading gets at most half of it.
        var hero = Reading(hasCaption ? width / 2 : width, height, characters);

        // [S-646] And when even half the line leaves the caption too little, the caption moves
        // under the reading and the reading has the whole width — rather than "30 G…". It moves
        // there too when that simply draws the reading larger.
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

            // An icon's ink fills less of its em box than a figure's, so it is drawn a little larger
            // than the type to stand as tall as the digits.
            _badge.FontSize = Math.Round(hero.FontSize * 1.1);
        }

        if (caption != _captionStep)
        {
            _captionStep = caption;
            _secondary.Apply(caption);
        }

        return hero.LineHeight + (stacked ? caption.LineHeight : 0);
    }

    /// <summary>The reading's step for a box. Never below Subtitle: the reading is the point of the tile.</summary>
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

    private double CaptionCharacters() => _part.CaptionCharacters(_secondary.Text);

    /// <summary>The min / avg / max over the samples the chart is showing, and the top of its scale.</summary>
    private void UpdateStats()
    {
        var line = _stats.Visibility == Visibility.Visible;
        var tiles = _tiles.Visibility == Visibility.Visible;
        var scale = _scale.Visibility == Visibility.Visible;

        if (!line && !tiles && !scale)
        {
            return;
        }

        var values = _values.ToArray();
        var visible = values.AsSpan(Math.Max(0, values.Length - _window));
        var summary = MeterHistory.Summarise(visible);

        if (scale)
        {
            // A share's chart tops out at 100%; a rate's at the window's peak, which is worth saying.
            _top.Text = _part.Scale.FollowsWindow
                ? MeterHistory.Format(_unit, _part.Scale.Ceiling(visible, _seconds.ToArray().AsSpan(Math.Max(0, _seconds.Count - _window))))
                : "100%";
        }

        if (line)
        {
            _stats.Text = MeterHistory.SummaryLine(_unit, summary, _compactStats);
        }

        if (tiles && _tilesShowFacts && _last.Facts is { Count: 3 } facts)
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

        var gauge = _config as GaugeConfig;
        _graph = gauge is { ShowGraph: true };

        // Before Resubscribe, which fires a tick of its own: a part that changed what it measures
        // (a ping host) must be pointed at the new thing before that tick reaches it.
        var sampleNow = _part.ApplyConfig(_config, this);

        _capacity = gauge?.GraphCapacity ?? 60;
        _band.Capacity = _capacity;
        TrimValues();

        var resubscribed = Resubscribe();

        // After Resubscribe, so the scale line names the span at the cadence now in force.
        Lay();

        // Only a change the part asked to see at once, at an unchanged cadence, still needs a push.
        if (sampleNow && !resubscribed)
        {
            Sample();
        }
    }

    /// <summary>True when the subscription was rebuilt, which also fires an immediate tick.</summary>
    private bool Resubscribe()
    {
        var interval = (_config as GaugeConfig)?.TickInterval ?? TimeSpan.FromSeconds(1);

        // Only when the cadence actually changed. Dropping and remaking the subscription on every
        // config touch made dragging a settings slider stutter.
        if (_subscription is not null && interval == _subscribedInterval)
        {
            return false;
        }

        // A new cadence makes the samples already held a different spacing from the ones to come,
        // which a fixed-slot x-axis would draw as a kink in time. Start the history again.
        if (_subscription is not null)
        {
            ClearHistory();
        }

        _subscription?.Dispose();
        _subscribedInterval = interval;
        _subscription = _ticker?.Subscribe(interval, Sample);
        return _subscription is not null;
    }

    private void Sample()
    {
        try
        {
            _part.Sample(_demand, this);
        }
        catch (Exception ex)
        {
            // A meter that cannot read its counter shows that it cannot, rather than taking the
            // whole surface down from inside a tick.
            Log.Error(Scope, $"could not sample {_kind}", ex);
            Present(MeterReading.Unavailable("error"));
        }
    }

    /// <summary>Put one reading on screen, wherever it came from.</summary>
    public void Present(MeterReading reading)
    {
        if (_detached)
        {
            return;
        }

        _last = reading;

        // A desktop with no battery is not a battery meter that failed, so it gets an empty state
        // rather than a dimmed "--".
        _absent = reading.Absent;

        _primary.Text = reading.Primary;
        _secondary.Text = reading.Secondary ?? string.Empty;
        _secondary.Visibility = string.IsNullOrEmpty(reading.Secondary) ? Visibility.Collapsed : Visibility.Visible;

        // Unavailable is a colour, not an opacity. Pressure colours the chart and the bars, never
        // the number, which stays legible whatever the load.
        _primary.Foreground = reading.Available ? _readingBrush : _unavailableBrush;
        _pressureBrush = Pressure(_part.PressureOf(reading));
        _band.SetBrush(_pressureBrush);

        var badge = _part.Badge(reading);
        _badge.Glyph = badge ?? string.Empty;
        _badge.Foreground = _pressureBrush;
        _badge.Visibility = badge is null ? Visibility.Collapsed : Visibility.Visible;

        _bar.Value = (reading.Fraction ?? 0) * 100;
        _bar.Foreground = reading.Available ? _pressureBrush : _barUnavailableBrush;
        _usageBar.Value = _bar.Value;
        _usageBar.Foreground = _bar.Foreground;

        // [S-1172] A reading with a fraction is a sample even when it is not available: a ping that
        // went unanswered (dimmed "--") still plots, on the baseline. Every other unavailable
        // reading carries no fraction, so it is not one.
        if (reading is { Fraction: { } fraction })
        {
            // A window-scaled chart plots the value itself; a share chart plots the fraction.
            _band.Push(_part.Scale.FollowsWindow ? reading.Value ?? fraction : fraction, reading.Second);
            _values.Enqueue(reading.Value ?? fraction);
            _seconds.Enqueue(reading.Second ?? double.NaN);
            TrimValues();
        }

        // The caption can appear and disappear between samples, which changes how much of the line
        // the reading may take — and the absent state swaps the whole ladder out.
        Lay();
    }

    public void ClearHistory()
    {
        _band.Clear();
        _values.Clear();
        _seconds.Clear();
    }

    private void TrimValues()
    {
        while (_values.Count > _capacity)
        {
            _values.Dequeue();
        }

        while (_seconds.Count > _capacity)
        {
            _seconds.Dequeue();
        }
    }

    /// <summary>[S-1186] A preview's meter part may read its debug fixture instead of the machine.</summary>
    public void Bind(IWidgetSurface surface)
    {
        _part.Bind(surface.IsPreview);

        // [S-1167] And keep the surface for a kind's header title (MeterView.Part.cs).
        BindTitle(surface);
    }

    private Brush Pressure(MeterPressure pressure) => pressure switch
    {
        MeterPressure.Critical => _criticalBrush,
        MeterPressure.Elevated => _cautionBrush,
        MeterPressure.Healthy => _successBrush,
        _ => _accentBrush,
    };

    public void Detach()
    {
        _detached = true;
        _subscription?.Dispose();
        _subscription = null;
        _part.Detach();
    }
}
