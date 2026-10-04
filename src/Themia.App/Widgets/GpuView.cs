using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Glyphs = Themia.App.Shell.Glyphs;

namespace Themia.App.Widgets;

/// <summary>
/// [S-430] Graphics load, as the CPU and memory tiles show theirs: the reading, a few lines of
/// detail under it, and the history as a full-bleed band along the bottom.
/// <para>
/// One adapter fills the tile and names the card after itself. Two or more stack as panels, each
/// with its own name, reading and history ([S-433]) — a laptop's integrated and discrete GPUs are
/// two different stories, and one averaged number would tell neither.
/// </para>
/// </summary>
internal sealed partial class GpuView : Grid, IWidgetView, IWidgetSurfaceAware
{
    private const string Scope = "gpu-widget";

    private readonly StackPanel _panels = new();
    private readonly ScrollViewer _scroller;
    private readonly StackPanel _empty;
    private readonly TextBlock _emptyText = new();
    private readonly List<Panel> _shown = [];

    private Ticker? _ticker;
    private IWidgetSurface? _surface;
    private GpuConfig _config = new();
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval;
    private List<int> _selection = [];
    private GpuSnapshot? _latest;
    private bool _acquired;
    private bool _detached;

    public GpuView()
    {
        // The one-adapter panel stretches to the tile; stacked panels scroll, with the bar hidden as
        // every widget body hides it.
        _scroller = new ScrollViewer
        {
            Content = _panels,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
        };

        _emptyText.Text = "No graphics adapter to read";
        _empty = Fluent.EmptyState(Glyphs.Gpu, _emptyText, out _);
        _empty.Visibility = Visibility.Collapsed;

        Children.Add(_scroller);
        Children.Add(_empty);

        SizeChanged += (_, _) => Layout();
    }

    public void Bind(IWidgetSurface surface) => _surface = surface;

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        _detached = false;

        if (!_acquired)
        {
            GpuSampler.Acquire();
            _acquired = true;
        }

        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as GpuConfig ?? new GpuConfig();

        var selection = GpuRules.ResolveAdapters(_config.Adapters);

        // [S-436] A different adapter is a different measurement: its panels — and their history —
        // start fresh. Anything else only changes what the existing panels show.
        if (!selection.SequenceEqual(_selection))
        {
            _selection = selection;
            BuildPanels();
        }

        foreach (var panel in _shown)
        {
            panel.Configure(_config, stacked: GpuRules.Stacked(_selection.Count));
        }

        if (!Resubscribe() && _latest is not null)
        {
            Present(_latest, addSample: false);
        }

