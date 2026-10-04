using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Services.Web;
using Themia.App.Shell;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// The next few days, from open-meteo's daily block.
/// <para>
/// A Windows 11 list: one fixed-height row per day, no rule between them, and each row spanning the
/// full width of the card with the inset carried as its own padding. The original is a grid of
/// cards that grow to fill the tile — a web layout, and the reason a taller widget there showed the
/// same seven days in seven taller boxes rather than showing more of them.
/// </para>
/// <para>
/// The bar on each row is scaled against the whole week rather than against that day's own range,
/// which is the entire point of having it: a week that runs 12° to 14° every day should draw seven
/// short marks in the same place, not seven full-width ones.
/// </para>
/// </summary>
internal sealed partial class WeatherForecastView : Grid, IWidgetView, IWidgetHeaderContent, IWidgetSurfaceAware
{
    private const string Scope = "forecast";

    /// <summary>Under this width the range bar is dropped: it needs room to mean anything.</summary>
    private const double BarNeedsWidth = 300;

    /// <summary>And under this, the precipitation column goes too, leaving the day and its two numbers.</summary>
    private const double PrecipitationNeedsWidth = 380;

    /// <summary>The bar's own thickness. Fluent's hairline would read as a scratch at this length.</summary>
    private const double BarHeight = Fluent.Space1;

    private readonly TextBlock _freshness = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _emptyState;
    private readonly TextBlock _emptyMessage = new();

    private readonly List<IDisposable> _hovers = [];

