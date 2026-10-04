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
/// The weather where the user says they are, from open-meteo.
/// <para>
/// A readout, in the shape <see cref="MeterView"/> uses for a system meter: a header, one large
/// number stepped off the Fluent ramp, and a quiet line of supporting detail. Not the web app's
/// tiled card of boxed statistics, which is a dashboard pattern and reads as one.
/// </para>
/// <para>
/// The header carries how old the reading is, always, in every state. That line is the reason this
/// widget can be trusted: every other thing on it — a temperature, a humidity — looks exactly as
/// authoritative four days stale as four seconds fresh, and a weather tile silently showing
/// Tuesday's conditions on Friday is the specific way this kind of widget misleads somebody.
/// </para>
/// </summary>
internal sealed partial class WeatherNowView : Grid, IWidgetView, IWidgetHeaderContent, IWidgetSurfaceAware
{
    private const string Scope = "weather";

    /// <summary>Under this height the detail line is dropped: the temperature is what the tile is for.</summary>
    private const double DetailHidesBelow = 168;

    /// <summary>The widest reading this widget prints, in characters — "-18°F". Stable, so the step is.</summary>
    private const double ReadingCharacters = 5;

    private readonly TextBlock _freshness = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly TextBlock _temperature = Fluent.Text(FluentType.Subtitle);
    private readonly TextBlock _condition = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
    private readonly TextBlock _detail = Fluent.Text(FluentType.Caption, "TextFillColorSecondaryBrush");
    private readonly FontIcon _look = Fluent.Icon(Glyphs.Weather, "TextFillColorSecondaryBrush", Fluent.Space6);
    private readonly Grid _readout = new();
    private readonly Border _detailHost;
    private readonly StackPanel _emptyState;
    private readonly TextBlock _emptyMessage = new();
    private readonly FontIcon _emptyIcon;

    // Looked up once. Swapping a brush per tick is a dictionary hit and a possible log line for a
    // colour that changes twice a day at most.
    private readonly Brush _freshBrush = FluentLayout.Themed("TextFillColorTertiaryBrush", Colors.Gray);
    private readonly Brush _staleBrush = FluentLayout.Themed("SystemFillColorCautionBrush", Colors.Goldenrod);

    private readonly RefreshSchedule _schedule = new(Scope, "open-meteo");

    private Ticker? _ticker;
    private WeatherNowConfig _config = new();

    /// <summary>
    /// [S-13a] A copy of the settings this view last acted on, which is what a change is measured
    /// against. Not <c>_config</c>: the settings page edits the live config object in place, so by
    /// the time a change arrives the "previous" config already holds the new values, and comparing
    /// the two found no difference — a new address or place stayed unfetched until the next tick.
    /// </summary>
    private WeatherNowConfig _applied = new();

    /// <summary>The card this view draws on, which owns the header it names.</summary>
    private IWidgetSurface? _surface;
    private IDisposable? _subscription;
    private TimeSpan _subscribedInterval = TimeSpan.Zero;
    private CancellationTokenSource? _inFlight;

    /// <summary>
    /// Which request is current. A reply carrying an older number is for a place the widget has
    /// stopped pointing at, and a slow answer for Oslo must not overwrite Zurich.
    /// </summary>
    private int _generation;

    private Fetched<CurrentWeather>? _reading;
    private TypeStep _heroStep = FluentType.Subtitle;
    private bool _detached;

    public WeatherNowView()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _look.Margin = new Thickness(0, 0, Fluent.Space3, 0);
        _temperature.VerticalAlignment = VerticalAlignment.Center;

        _condition.VerticalAlignment = VerticalAlignment.Bottom;
        _condition.Margin = new Thickness(Fluent.Space2, 0, 0, Fluent.Space1);

        // A Grid, not a horizontal StackPanel: a StackPanel measures its children with unbounded
        // width, so the condition never learns how much room it has and its trimming never fires —
        // "Thunderstorm with hail" then runs off the edge of a narrow card mid-word.
        _readout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _readout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _readout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // The condition's own line, for a tile too narrow to keep it beside the number ([S-656]).
        _readout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _readout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        SetRowSpan(_look, 2);
        _look.VerticalAlignment = VerticalAlignment.Center;
        _readout.Margin = WidgetLayout.SideInset;
        _readout.VerticalAlignment = VerticalAlignment.Center;

        SetColumn(_look, 0);
        SetColumn(_temperature, 1);
        SetColumn(_condition, 2);
        _readout.Children.Add(_look);
        _readout.Children.Add(_temperature);
        _readout.Children.Add(_condition);

        _detailHost = new Border
        {
            Child = _detail,
            Padding = new Thickness(Fluent.Space4, 0, Fluent.Space4, Fluent.Space4),
        };

        SetRow(_detailHost, 1);

        _emptyState = Fluent.EmptyState(Glyphs.Weather, _emptyMessage, out _emptyIcon);
        _emptyState.Visibility = Visibility.Collapsed;

        Children.Add(_readout);
        Children.Add(_detailHost);
        Children.Add(_emptyState);

