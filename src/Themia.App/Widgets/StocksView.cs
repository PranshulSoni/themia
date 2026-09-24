using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Themia.App.Services;
using Themia.App.Services.Web;
using Themia.App.Shell;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.Foundation;

namespace Themia.App.Widgets;

/// <summary>
/// One instrument's price, its move, and the shape of the period — the readout
/// <see cref="MeterView"/> gives a system meter, pointed at a market.
/// <para>
/// The chart is <b>full-bleed</b>: it reaches the card's left, right and bottom edges, and the text
/// above it carries the inset instead. A chart floating inside a margin reads as a picture of a
/// chart rather than as the widget's surface.
/// </para>
/// <para>
/// The two services behind it — Yahoo's chart endpoint and CoinGecko's public API — are free,
/// shared and unauthenticated, so this widget is careful about asking: one request per symbol per
/// interval however many widgets want it, and a failure backs off rather than retrying.
/// </para>
/// </summary>
internal sealed partial class StocksView : Grid, IWidgetView
{
    private const string Scope = "stocks";

    /// <summary>The chart never shrinks below this: under it a trace is a smudge, not a chart.</summary>
    private const double SparklineHeight = Fluent.Space6;

    /// <summary>Nor grows past it: a chart taller than the price it annotates inverts the tile.</summary>
    private const double SparklineMaxHeight = 120;

    /// <summary>The share of the tile the chart takes between those two bounds.</summary>
    private const double SparklineShare = 0.34;

    /// <summary>The widest price this widget prints, in characters — "123,456.7890".</summary>
    private const double ReadingCharacters = 12;

    private readonly TextBlock _title = Fluent.Text(FluentType.BodyStrong);
    private readonly TextBlock _freshness = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly TextBlock _price = Fluent.Text(FluentType.Subtitle);
    private readonly TextBlock _change = Fluent.Text(FluentType.Body);
    private readonly TextBlock _currency = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly TextBlock _phase = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly FontIcon _phaseIcon = Fluent.Icon(Glyphs.Moon, "TextFillColorTertiaryBrush", 12);
    private readonly StackPanel _phaseBadge = new() { Orientation = Orientation.Horizontal, Spacing = Fluent.Space1 };
    private readonly Grid _header;
    private readonly Grid _readout = new();
    private readonly Grid _sparkHost = new();
    private readonly Polyline _sparkLine = new();
    private readonly Polygon _sparkFill = new();
    private readonly StackPanel _emptyState;
    private readonly TextBlock _emptyMessage = new();