    private readonly Brush _freshBrush = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);
    private readonly Brush _staleBrush = FluentLayout.Themed("SystemFillColorCautionBrush", Colors.Goldenrod);
    private readonly Brush _trackBrush = FluentLayout.Themed("ControlStrongFillColorDisabledBrush", Colors.Gray);
    private readonly Brush _barBrush = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);

    private readonly RefreshSchedule _schedule = new(Scope, "open-meteo");

    private Ticker? _ticker;
    private WeatherForecastConfig _config = new();

    /// <summary>
    /// [S-13a] A copy of the settings this view last acted on, which is what a change is measured
    /// against. Not <c>_config</c>: the settings page edits the live config object in place, so by
    /// the time a change arrives the "previous" config already holds the new values, and comparing
    /// the two found no difference — a new address or place stayed unfetched until the next tick.
    /// </summary>
    private WeatherForecastConfig _applied = new();

    /// <summary>The card this view draws on, which owns the header it names.</summary>
    private IWidgetSurface? _surface;
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;
    private CancellationTokenSource? _inFlight;
    private int _generation;

    private Fetched<Forecast>? _forecast;
    private bool _showBar = true;

    /// <summary>Narrower than this, a row is the day and its high only. [S-658]</summary>
    private const double DetailNeedsWidth = 160;

    private bool _narrow;
    private bool _showPrecipitation = true;
    private bool _compact;
    private bool _detached;

    public WeatherForecastView()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _scroll = new ScrollViewer
        {
            Content = _list,
            Padding = WidgetLayout.ListInset,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };

        _emptyState = Fluent.EmptyState(Glyphs.Forecast, _emptyMessage, out _);
        _emptyState.Visibility = Visibility.Collapsed;

        Children.Add(_scroll);
        Children.Add(_emptyState);

        SizeChanged += OnSizeChanged;
    }

    /// <summary>
    /// The age of the forecast, in the card's header. Built in the constructor because the card
    /// reads this once, when it builds that header.
    /// </summary>
    public FrameworkElement? HeaderTrailing => _freshness;

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _ticker = ticker;
        _detached = false;
        ApplyConfig(config);
    }

    /// <inheritdoc />
    public void Bind(IWidgetSurface surface)
    {
        _surface = surface;
        PublishTitle();
    }

    public void ApplyConfig(WidgetConfig config)
    {
        var next = config as WeatherForecastConfig ?? new WeatherForecastConfig();
        var previous = _applied;
        _config = next;
        _applied = (WeatherForecastConfig)next.Clone();
        PublishTitle();

        // Only the fields that change what comes back are worth a re-read. Toggling the
        // precipitation column redraws the days already in hand; it must not fire a request.
        var needsFetch = previous.Latitude != next.Latitude
            || previous.Longitude != next.Longitude
            || previous.Unit != next.Unit
            || previous.EffectiveDays != next.EffectiveDays;

        if (needsFetch)
        {
            _forecast = null;
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

        // Still backed off, but the day labels and the freshness line both move on their own —
        // "Today" has to become "Wed" at midnight whether or not the network is working.
        Render();
    }

    private void Reload()
    {
        if (_detached)
        {
            return;
        }

        if (!WebInput.AreCoordinates(_config.Latitude, _config.Longitude))
        {
            _schedule.Record(FetchStatus.NotConfigured, _config.TickInterval, null, DateTimeOffset.Now);
            Render();
            return;
        }

        var latitude = _config.Latitude!.Value;
        var longitude = _config.Longitude!.Value;
        var unit = _config.Unit;
        var days = _config.EffectiveDays;

        _forecast ??= WeatherService.PeekForecast(latitude, longitude, unit, days);

        _inFlight?.Cancel();
        var cts = new CancellationTokenSource();
        _inFlight = cts;
        var generation = ++_generation;

        Render();
        _ = LoadAsync(latitude, longitude, unit, days, generation, cts);
    }

    private async Task LoadAsync(
        double latitude,
        double longitude,
        TemperatureUnit unit,
        int days,
        int generation,
        CancellationTokenSource cts)
    {
        try
        {
            var result = await WeatherService
                .ForecastAsync(latitude, longitude, unit, days, _config.TickInterval, cts.Token)
                .ConfigureAwait(true);

            if (_detached || generation != _generation)
            {
                return;
            }

            _schedule.Record(result.Status, _config.TickInterval, result.Detail, DateTimeOffset.Now);

            if (result.Value is not null)
            {
                _forecast = result;
            }
            else if (_forecast is { } held)
            {
                _forecast = held.Stale(result.Status, result.Detail);
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
        var bar = e.NewSize.Width >= BarNeedsWidth;
        var precipitation = e.NewSize.Width >= PrecipitationNeedsWidth;
        var compact = e.NewSize.Height < Fluent.CompactBelow;
        var narrow = e.NewSize.Width > 0 && e.NewSize.Width < DetailNeedsWidth;

        if (bar == _showBar && precipitation == _showPrecipitation && compact == _compact && narrow == _narrow)
        {
            return;
        }

        _showBar = bar;
        _showPrecipitation = precipitation;
        _compact = compact;
        _narrow = narrow;
        Render();
    }

    private void Render()
    {
        if (_detached)
        {
            return;
        }

        ClearRows();

        var now = DateTimeOffset.Now;
        var held = _forecast;
        var days = held?.Value?.Days;

        if (days is null || days.Count == 0)
        {
            var status = _schedule.Status;

            _emptyMessage.Text = status == FetchStatus.NotConfigured
                ? "Choose a place in settings"
                : FetchState.Message(status);

            _emptyState.Visibility = Visibility.Visible;
            _scroll.Visibility = Visibility.Collapsed;
            _freshness.Text = string.Empty;
            return;
        }

        _emptyState.Visibility = Visibility.Collapsed;
        _scroll.Visibility = Visibility.Visible;

        // One clock reading for the whole list, so a forecast drawn across midnight cannot label
        // two different rows "Today".
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var (coldest, warmest) = Extremes(days);

        foreach (var day in days)
        {
            _list.Children.Add(BuildRow(day, today, coldest, warmest));
        }

        UpdateFreshness(held!, now);
    }

    private void UpdateFreshness(Fetched<Forecast> held, DateTimeOffset now)
    {
        var failed = FetchState.IsFailure(held.Status);
        var stale = failed || FetchState.IsStale(held.At, now, _config.TickInterval);

        _freshness.Text = FetchState.Freshness(held.At, now);
        _freshness.Foreground = stale ? _staleBrush : _freshBrush;

        ToolTipService.SetToolTip(
            _freshness,
            failed
                ? $"{FetchState.Message(held.Status)}. Showing the forecast from {FetchState.Freshness(held.At, now).ToLowerInvariant()}."
                : $"Updated {FetchState.Freshness(held.At, now).ToLowerInvariant()}");
    }

    /// <summary>The coldest low and warmest high across the whole forecast, which the bars scale against.</summary>
    private static (double Coldest, double Warmest) Extremes(IReadOnlyList<ForecastDay> days)
    {
        var coldest = double.MaxValue;
        var warmest = double.MinValue;

        foreach (var day in days)
        {
            if (day.Low is { } low && double.IsFinite(low))
            {
                coldest = Math.Min(coldest, low);
                warmest = Math.Max(warmest, low);
            }

            if (day.High is { } high && double.IsFinite(high))
            {
                coldest = Math.Min(coldest, high);
                warmest = Math.Max(warmest, high);
            }
        }

        // A forecast where every day is missing both numbers. The bar then spans the row, which is
        // what RangeBar returns for a zero span anyway.
        return coldest <= warmest ? (coldest, warmest) : (0, 1);
    }

    private FrameworkElement BuildRow(ForecastDay day, DateOnly today, double coldest, double warmest)
    {
        var label = Fluent.Text(FluentType.Body);
        label.Text = WeatherReport.DayLabel(day.Day, today);
        label.MinWidth = 44;

        // Today is the row the reader is most likely to be after, so it carries the one piece of
        // emphasis in the list: the semibold step. Not a colour, and not a filled background —
        // either of those would make it read as selected rather than as current.
        if (day.Day == today)
        {
            label.Apply(FluentType.BodyStrong);
        }

        var icon = Fluent.Icon(LookGlyph(WeatherReport.Look(day.Code)), "TextFillColorSecondaryBrush");
        icon.Margin = new Thickness(Fluent.Space3, 0, Fluent.Space3, 0);

        var low = Number(WeatherReport.Degrees(day.Low), "TextFillColorSecondaryBrush");
        var high = Number(WeatherReport.Degrees(day.High), "TextFillColorPrimaryBrush");

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        SetColumn(label, 0);
        SetColumn(icon, 1);
        SetColumn(low, 3);
        SetColumn(high, 4);
        grid.Children.Add(label);
        grid.Children.Add(high);

        // [S-658] A narrow tile keeps the day and its high, which is the forecast; the icon and the
        // low go first. With all four in 88px the numbers were pushed off the card entirely and
        // the row was a day name and a cloud.
        if (!_narrow)
        {
            grid.Children.Add(icon);
            grid.Children.Add(low);
        }

        if (_showBar && day.Low is { } lowValue && day.High is { } highValue)
        {
            var bar = BuildBar(lowValue, highValue, coldest, warmest);
            SetColumn(bar, 2);
            grid.Children.Add(bar);
        }

        if (_showPrecipitation && _config.ShowPrecipitation)
        {
            var rain = Fluent.Text(FluentType.Caption, "AccentTextFillColorPrimaryBrush");
            rain.Text = WeatherReport.Precipitation(day.Precipitation);
            rain.TextAlignment = TextAlignment.Right;
            rain.MinWidth = 52;
            rain.Margin = new Thickness(Fluent.Space2, 0, 0, 0);

            SetColumn(rain, 5);
            grid.Children.Add(rain);
        }

        var row = Fluent.Row(grid, _compact);
        _hovers.Add(Fluent.Hover(row));

        ToolTipService.SetToolTip(row, $"{Formatting.ShortDate(day.Day)} · {WeatherReport.Describe(day.Code)}");
        return row;
    }

    /// <summary>
    /// The day's range against the week's.
    /// <para>
    /// Three star columns whose weights <i>are</i> the three fractions, so the bar positions itself
    /// at layout time and needs no measurement, no <c>ActualWidth</c> read and no second pass. A
    /// version that computed pixel margins had to be redone on every resize and was a frame behind
    /// after each one.
    /// </para>
    /// </summary>
    private FrameworkElement BuildBar(double low, double high, double coldest, double warmest)
    {
        var (start, end) = WeatherReport.RangeBar(low, high, coldest, warmest);

        var track = new Border
        {
            Height = BarHeight,
            Background = _trackBrush,
            CornerRadius = new CornerRadius(BarHeight / 2),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var fill = new Border
        {
            Height = BarHeight,
            Background = _barBrush,
            CornerRadius = new CornerRadius(BarHeight / 2),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var host = new Grid
        {
            Margin = new Thickness(0, 0, Fluent.Space3, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(start, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(Math.Max(end - start, 0.02), GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(Math.Max(1 - end, 0), GridUnitType.Star) },
            },
        };

        SetColumnSpan(track, 3);
        SetColumn(fill, 1);
        host.Children.Add(track);
        host.Children.Add(fill);
        return host;
    }

    private static TextBlock Number(string text, string colorKey)
    {
        var block = Fluent.Text(FluentType.Body, colorKey);
        block.Text = text;
        block.TextAlignment = TextAlignment.Right;
        block.MinWidth = 34;
        block.Margin = new Thickness(Fluent.Space2, 0, 0, 0);

        // Tabular figures, so the column of temperatures lines up down the list instead of
        // jittering by a pixel per row.
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        return block;
    }

    /// <summary>The same glyph the current-conditions widget draws for a condition. [S-652]</summary>
    private static string LookGlyph(WeatherLook look) => WeatherNowView.LookGlyph(look);

    /// <summary>
    /// Drop the rows and, with them, their hover handlers: a handler left hooked keeps the row it
    /// lights alive and re-lights it after the list has been redrawn.
    /// </summary>
    private void ClearRows()
    {
        foreach (var hover in _hovers)
        {
            hover.Dispose();
        }

        _hovers.Clear();
        _list.Children.Clear();
    }

    public void Detach()
    {
        _detached = true;

        SizeChanged -= OnSizeChanged;
        ClearRows();

        _subscription?.Dispose();
        _subscription = null;
        _subscribedInterval = TimeSpan.Zero;

        _inFlight?.Cancel();
        _inFlight = null;

        _ticker = null;
    }
    /// <summary>
    /// Tell the card what this widget is about. The catalogue can only say "Weather" or
    /// "Folder"; the widget knows which place, which folder, which ticker — and on a desktop
    /// holding three of them, that is the only thing telling them apart.
    /// </summary>
    private void PublishTitle()
    {
        var title = string.IsNullOrWhiteSpace(_config.Place) ? _config.LocationName : _config.Place;
        _surface?.SetHeaderTitle(string.IsNullOrWhiteSpace(title) ? null : title);
    }

}
