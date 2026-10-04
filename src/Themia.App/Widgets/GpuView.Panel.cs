using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.App.Widgets.Meters;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

internal sealed partial class GpuView
{
    /// <summary>
    /// One adapter: its reading, its lines, its engines and processes, and its history. Built once and
    /// rewritten in place on every sample, so the tile does not flicker as it updates; laid out again
    /// whenever its room or what it has to show changes.
    /// </summary>
    private sealed class Panel
    {
        /// <summary>The least history worth drawing under a reading: below it the trace is a smudge.</summary>
        private const double MinChart = 24;

        /// <summary>On a tile large both ways the history is a chart, not a strip.</summary>
        private const double MinPanelChart = 72;

        /// <summary>A list is shown with at least this many rows, or not at all: one row of five engines is a fragment.</summary>
        private const int MinListRows = 2;

        private const double ListRowHeight = Fluent.RowHeightCompact;

        private static readonly Brush ReadingBrush = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White);
        private static readonly Brush UnavailableBrush = FluentLayout.Themed("TextFillColorDisabledBrush", Colors.Gray);
        private static readonly Brush AccentBrush = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);
        private static readonly Brush CautionBrush = FluentLayout.Themed("SystemFillColorCautionBrush", Colors.Orange);
        private static readonly Brush CriticalBrush = FluentLayout.Themed("SystemFillColorCriticalBrush", Colors.Red);

        private readonly int _index;
        private readonly Grid _root = new();
        private readonly StackPanel _info = new();
        private readonly Grid _reading = new() { ColumnSpacing = Fluent.Space2 };
        private readonly TextBlock _name = Fluent.Text(FluentType.BodyStrong, "TextFillColorSecondaryBrush");
        private readonly TextBlock _hero = Fluent.Text(FluentType.Subtitle);
        private readonly TextBlock _caption = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
        private readonly StackPanel _lines = new() { Spacing = Fluent.Space1 };
        private readonly Detail _dedicated = new("Dedicated", withBar: true);
        private readonly Detail _shared = new("Shared", withBar: true);
        private readonly Detail _temperature = new("Temperature");
        private readonly Detail _power = new("Power");
        private readonly Detail _clocks = new("Clocks");
        private readonly Detail _fan = new("Fan");
        private readonly Detail[] _all;
        private readonly StackPanel _lists = new();
        private readonly ListBlock _engines = new("Engines");
        private readonly ListBlock _processes = new("Top processes");
        private readonly Grid _scale = new();
        private readonly TextBlock _span = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
        private readonly TextBlock _top = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
        private readonly SparkBand _spark;

        private GpuConfig _config = new();
        private GpuReading? _last;
        private bool _stacked;
        private TypeStep _heroStep = FluentType.Subtitle;
        private TypeStep _lineStep = FluentType.Caption;

        public Panel(int index)
        {
            _index = index;
            _all = [_dedicated, _shared, _temperature, _power, _clocks, _fan];
            _spark = new SparkBand(_config.GraphCapacity);

            _hero.Text = "--";
            _hero.Foreground = UnavailableBrush;
            Typography.SetNumeralAlignment(_hero, FontNumeralAlignment.Tabular);
            _hero.TextTrimming = TextTrimming.None;

            _name.Text = $"GPU {index}";
            _name.VerticalAlignment = VerticalAlignment.Center;
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _name.MaxLines = 1;
            _caption.VerticalAlignment = VerticalAlignment.Bottom;
            _caption.Margin = new Thickness(0, 0, 0, 6);
            Typography.SetNumeralAlignment(_caption, FontNumeralAlignment.Tabular);

            // The reading row: [name] [hero] [caption]. Which columns are used depends on whether the
            // panel is one of several (the name leads) and whether the caption fits beside.
            _reading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _reading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _reading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _reading.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _reading.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _reading.Margin = WidgetLayout.SideInset;
            _reading.Children.Add(_name);
            _reading.Children.Add(_hero);
            _reading.Children.Add(_caption);

            foreach (var detail in _all)
            {
                _lines.Children.Add(detail.Root);
            }

            _lines.Margin = new Thickness(WidgetLayout.ContentInset, Fluent.Space2, WidgetLayout.ContentInset, 0);

            _lists.Children.Add(_engines.Root);
            _lists.Children.Add(_processes.Root);

            _scale.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _scale.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_top, 1);
            _scale.Children.Add(_span);
            _scale.Children.Add(_top);
            _scale.Margin = new Thickness(WidgetLayout.ContentInset, Fluent.Space2, WidgetLayout.ContentInset, 0);
            _top.Text = "100%";

            _info.Children.Add(_reading);
            _info.Children.Add(_lines);
            _info.Children.Add(_scale);

            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });

            _root.Children.Add(_info);
            _root.Children.Add(_spark);
            _root.Children.Add(_lists);
        }

        public FrameworkElement Root => _root;

        public void Configure(GpuConfig config, bool stacked)
        {
            _config = config;
            _stacked = stacked;

            // [S-1200] The chart holds the configured span at the configured refresh.
            _spark.Capacity = config.GraphCapacity;
            _name.Visibility = stacked ? Visibility.Visible : Visibility.Collapsed;
        }

        public void Show(GpuReading? reading, bool addSample)
        {
            _last = reading;

            if (reading is null)
            {
                // [S-433] An adapter that has gone — undocked, or the selection names one this
                // machine does not have. Said in the panel, not by blanking the tile.
                _name.Text = $"GPU {_index}";
                _hero.Text = "--";
                _hero.Foreground = UnavailableBrush;
                _caption.Text = string.Empty;

                foreach (var detail in _all)
                {
                    detail.Hide();
                }

                _engines.Fill([], AccentBrush);
                _processes.Fill([], AccentBrush);
                return;
            }

            _name.Text = GpuRules.ShortName(reading.Name);
            ToolTipService.SetToolTip(_root, reading.Name);

            if (reading.HasLoad)
            {
                var fraction = reading.Utilization / 100;
                _hero.Text = Formatting.Percent(fraction);
                _hero.Foreground = ReadingBrush;

                // [S-432] Pressure colours the trace, not the number: the number stays legible in
                // every state, and a trace that turns amber is seen from across the room.
                _spark.SetBrush(Pressure(GpuRules.LoadPressure(reading.Utilization)));

                if (addSample)
                {
                    _spark.Push(fraction);
                }
            }

            // [S-437] The memory lines only when they say something true.
            if (_config.ShowDedicated && GpuRules.ShowsDedicated(reading.DedicatedUsed, reading.DedicatedTotal))
            {
                var total = reading.DedicatedTotal;
                var share = GpuRules.Fraction(reading.DedicatedUsed, total);
                _dedicated.Show(
                    total > 0 ? $"{Formatting.Bytes(reading.DedicatedUsed)} / {Formatting.Bytes(total)}" : Formatting.Bytes(reading.DedicatedUsed),
                    total > 0 ? share : null,
                    Pressure(GpuRules.MemoryPressure(share)));
            }
            else
            {
                _dedicated.Hide();
            }

            if (_config.ShowShared && GpuRules.ShowsShared(reading.SharedUsed, reading.SharedTotal))
            {
                var share = GpuRules.Fraction(reading.SharedUsed, reading.SharedTotal);
                _shared.Show(
                    $"{Formatting.Bytes(reading.SharedUsed)} / {Formatting.Bytes(reading.SharedTotal)}",
                    share,
                    Pressure(GpuRules.MemoryPressure(share)));
            }
            else
            {
                _shared.Hide();
            }

            // [S-439] Driver lines hide themselves on a card whose driver does not answer.
            ShowOptional(_temperature, _config.ShowTemperature, reading.TemperatureC is { } c ? string.Create(CultureInfo.InvariantCulture, $"{c:0} °C") : null);
            ShowOptional(_power, _config.ShowPower, reading.PowerWatts is { } w ? string.Create(CultureInfo.InvariantCulture, $"{w:0.0} W") : null);
            ShowOptional(_clocks, _config.ShowClocks, Clocks(reading));
            ShowOptional(_fan, _config.ShowFan, reading.FanPercent is { } f ? string.Create(CultureInfo.InvariantCulture, $"{f}%") : null);

            // [S-1203] One bar per engine kind; [S-1205] the busiest processes, each as a share of the card.
            _engines.Fill(
                _config.ShowEngines && reading.HasLoad
                    ? [.. reading.Engines.Select(e => new MeterRow(GpuEngines.Label(e.Kind), Formatting.Percent(e.Percent / 100), e.Percent / 100))]
                    : [],
                Pressure(GpuPressure.Normal));
            _processes.Fill(
                _config.ShowProcesses
                    ? [.. reading.Processes.Select(p => new MeterRow(p.Name, Formatting.Percent(p.Percent / 100), p.Percent / 100,
                        string.Create(CultureInfo.InvariantCulture, $"{p.Name} — process {p.Pid}")))]
                    : [],
                Pressure(GpuPressure.Normal));
        }

        /// <summary>
        /// [S-1201] [S-1202] Climb the ladder for this room: the reading always; the history, the memory
        /// lines, the driver lines, the lists and the scale line while each fits whole; what is left goes
        /// to the reading (up to Display) and then to the chart.
        /// </summary>
        public void Lay(WidgetArea area, TimeSpan interval)
        {
            if (!area.IsMeasured)
            {
                return;
            }

            var arrangement = GpuLayout.Arrange(area);
            var split = arrangement != GpuArrangement.Stack;
            var columnWidth = split ? Math.Floor(area.Width / 2) : area.Width;
            // A strip centres its reading block: keep it off the card's bottom edge.
            var box = new WidgetArea(columnWidth, area.Height)
                .Deflate(WidgetLayout.ContentInset, arrangement == GpuArrangement.Side ? Fluent.Space1 : 0);

            // Lines grow to Body on a tile tall enough that Caption would leave the column sparse —
            // the CPU tile's proportions at the same sizes.
            var lineStep = area.HeightClass >= SizeClass.Expanded && arrangement != GpuArrangement.Side ? FluentType.Body : FluentType.Caption;
            if (lineStep != _lineStep)
            {
                _lineStep = lineStep;
                foreach (var detail in _all)
                {
                    detail.Apply(lineStep);
                }
            }

            var room = new Disclosure(box.Width, box.Height, Fluent.Space1);

            // Rung 0: the reading at its smallest — beside the name when this is one of several.
            var readingFloor = FluentType.Subtitle.LineHeight;
            room.Reserve(readingFloor);

            // Rung 1: the history, in this column unless a strip puts it beside.
            var graph = _config.ShowGraph && _last is not null;
            var chartMin = arrangement == GpuArrangement.Panels ? MinPanelChart : MinChart;
            var chartBelow = graph && arrangement != GpuArrangement.Side && room.Take(chartMin + Fluent.Space2);
            var chartBeside = graph && arrangement == GpuArrangement.Side;

            // Rung 2: the caption — beside the number when the column is wide enough, else under it.
            var precise = area.AtLeast(SizeClass.Expanded, SizeClass.Expanded);
            var captionText = _last is { HasLoad: true } r ? GpuEngines.Caption(r.Engines, precise) : string.Empty;
            var captionWidth = FluentType.EstimateWidth(FluentType.Caption, GpuEngines.CaptionCharacters(precise));
            var nameWidth = _stacked ? FluentType.EstimateWidth(FluentType.BodyStrong, 12) : 0;
            var captionBeside = captionText.Length > 0
                && box.Width >= nameWidth + FluentType.EstimateWidth(FluentType.Subtitle, 4) + captionWidth + (Fluent.Space2 * 2);
            var captionUnder = captionText.Length > 0 && !captionBeside && !_stacked
                && area.HeightClass >= SizeClass.Regular && room.Take(FluentType.Caption.LineHeight);

            // Rung 3: memory, then the driver's lines, as many as fit whole, in that order.
            var first = true;
            foreach (var detail in _all)
            {
                var fits = detail.Wanted && room.Take(detail.Height(lineStep) + (first ? Fluent.Space1 : 0));
                detail.Root.Visibility = fits ? Visibility.Visible : Visibility.Collapsed;
                first &= !fits;
            }

            _lines.Visibility = first ? Visibility.Collapsed : Visibility.Visible;

            // Rung 4: the engines and the processes — down the right on a large tile, under the lines
            // on a tall one, never on a strip.
            int engineRows, processRows;
            if (arrangement == GpuArrangement.Panels)
            {
                var lists = new Disclosure(columnWidth, area.Height, Fluent.Space2);
                engineRows = TakeList(lists, _engines);
                processRows = TakeList(lists, _processes);
            }
            else if (arrangement == GpuArrangement.Stack && area.HeightClass >= SizeClass.Expanded)
            {
                engineRows = TakeList(room, _engines);
                processRows = TakeList(room, _processes);
            }
            else
            {
                engineRows = 0;
                processRows = 0;
            }

            _engines.Show(engineRows);
            _processes.Show(processRows);
            _lists.Visibility = engineRows + processRows > 0 ? Visibility.Visible : Visibility.Collapsed;

            // Rung 5: what the chart covers, on a tile large both ways.
            var scale = chartBelow && precise && room.Take(FluentType.Caption.LineHeight + Fluent.Space2);
            _scale.Visibility = scale ? Visibility.Visible : Visibility.Collapsed;

            if (scale)
            {
                _span.Text = MeterHistory.Span(_spark.Window, interval);
            }

            // The reading takes what the rungs left, up to Display when a chart wants the rest.
            var heroBox = readingFloor + room.Remaining;
            if (chartBelow || chartBeside)
            {
                heroBox = Math.Min(heroBox, FluentType.Display.LineHeight);
            }

            var heroWidth = box.Width - nameWidth - (captionBeside ? captionWidth + Fluent.Space2 : 0);
            var hero = _stacked
                ? FluentType.Subtitle
                : FluentType.Hero(heroWidth, heroBox, 4);

            if (hero.FontSize < FluentType.Subtitle.FontSize)
            {
                hero = FluentType.Subtitle;
            }

            if (hero != _heroStep)
            {
                _heroStep = hero;
                _hero.Apply(hero);
            }

            PlaceReading(captionText, captionBeside, captionUnder);
            PlaceParts(arrangement, graph, chartBelow, chartMin);
        }

        /// <summary>A list's title and its whole rows, or nothing when fewer than <see cref="MinListRows"/> would fit.</summary>
        private static int TakeList(Disclosure room, ListBlock list)
        {
            if (list.Count == 0)
            {
                return 0;
            }

            var title = FluentType.Caption.LineHeight + Fluent.Space2;
            var wanted = Math.Min(MinListRows, list.Count);

            if (Disclosure.WholeRows(room.Remaining - title - Fluent.Space2, ListRowHeight) < wanted || !room.Take(title))
            {
                return 0;
            }

            return room.TakeRows(ListRowHeight, list.Count);
        }

        private void PlaceReading(string captionText, bool beside, bool under)
        {
            _caption.Text = captionText;
            _caption.Visibility = beside || under ? Visibility.Visible : Visibility.Collapsed;

            // [S-430] Several panels: the name leads (and gives way, trimmed) and the number keeps its
            // place at the right; one panel: the number leads and the caption follows it.
            _reading.ColumnDefinitions[0].Width = _stacked ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
            _reading.ColumnDefinitions[2].Width = _stacked ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            SetColumn(_name, 0);
            SetColumn(_hero, _stacked ? 2 : 0);
            _hero.HorizontalAlignment = _stacked ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            SetColumnSpan(_hero, 1);

            SetRow(_caption, beside ? 0 : 1);
            SetColumn(_caption, beside ? 1 : 0);
            SetColumnSpan(_caption, beside ? 1 : 3);
            _caption.Margin = beside ? new Thickness(0, 0, 0, _heroStep.LineHeight * 0.18) : new Thickness(0);
        }

        /// <summary>Put the info column, the chart and the lists where the arrangement wants them.</summary>
        private void PlaceParts(GpuArrangement arrangement, bool graph, bool chartBelow, double chartMin)
        {
            var split = arrangement != GpuArrangement.Stack;
            _root.ColumnDefinitions[1].Width = split ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

            SetRow(_info, 0);
            SetColumn(_info, 0);
            SetRowSpan(_info, arrangement == GpuArrangement.Side ? 2 : 1);

            // A strip: the reading block centred in its half, the chart full height on the right.
            _info.VerticalAlignment = arrangement == GpuArrangement.Side || !graph ? VerticalAlignment.Center : VerticalAlignment.Top;

            if (arrangement == GpuArrangement.Side)
            {
                _spark.Visibility = graph ? Visibility.Visible : Visibility.Collapsed;
                SetRow(_spark, 0);
                SetRowSpan(_spark, 2);
                SetColumn(_spark, 1);
                _spark.MinHeight = 0;
                _spark.Margin = new Thickness(0, Fluent.Space2, 0, 0);
            }
            else
            {
                _spark.Visibility = chartBelow ? Visibility.Visible : Visibility.Collapsed;
                SetRow(_spark, 1);
                SetRowSpan(_spark, 1);
                SetColumn(_spark, 0);
                _spark.MinHeight = chartMin;
                _spark.Margin = new Thickness(0, Fluent.Space2, 0, 0);
            }

            // The lists: their own column on a large tile, the end of the info column on a tall one.
            if (_lists.Parent is Microsoft.UI.Xaml.Controls.Panel holder)
            {
                holder.Children.Remove(_lists);
            }

            if (arrangement == GpuArrangement.Panels)
            {
                _root.Children.Add(_lists);
                SetRow(_lists, 0);
                SetRowSpan(_lists, 2);
                SetColumn(_lists, 1);
                _lists.Margin = new Thickness(0);
                _lists.VerticalAlignment = VerticalAlignment.Top;
            }
            else
            {
                // Each list's title carries its own top gap, so the block needs none.
                _info.Children.Insert(_info.Children.IndexOf(_scale), _lists);
                _lists.Margin = new Thickness(0);
            }

            // Without a chart below, nothing fills the second row: the info column is the whole panel.
            _root.RowDefinitions[0].Height = chartBelow ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            _root.RowDefinitions[1].Height = chartBelow ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        }

        private static void ShowOptional(Detail detail, bool wanted, string? value)
        {
            if (wanted && value is not null)
            {
                detail.Show(value, null, null);
            }
            else
            {
                detail.Hide();
            }
        }

        private static string? Clocks(GpuReading reading) => (reading.CoreClockMhz, reading.MemoryClockMhz) switch
        {
            ({ } core, { } memory) => string.Create(CultureInfo.InvariantCulture, $"{core} / {memory} MHz"),
            ({ } core, null) => string.Create(CultureInfo.InvariantCulture, $"{core} MHz"),
            (null, { } memory) => string.Create(CultureInfo.InvariantCulture, $"{memory} MHz mem"),
            _ => null,
        };

        private static Brush Pressure(GpuPressure pressure) => pressure switch
        {
            GpuPressure.Critical => CriticalBrush,
            GpuPressure.High => CautionBrush,
            _ => AccentBrush,
        };
    }

    /// <summary>A titled list of meter rows (engines, processes), kept and rewritten in place.</summary>
    private sealed class ListBlock
    {
        private readonly TextBlock _title = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
        private readonly StackPanel _rows = new();
        private readonly List<MeterListRow> _cells = [];
        private IReadOnlyList<MeterRow> _data = [];
        private Brush? _brush;

        public ListBlock(string title)
        {
            _title.Text = title;
            _title.Margin = new Thickness(WidgetLayout.ContentInset, Fluent.Space2, WidgetLayout.ContentInset, 0);
            Root.Children.Add(_title);
            Root.Children.Add(_rows);
        }

        public StackPanel Root { get; } = new();

        public int Count => _data.Count;

        public void Fill(IReadOnlyList<MeterRow> rows, Brush brush)
        {
            _data = rows;
            _brush = brush;
        }

        /// <summary>Show the first <paramref name="rows"/> rows; zero hides the block.</summary>
        public void Show(int rows)
        {
            Root.Visibility = rows > 0 ? Visibility.Visible : Visibility.Collapsed;

            while (_cells.Count < rows)
            {
                var cell = new MeterListRow();
                _cells.Add(cell);
                _rows.Children.Add(cell.Host);
            }

            for (var i = 0; i < _cells.Count; i++)
            {
                var shown = i < rows && i < _data.Count;
                _cells[i].Host.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

                if (shown)
                {
                    _cells[i].Show(_data[i], bars: true, _brush ?? new SolidColorBrush(Colors.DodgerBlue));
                }
            }
        }
    }

    /// <summary>One detail line: a label, a value, and for memory a thin bar under them.</summary>
    private sealed class Detail
    {
        private const double BarHeight = 4;
        private const double BarGap = 2;

        private readonly TextBlock _label = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
        private readonly TextBlock _value = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
        private readonly ProgressBar? _bar;

        public Detail(string label, bool withBar = false)
        {
            _label.Text = label;
            _value.TextAlignment = TextAlignment.Right;
            _value.HorizontalAlignment = HorizontalAlignment.Right;
            Typography.SetNumeralAlignment(_value, FontNumeralAlignment.Tabular);

            var line = new Grid
            {
                ColumnSpacing = Fluent.Space2,
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                },
            };

            SetColumn(_value, 1);
            line.Children.Add(_label);
            line.Children.Add(_value);

            Root.Children.Add(line);

            if (withBar)
            {
                _bar = new ProgressBar { Minimum = 0, Maximum = 1, Margin = new Thickness(0, BarGap, 0, 0) };
                Root.Children.Add(_bar);
            }

            Root.Visibility = Visibility.Collapsed;
        }

        public StackPanel Root { get; } = new();

        /// <summary>Whether the config and the data want this line; whether it fits is decided by the panel.</summary>
        public bool Wanted { get; private set; }

        /// <summary>The line's height at <paramref name="step"/>, so the panel can tell whether one more fits.</summary>
        public double Height(TypeStep step) => step.LineHeight + (_bar?.Visibility == Visibility.Visible ? BarHeight + BarGap : 0);

        public void Apply(TypeStep step)
        {
            _label.Apply(step);
            _value.Apply(step);
        }

        public void Show(string value, double? fraction, Brush? brush)
        {
            Wanted = true;
            _value.Text = value;

            if (_bar is not null)
            {
                _bar.Visibility = fraction is null ? Visibility.Collapsed : Visibility.Visible;
                _bar.Value = fraction ?? 0;

                if (brush is not null)
                {
                    _bar.Foreground = brush;
                }
            }
        }

        public void Hide()
        {
            Wanted = false;
            Root.Visibility = Visibility.Collapsed;
        }
    }
}