    private readonly Brush _freshBrush = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);
    private readonly Brush _staleBrush = FluentLayout.Themed("SystemFillColorCautionBrush", Colors.Goldenrod);
    private readonly Brush _upBrush = FluentLayout.Themed("SystemFillColorSuccessBrush", Colors.SeaGreen);
    private readonly Brush _downBrush = FluentLayout.Themed("SystemFillColorCriticalBrush", Colors.IndianRed);
    private readonly Brush _flatBrush = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray);

    private readonly RefreshSchedule _schedule = new(Scope, "quotes");

    private Ticker? _ticker;
    private StocksConfig _config = new();
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;
    private CancellationTokenSource? _inFlight;
    private int _generation;

    private Fetched<TickerQuote>? _quote;
    private TypeStep _heroStep = FluentType.Subtitle;
    private bool _detached;

    public StocksView()
    {
        // No padding on the view: the chart is a full-bleed band and has to reach the card edge on
        // three sides. Text carries the inset instead, exactly as a list row does.
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _title.Text = "Stocks";
        _header = Fluent.Header(Glyphs.Stocks, _title, _freshness);
        _header.Margin = Fluent.HeaderMargin;
        SetRow(_header, 0);

        BuildReadout();
        BuildSparkline();

        _emptyState = Fluent.EmptyState(Glyphs.Stocks, _emptyMessage, out _);
        _emptyState.Visibility = Visibility.Collapsed;
        SetRow(_emptyState, 1);

        Children.Add(_header);
        Children.Add(_readout);
        Children.Add(_sparkHost);
        Children.Add(_emptyState);

        SizeChanged += OnSizeChanged;
    }

    private void BuildReadout()
    {
        _price.VerticalAlignment = VerticalAlignment.Bottom;

        // Lifted off the baseline of the price it sits beside: bottom-aligning a 12px caption
        // against a 68px number puts it below that number's baseline and reads as a slipped line.
        _change.VerticalAlignment = VerticalAlignment.Bottom;
        _change.Margin = new Thickness(Fluent.Space2, 0, 0, Fluent.Space1);

        _currency.VerticalAlignment = VerticalAlignment.Bottom;
        _currency.Margin = new Thickness(Fluent.Space2, 0, 0, Fluent.Space1);

        _phaseIcon.VerticalAlignment = VerticalAlignment.Center;
        _phase.VerticalAlignment = VerticalAlignment.Center;
        _phaseBadge.VerticalAlignment = VerticalAlignment.Bottom;
        _phaseBadge.Margin = new Thickness(Fluent.Space3, 0, 0, Fluent.Space1);
        _phaseBadge.Visibility = Visibility.Collapsed;
        _phaseBadge.Children.Add(_phaseIcon);
        _phaseBadge.Children.Add(_phase);

        // A Grid rather than a horizontal StackPanel, for the reason MeterView gives: a StackPanel
        // measures its children with unbounded width, so nothing beside the price ever learns how
        // much room is left and its trimming never fires.
        _readout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _readout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _readout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _readout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _readout.Margin = WidgetLayout.SideInset;
        _readout.VerticalAlignment = VerticalAlignment.Center;

        SetColumn(_price, 0);
        SetColumn(_change, 1);
        SetColumn(_currency, 2);
        SetColumn(_phaseBadge, 3);
        _readout.Children.Add(_price);
        _readout.Children.Add(_change);
        _readout.Children.Add(_currency);
        _readout.Children.Add(_phaseBadge);
        SetRow(_readout, 1);
    }

    /// <summary>
    /// A filled area under a stroked line — the shape a sparkline is expected to have. The fill
    /// carries the trend at a glance; the line keeps the last value legible on a small tile.
    /// </summary>
    private void BuildSparkline()
    {
        _sparkFill.Stretch = Stretch.None;

        // The wash under the trace is the trend colour at low alpha, and the alpha is applied to
        // the *shape* rather than to the shared brush, which is the same object every other
        // surface of that colour in the app paints with.
        _sparkFill.Opacity = Fluent.ChartWash;

        _sparkLine.StrokeThickness = Fluent.ChartStroke;
        _sparkLine.StrokeLineJoin = PenLineJoin.Round;
        _sparkLine.Stretch = Stretch.None;

        _sparkHost.IsHitTestVisible = false;
        _sparkHost.MinHeight = SparklineHeight;
        _sparkHost.Children.Add(_sparkFill);
        _sparkHost.Children.Add(_sparkLine);
        _sparkHost.SizeChanged += (_, _) => RedrawSparkline();
        SetRow(_sparkHost, 2);
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        _detached = false;
        ApplyConfig(config);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        var next = config as StocksConfig ?? new StocksConfig();
        var previous = _config;
        _config = next;

        var needsFetch = previous.EffectiveSymbol != next.EffectiveSymbol
            || previous.Market != next.Market
            || previous.Range != next.Range;

        if (needsFetch)
        {
            // A different instrument, or a different period. The numbers in hand are about
            // something else and must not sit under the new heading for even one frame.
            _quote = null;
            _schedule.Reset();
        }

        if (Resubscribe())
        {
            return;
        }

        if (needsFetch)
        {
            Reload();
        }
        else
        {
            Render();
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
        _subscription = _ticker?.Subscribe(interval, OnTick);
        return _subscription is not null;
    }

    private void OnTick()
    {
        if (_detached)
        {
            return;
        }

        if (_schedule.MayFetch(DateTimeOffset.Now))
        {
            Reload();
            return;
        }

        // Backed off, but the badge still has to flip at the opening bell and the freshness line
        // still has to count up, both of which are functions of the clock rather than of a fetch.
        Render();
    }

    private void Reload()
    {
        if (_detached)
        {
            return;
        }

        var symbol = _config.EffectiveSymbol;

        if (symbol.Length == 0)
        {
            // Told apart on purpose: nothing typed is "not set up", something typed that is not a
            // symbol is "refused". Silently treating the second as the first would leave somebody
            // staring at an empty widget wondering which.
            var status = string.IsNullOrWhiteSpace(_config.Symbol) ? FetchStatus.NotConfigured : FetchStatus.Refused;
            _schedule.Record(status, _config.TickInterval, _config.Symbol, DateTimeOffset.Now);
            Render();
            return;
        }

        var market = _config.Market;
        var range = _config.Range;

        _quote ??= MarketService.Peek(symbol, market, range);

        _inFlight?.Cancel();
        var cts = new CancellationTokenSource();
        _inFlight = cts;
        var generation = ++_generation;

        Render();
        _ = LoadAsync(symbol, market, range, generation, cts);
    }

    private async Task LoadAsync(
        string symbol,
        QuoteMarket market,
        QuoteRange range,
        int generation,
        CancellationTokenSource cts)
    {
        try
        {
            var result = await MarketService
                .QuoteAsync(symbol, market, range, _config.TickInterval, cts.Token)
                .ConfigureAwait(true);

            if (_detached || generation != _generation)
            {
                return;
            }

            _schedule.Record(result.Status, _config.TickInterval, result.Detail, DateTimeOffset.Now);

            if (result.Value is not null)
            {
                _quote = result;
            }
            else if (_quote is { } held)
            {
                _quote = held.Stale(result.Status, result.Detail);
            }

            Render();
        }
        catch (OperationCanceledException)
        {
            // ignore: superseded by a newer request, which will draw in this one's place.
        }
        finally
        {
            if (ReferenceEquals(_inFlight, cts))
            {
                _inFlight = null;
            }

            cts.Dispose();
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _header.Visibility = e.NewSize.Height >= Fluent.HeaderHidesBelow
            ? Visibility.Visible
            : Visibility.Collapsed;

        ApplyTypeScale();
    }

    /// <summary>
    /// Step the price to the tile, and give the chart a bounded band at the foot.
    /// <para>
    /// The <b>reading</b> takes the slack, not the chart. With the star on the chart row, a tall
    /// tile handed the trace a 350px band and stranded the number at the top — the proportion
    /// Windows gives a chart under a readout is a band, not the remainder.
    /// </para>
    /// </summary>
    private void ApplyTypeScale()
    {
        var width = ActualWidth - (Fluent.Space4 * 2);
        var height = ActualHeight;

        var band = Math.Clamp(height * SparklineShare, SparklineHeight, SparklineMaxHeight);
        _sparkHost.Height = _sparkHost.Visibility == Visibility.Visible ? band : 0;

        var reserved = (_header.Visibility == Visibility.Visible ? FluentType.BodyStrong.LineHeight + Fluent.Space6 : 0)
            + (_sparkHost.Visibility == Visibility.Visible ? band : 0);

        // The change and the currency share the line with the price, so the price gets at most
        // two thirds of it. Letting the number take the whole line is how a narrow tile ends up
        // with a 68px price and a change trimmed to a single ellipsis.
        var step = FluentType.Fit(
            width * 0.66,
            Math.Max(height - reserved, 0),
            ReadingCharacters,
            FluentType.Subtitle,
            FluentType.Display);

        if (step == _heroStep)
        {
            return;
        }

        _heroStep = step;
        _price.Apply(step);

        // The change steps once, with the price, rather than sliding: next to a 28px number a 12px
        // caption is right, next to a 68px one it is a footnote.
        _change.Apply(step.FontSize >= FluentType.Title.FontSize ? FluentType.BodyLarge : FluentType.Body);
    }

    private void Render()
    {
        if (_detached)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var held = _quote;

        if (held?.Value is not { } quote)
        {
            var status = _schedule.Status;

            _emptyMessage.Text = status switch
            {
                FetchStatus.NotConfigured => "Choose a symbol in settings",
                FetchStatus.Refused => $"\"{_config.Symbol}\" is not a symbol",
                FetchStatus.Empty => "No quote for that symbol",
                _ => FetchState.Message(status),
            };

            _emptyState.Visibility = Visibility.Visible;
            _readout.Visibility = Visibility.Collapsed;
            _sparkHost.Visibility = Visibility.Collapsed;
            _freshness.Text = string.Empty;
            ApplyTypeScale();
            return;
        }

        _emptyState.Visibility = Visibility.Collapsed;
        _readout.Visibility = Visibility.Visible;

        _title.Text = Heading(quote);
        _price.Text = MarketQuote.Price(quote.Price, quote.PriceHint);
        _change.Text = MarketQuote.ChangePercent(quote.ChangePercent);
        _currency.Text = quote.Currency;

        var direction = quote.ChangePercent switch
        {
            > 0 => _upBrush,
            < 0 => _downBrush,
            _ => _flatBrush,
        };

        _change.Foreground = direction;

        UpdatePhase(quote, now);
        UpdateFreshness(held, now);

        var wanted = _config.ShowSparkline && quote.Closes.Count >= 2;
        _sparkHost.Visibility = wanted ? Visibility.Visible : Visibility.Collapsed;

        if (wanted)
        {
            _sparkLine.Stroke = direction;
            _sparkFill.Fill = direction;
            RedrawSparkline();
        }

        ApplyTypeScale();
    }

    /// <summary>What the header says: the user's own name for it, else the service's, else the symbol.</summary>
    private string Heading(TickerQuote quote)
    {
        if (!string.IsNullOrWhiteSpace(_config.DisplayName))
        {
            return _config.DisplayName.Trim();
        }

        return !string.IsNullOrWhiteSpace(quote.Name) ? quote.Name : quote.Symbol;
    }

    /// <summary>
    /// The session badge.
    /// <para>
    /// Evaluated against the clock at render, not at fetch: the badge is then right on a cold
    /// launch from the cache and flips at the bell regardless of the refresh interval. Crypto never
    /// carries one — it trades every minute of every day, so there is no session to be outside of.
    /// An <see cref="MarketPhase.Unknown"/> schedule carries none either, because "the reply said
    /// nothing about the calendar" is not "the market is closed".
    /// </para>
    /// </summary>
    private void UpdatePhase(TickerQuote quote, DateTimeOffset now)
    {
        if (!_config.ShowMarketState || _config.Market == QuoteMarket.Crypto)
        {
            _phaseBadge.Visibility = Visibility.Collapsed;
            return;
        }

        var phase = MarketQuote.Phase(quote.Sessions, now);
        var label = MarketQuote.PhaseLabel(phase);

        if (label.Length == 0)
        {
            _phaseBadge.Visibility = Visibility.Collapsed;
            return;
        }

        _phase.Text = label;
        _phaseIcon.Glyph = phase == MarketPhase.Closed ? Glyphs.Moon : Glyphs.Sun;
        _phaseBadge.Visibility = Visibility.Visible;
    }

    private void UpdateFreshness(Fetched<TickerQuote> held, DateTimeOffset now)
    {
        var failed = FetchState.IsFailure(held.Status);
        var stale = failed || FetchState.IsStale(held.At, now, _config.TickInterval);

        _freshness.Text = FetchState.Freshness(held.At, now);
        _freshness.Foreground = stale ? _staleBrush : _freshBrush;

        // A price is the single most dangerous number in this app to show stale, so the age is
        // always on screen and the reason for it is always one hover away.
        ToolTipService.SetToolTip(
            _freshness,
            failed
                ? $"{FetchState.Message(held.Status)}. Showing the price from {FetchState.Freshness(held.At, now).ToLowerInvariant()}."
                : $"Priced {FetchState.Freshness(held.At, now).ToLowerInvariant()}");
    }

    /// <summary>
    /// Redraw the trace. Cheap by construction: two visuals, rebuilt only on a new quote or a
    /// resize, over a series the service already downsampled to a few hundred points.
    /// </summary>
    private void RedrawSparkline()
    {
        if (_sparkHost.Visibility != Visibility.Visible || _quote?.Value is not { } quote)
        {
            return;
        }

        var width = _sparkHost.ActualWidth;
        var height = _sparkHost.ActualHeight;
        var scaled = MarketQuote.Normalise(quote.Closes);

        if (width <= 0 || height <= 0 || scaled.Count < 2)
        {
            _sparkLine.Points.Clear();
            _sparkFill.Points.Clear();
            return;
        }

        // Inset by the stroke width so the day's high and low are not clipped in half at the edges.
        var inset = _sparkLine.StrokeThickness;
        var usable = Math.Max(1, height - inset);

        var line = new PointCollection();

        for (var i = 0; i < scaled.Count; i++)
        {
            // The window is exactly as long as the series, which makes Sparkline's fixed-slot rule
            // spread the points edge to edge — right here, where the series is complete when it
            // arrives, and still the same rule the live meters use while theirs are filling.
            var x = Sparkline.SampleX(i, scaled.Count, scaled.Count) * width;
            var y = (inset / 2) + (Sparkline.SampleY(scaled[i]) * usable);
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

        SizeChanged -= OnSizeChanged;

        _subscription?.Dispose();
        _subscription = null;
        _subscribedInterval = TimeSpan.Zero;

        _inFlight?.Cancel();
        _inFlight = null;

        _ticker = null;
    }
}