        SizeChanged += OnSizeChanged;
    }

    /// <summary>
    /// The age of the reading, in the card's header. Built in the constructor because the card
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
        var next = config as WeatherNowConfig ?? new WeatherNowConfig();
        var previous = _applied;
        _config = next;
        _applied = (WeatherNowConfig)next.Clone();
        PublishTitle();

        var placeChanged = previous.Latitude != next.Latitude
            || previous.Longitude != next.Longitude
            || previous.Unit != next.Unit;

        if (placeChanged)
        {
            // A different place, or a different scale, is a different reading. The one in hand is
            // about somewhere else and must not linger on screen under the new heading.
            _reading = null;

            // And the user has just changed something, which is an explicit "try again now": a
            // back-off earned by the previous setting has nothing to do with this one.
            _schedule.Reset();
        }

        // Subscribing fires a tick of its own, which reloads with the new config already in place.
        if (Resubscribe())
        {
            return;
        }

        if (placeChanged)
        {
            Reload();
        }
        else
        {
            Render();
        }
    }

    /// <summary>
    /// Rebuild the ticker subscription only when the cadence actually changed, and say whether it
    /// did — a new subscription fires immediately, so the caller has nothing left to do.
    /// </summary>
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

    /// <summary>
    /// A tick. It redraws unconditionally — the freshness line has to keep counting up even while
    /// the widget is backed off — and fetches only when it is allowed to.
    /// </summary>
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

        // Whatever the shared cache already holds for this exact place, drawn before the request
        // goes out. A second weather widget on the same city, or this one after a relayout, shows
        // a temperature immediately instead of flashing "Loading".
        _reading ??= WeatherService.PeekCurrent(latitude, longitude, unit);

        _inFlight?.Cancel();
        var cts = new CancellationTokenSource();
        _inFlight = cts;
        var generation = ++_generation;

        Render();
        _ = LoadAsync(latitude, longitude, unit, generation, cts);
    }

    private async Task LoadAsync(
        double latitude,
        double longitude,
        TemperatureUnit unit,
        int generation,
        CancellationTokenSource cts)
    {
        try
        {
            var result = await WeatherService
                .CurrentAsync(latitude, longitude, unit, _config.TickInterval, cts.Token)
                .ConfigureAwait(true);

            // A late reply for a place the widget has since left. Dropped rather than drawn.
            if (_detached || generation != _generation)
            {
                return;
            }

            _schedule.Record(result.Status, _config.TickInterval, result.Detail, DateTimeOffset.Now);

            if (result.Value is not null)
            {
                _reading = result;
            }
            else if (_reading is { } held)
            {
                // Keep what is on screen and annotate it with what has just failed. The reading
                // stays true as of its own timestamp; only the attempt to improve on it failed.
                _reading = held.Stale(result.Status, result.Detail);
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
        UpdateDetailVisibility();
        ApplyTypeScale();
    }

    /// <summary>
    /// Step the temperature to the tile: Subtitle on a small one, up to Display on a large one.
    /// <para>
    /// A ramp step chosen for the box, not a font size computed as a fraction of it. Fitted against
    /// the widest string this widget can print rather than the one on screen, so "8°C" and "-18°C"
    /// do not pick different steps and make the number jump size between two readings.
    /// </para>
    /// </summary>
    private void ApplyTypeScale()
    {
        // [S-656] Sized against the room the reading really has — the tile less its insets and
        // the detail line — rather than against the readout's own height, which is only ever the
        // height of the step already chosen: a tall tile kept the first, smallest step for good.
        var width = ActualWidth - (Fluent.Space4 * 2);
        var height = ActualHeight - DetailBlock();

        if (!(width > 0) || !(height > 0))
        {
            return;
        }

        // The icon is the first thing to go: on a tile that cannot hold it beside even the
        // smallest reading, "19°C" matters and the cloud beside it does not.
        var showIcon = width >= IconFor(FluentType.Subtitle) + Fluent.Space3 + FluentType.EstimateWidth(FluentType.Subtitle, ReadingCharacters);
        _look.Visibility = showIcon ? Visibility.Visible : Visibility.Collapsed;

        TypeStep Pick(double h)
        {
            var first = FluentType.Fit(width, h, ReadingCharacters, FluentType.Subtitle, FluentType.Display);
            var room = showIcon ? width - IconFor(first) - Fluent.Space3 : width;
            return FluentType.Fit(room, h, ReadingCharacters, FluentType.Subtitle, FluentType.Display);
        }

        var step = Pick(height);
        var readingRoom = showIcon ? width - IconFor(step) - Fluent.Space3 : width;

        // The condition moves under the temperature when it would be trimmed beside it ("Ove…"),
        // and on a tile with no room for it even there it is left to its ellipsis.
        var stacked = _condition.Text.Length > 0 && !ReadoutLayout.CaptionFitsBeside(
            readingRoom,
            step,
            ReadingCharacters,
            FluentType.Body,
            _condition.Text.Length,
            Fluent.Space2);

        if (stacked)
        {
            step = Pick(height - FluentType.Body.LineHeight - Fluent.Space1);
        }

        SetRow(_condition, stacked ? 1 : 0);
        SetColumn(_condition, stacked ? 1 : 2);
        SetColumnSpan(_condition, stacked ? 2 : 1);
        _condition.VerticalAlignment = stacked ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        _condition.Margin = stacked ? new Thickness(0) : new Thickness(Fluent.Space2, 0, 0, Fluent.Space1);

        if (step == _heroStep)
        {
            return;
        }

        _heroStep = step;
        _temperature.Apply(step);

        // The icon grows with the number it sits beside; a 16px glyph next to a 68px reading reads
        // as a bullet point rather than as the condition.
        _look.FontSize = IconFor(step);
    }

    private static double IconFor(TypeStep step) => Math.Clamp(step.FontSize * 0.8, Fluent.Space6, 72);

    /// <summary>What the detail line under the reading takes, when it is showing.</summary>
    private double DetailBlock()
        => _detailHost.Visibility == Visibility.Visible
            ? FluentType.Caption.LineHeight + Fluent.Space4 + Fluent.Space2
            : Fluent.Space2;

    private void UpdateDetailVisibility()
    {
        var wanted = _config.ShowDetails
            && ActualHeight >= DetailHidesBelow
            && _reading?.Value is not null;

        _detailHost.Visibility = wanted ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Draw what is in hand. Every state with no reading says which state it is: a blank weather
    /// tile is indistinguishable from a broken one, and each of these needs a different thing from
    /// the person looking at it.
    /// </summary>
    private void Render()
    {
        if (_detached)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var held = _reading;

        if (held?.Value is not { } reading)
        {
            var status = _schedule.Status;

            _emptyMessage.Text = status == FetchStatus.NotConfigured
                ? "Choose a place in settings"
                : FetchState.Message(status);

            _emptyIcon.Glyph = Glyphs.Weather;
            _emptyState.Visibility = Visibility.Visible;
            _readout.Visibility = Visibility.Collapsed;
            _detailHost.Visibility = Visibility.Collapsed;
            _freshness.Text = string.Empty;
            return;
        }

        _emptyState.Visibility = Visibility.Collapsed;
        _readout.Visibility = Visibility.Visible;

        _temperature.Text = WeatherReport.Temperature(reading.Temperature, _config.Unit);
        _condition.Text = WeatherReport.Describe(reading.Code);
        _look.Glyph = LookGlyph(WeatherReport.Look(reading.Code));

        _detail.Text = string.Join(
            "   ",
            $"Feels {WeatherReport.Temperature(reading.FeelsLike, _config.Unit)}",
            $"Humidity {WeatherReport.Humidity(reading.Humidity)}",
            $"Wind {WeatherReport.Wind(reading.WindKilometresPerHour)}");

        // The age of the reading, and — when the last attempt to refresh it failed — the reason,
        // in the caution colour. Said in the tooltip as well as in the colour, because a state
        // carried only by a colour is a state some readers never get.
        var failed = FetchState.IsFailure(held.Status);
        var stale = failed || FetchState.IsStale(held.At, now, _config.TickInterval);

        _freshness.Text = FetchState.Freshness(held.At, now);
        _freshness.Foreground = stale ? _staleBrush : _freshBrush;

        ToolTipService.SetToolTip(
            _freshness,
            failed
                ? $"{FetchState.Message(held.Status)}. Showing the reading from {FetchState.Freshness(held.At, now).ToLowerInvariant()}."
                : $"Updated {FetchState.Freshness(held.At, now).ToLowerInvariant()}");

        // Detail first: whether it shows decides how much height the reading has.
        UpdateDetailVisibility();
        ApplyTypeScale();
    }

    /// <summary>
    /// The Segoe glyph for a condition. Shared with the forecast, so a day reads the same in both.
    /// <para>
    /// [S-652] One glyph per kind of weather. This used to fold rain, snow and thunder into the
    /// thermometer — the forecast widget's own icon — because the weather code points in
    /// <c>Glyphs</c> were not in the font and drew as boxes; they are fixed, so each condition has
    /// its own shape again.
    /// </para>
    /// </summary>
    internal static string LookGlyph(WeatherLook look) => look switch
    {
        WeatherLook.Clear => Glyphs.Sun,
        WeatherLook.PartlyCloudy or WeatherLook.Overcast => Glyphs.Cloudy,
        WeatherLook.Fog => Glyphs.Fog,
        WeatherLook.Drizzle or WeatherLook.Rain => Glyphs.Rain,
        WeatherLook.Snow => Glyphs.Snow,
        WeatherLook.Thunderstorm => Glyphs.Thunderstorm,
        _ => Glyphs.Weather,
    };

    public void Detach()
    {
        _detached = true;

        SizeChanged -= OnSizeChanged;

        _subscription?.Dispose();
        _subscription = null;
        _subscribedInterval = TimeSpan.Zero;

        // Cancel but do not dispose: the in-flight load owns its source and frees it on the way
        // out, so there is exactly one owner however many times this is called.
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