        Layout();
    }

    private void BuildPanels()
    {
        _panels.Children.Clear();
        _shown.Clear();

        var stacked = GpuRules.Stacked(_selection.Count);
        _panels.Spacing = stacked ? Fluent.Space3 : 0;

        foreach (var index in _selection)
        {
            var panel = new Panel(index);
            _shown.Add(panel);
            _panels.Children.Add(panel.Root);
        }
    }

    private bool Resubscribe()
    {
        var interval = _config.TickInterval;

        if (_subscription is not null && interval == _subscribedInterval)
        {
            return false;
        }

        _subscription?.Dispose();
        _subscribedInterval = interval;
        _subscription = _ticker?.Subscribe(interval, () => _ = SampleAsync());
        return _subscription is not null;
    }

    private async Task SampleAsync()
    {
        if (_detached)
        {
            return;
        }

        GpuSnapshot snapshot;
        try
        {
            snapshot = await GpuSampler.SampleAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "could not read the graphics adapters", ex);
            return;
        }

        // A sample that lands after the widget was torn down belongs to nobody.
        if (_detached)
        {
            return;
        }

        _latest = snapshot;
        Present(snapshot, addSample: true);
    }

    private void Present(GpuSnapshot snapshot, bool addSample)
    {
        // [S-433] No adapter at all, or none of the chosen ones present (an eGPU unplugged): said
        // as a state, not as a tile of dashes.
        var none = snapshot.Adapters.Count == 0;
        var gone = !none && _selection.All(index => index >= snapshot.Readings.Count);
        _emptyText.Text = none ? "No graphics adapter to read" : "The chosen graphics adapter is not here";
        _empty.Visibility = none || gone ? Visibility.Visible : Visibility.Collapsed;
        _scroller.Visibility = none || gone ? Visibility.Collapsed : Visibility.Visible;
        none |= gone;

        if (none)
        {
            _surface?.SetHeaderTitle(null);
            return;
        }

        for (var i = 0; i < _shown.Count; i++)
        {
            var index = _selection[i];
            var reading = index < snapshot.Readings.Count ? snapshot.Readings[index] : null;
            _shown[i].Show(reading, addSample);
        }

        // [S-430] One adapter names the card; a stack keeps the catalogue's "GPU" and names each panel.
        var single = _shown.Count == 1 && _selection[0] < snapshot.Readings.Count
            ? GpuRules.ShortName(snapshot.Readings[_selection[0]].Name)
            : null;

        _surface?.SetHeaderTitle(single);
        Layout();
    }

    /// <summary>Fit each panel to the room it has: one panel gets the whole tile, a stack gets a fixed height each.</summary>
    private void Layout()
    {
        var width = ActualWidth;
        var height = ActualHeight;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        var stacked = GpuRules.Stacked(_shown.Count);

        foreach (var panel in _shown)
        {
            panel.Fit(width, stacked ? Panel.StackedHeight(height, _shown.Count) : height);
        }
    }

    public void Detach()
    {
        _detached = true;
        _subscription?.Dispose();
        _subscription = null;
        _subscribedInterval = TimeSpan.Zero;

        if (_acquired)
        {
            _acquired = false;
            GpuSampler.Release();
        }
    }

    /// <summary>
    /// One adapter: its reading, its detail lines and its sparkline. Built once and rewritten in
    /// place on every sample, so the tile does not flicker as it updates.
    /// </summary>
    private sealed class Panel
    {
        /// <summary>A stacked panel never shrinks below this: under it a panel is a name and a number.</summary>
        private const double MinStackedHeight = 132;

        private static readonly Brush ReadingBrush = FluentLayout.Themed("TextFillColorPrimaryBrush", Colors.White);
        private static readonly Brush UnavailableBrush = FluentLayout.Themed("TextFillColorDisabledBrush", Colors.Gray);
        private static readonly Brush AccentBrush = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);
        private static readonly Brush CautionBrush = FluentLayout.Themed("SystemFillColorCautionBrush", Colors.Orange);
        private static readonly Brush CriticalBrush = FluentLayout.Themed("SystemFillColorCriticalBrush", Colors.Red);

        private readonly int _index;
        private readonly Grid _root = new();
        private readonly TextBlock _name = Fluent.Text(FluentType.BodyStrong, "TextFillColorSecondaryBrush");
        private readonly TextBlock _hero = Fluent.Text(FluentType.Subtitle);
        private readonly Grid _reading = new();
        private readonly StackPanel _details = new() { Spacing = Fluent.Space1 };
        private readonly StackPanel _content = new() { Spacing = Fluent.Space2 };
        private readonly SparkBand _spark = new(GpuRules.HistoryLength);
        private readonly Detail _dedicated = new("Dedicated", withBar: true);
        private readonly Detail _shared = new("Shared", withBar: true);
        private readonly Detail _temperature = new("Temperature");
        private readonly Detail _power = new("Power");
        private readonly Detail _fan = new("Fan");
        private readonly Detail _clocks = new("Clocks");
        private readonly Detail _video = new("Enc / Dec");
        private readonly Detail[] _all;

        private GpuConfig _config = new();
        private bool _stacked;
        private TypeStep _heroStep = FluentType.Subtitle;

        public Panel(int index)
        {
            _index = index;
            _all = [_dedicated, _shared, _temperature, _power, _fan, _clocks, _video];

            _hero.Text = "--";
            _hero.Foreground = UnavailableBrush;
            Typography.SetNumeralAlignment(_hero, FontNumeralAlignment.Tabular);

            // In a stack, the panel's own heading: name on the left, the reading on the right — the
            // card's header can only name one adapter.
            _name.Text = $"GPU {index}";
            _name.VerticalAlignment = VerticalAlignment.Center;
            _reading.ColumnSpacing = Fluent.Space2;
            _reading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _reading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _reading.Margin = WidgetLayout.SideInset;
            SetColumn(_name, 0);
            SetColumn(_hero, 1);
            _reading.Children.Add(_name);
            _reading.Children.Add(_hero);

            foreach (var detail in _all)
            {
                _details.Children.Add(detail.Root);
            }

            _details.Margin = WidgetLayout.SideInset;

            // The reading and its detail lines are one block, centred in what the band leaves — the
            // CPU tile's proportions, with the detail under the number instead of beside it.
            _content.Children.Add(_reading);
            _content.Children.Add(_details);

            _root.RowSpacing = Fluent.Space2;
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            SetRow(_content, 0);
            SetRow(_spark, 1);
            _root.Children.Add(_content);
            _root.Children.Add(_spark);
        }

        public FrameworkElement Root => _root;

        /// <summary>How tall each panel of a stack is: an equal share of the tile, but never less than readable.</summary>
        public static double StackedHeight(double height, int count) => Math.Max(MinStackedHeight, (height - (Fluent.Space3 * (count - 1))) / count);

        public void Configure(GpuConfig config, bool stacked)
        {
            _config = config;
            _stacked = stacked;

            _name.Visibility = stacked ? Visibility.Visible : Visibility.Collapsed;
            _content.VerticalAlignment = stacked ? VerticalAlignment.Top : VerticalAlignment.Center;
            _hero.HorizontalAlignment = stacked ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            SetColumn(_hero, stacked ? 1 : 0);
            SetColumnSpan(_hero, stacked ? 1 : 2);

            _spark.Visibility = config.ShowGraph ? Visibility.Visible : Visibility.Collapsed;
        }

        public void Show(GpuReading? reading, bool addSample)
        {
            if (reading is null)
            {
                // [S-433] An adapter that has gone — undocked, or the selection names one this
                // machine does not have. Said in the panel, not by blanking the tile.
                _name.Text = $"GPU {_index}";
                _hero.Text = "--";
                _hero.Foreground = UnavailableBrush;
                foreach (var detail in _all)
                {
                    detail.Hide();
                }

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

            // [S-437] The memory rows only when they say something true.
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

            // [S-439] Driver rows hide themselves on a card whose driver does not answer.
            ShowOptional(_temperature, _config.ShowTemperature, reading.TemperatureC is { } c ? $"{c:0} °C" : null);
            ShowOptional(_power, _config.ShowPower, reading.PowerWatts is { } w ? $"{w:0.0} W" : null);
            ShowOptional(_fan, _config.ShowFan, reading.FanPercent is { } f ? $"{f}%" : null);
            ShowOptional(_clocks, _config.ShowClocks, Clocks(reading));
            ShowOptional(_video, _config.ShowVideoEngines, Video(reading));
        }

        /// <summary>
        /// Step the reading to the room and drop detail lines from the bottom when they no longer fit,
        /// so a small tile keeps its number rather than squeezing everything.
        /// </summary>
        public void Fit(double width, double height)
        {
            _root.Height = height;

            var band = _spark.Visibility == Visibility.Visible
                ? Math.Clamp(height * (_stacked ? 0.28 : 0.32), Fluent.Space6, 96)
                : 0;
            _spark.Height = band;

            // The reading takes what the band leaves, less its gap; detail lines are whole lines
            // and are dropped from the end until the reading keeps at least a Subtitle line.
            var available = height - band - (band > 0 ? Fluent.Space2 : 0);
            var heroFloor = _stacked ? FluentType.BodyStrong.LineHeight : FluentType.Subtitle.LineHeight;
            var room = Math.Max(0, available - heroFloor - Fluent.Space2);
            var used = 0.0;

            foreach (var detail in _all)
            {
                var fits = detail.Wanted && room >= detail.Height;
                detail.Root.Visibility = fits ? Visibility.Visible : Visibility.Collapsed;

                if (fits)
                {
                    room -= detail.Height + Fluent.Space1;
                    used += detail.Height + (used > 0 ? Fluent.Space1 : Fluent.Space2);
                }
            }

            _details.Visibility = used > 0 ? Visibility.Visible : Visibility.Collapsed;

            var hero = _stacked
                ? FluentType.Subtitle
                : FluentType.Hero(width - (Fluent.Space4 * 2), Math.Min(available - used, FluentType.Display.LineHeight), 4);

            if (hero.FontSize < FluentType.Subtitle.FontSize)
            {
                hero = FluentType.Subtitle;
            }

            if (hero != _heroStep)
            {
                _heroStep = hero;
                _hero.Apply(hero);
            }
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

        private static string? Video(GpuReading reading) => (reading.EncoderPercent, reading.DecoderPercent) switch
        {
            ({ } enc, { } dec) => string.Create(CultureInfo.InvariantCulture, $"{enc:0}% / {dec:0}%"),
            ({ } enc, null) => string.Create(CultureInfo.InvariantCulture, $"{enc:0}%"),
            (null, { } dec) => string.Create(CultureInfo.InvariantCulture, $"{dec:0}%"),
            _ => null,
        };

        private static Brush Pressure(GpuPressure pressure) => pressure switch
        {
            GpuPressure.Critical => CriticalBrush,
            GpuPressure.High => CautionBrush,
            _ => AccentBrush,
        };
    }

    /// <summary>One detail line: a label, a value, and for memory a thin bar under them.</summary>
    private sealed class Detail
    {
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
                _bar = new ProgressBar { Minimum = 0, Maximum = 1, Margin = new Thickness(0, 2, 0, 0) };
                Root.Children.Add(_bar);
            }

            Root.Visibility = Visibility.Collapsed;
        }

        public StackPanel Root { get; } = new();

        /// <summary>Whether the config and the data want this line; whether it fits is decided by the panel.</summary>
        public bool Wanted { get; private set; }

        /// <summary>The line's height, so the panel can tell whether one more fits.</summary>
        public double Height => FluentType.Caption.LineHeight + (_bar?.Visibility == Visibility.Visible ? 6 : 0);

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
