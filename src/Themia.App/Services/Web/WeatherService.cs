using System.Globalization;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Services.Web;

/// <summary>Conditions right now at one point, already validated.</summary>
/// <param name="Temperature">In the unit that was asked for, or null if the service omitted it.</param>
/// <param name="FeelsLike">Apparent temperature, same unit.</param>
/// <param name="Humidity">Relative humidity, 0 to 100.</param>
/// <param name="WindKilometresPerHour">Wind speed at 10m.</param>
/// <param name="Code">WMO 4677 condition code, or null.</param>
internal sealed record CurrentWeather(
    double? Temperature,
    double? FeelsLike,
    double? Humidity,
    double? WindKilometresPerHour,
    int? Code);

/// <summary>One day of a forecast.</summary>
internal sealed record ForecastDay(DateOnly Day, int? Code, double? Low, double? High, double? Precipitation);

/// <summary>A forecast, in day order.</summary>
internal sealed record Forecast(IReadOnlyList<ForecastDay> Days);

/// <summary>A place the geocoder matched, with the coordinates to store for it.</summary>
internal sealed record GeocodedPlace(string Name, double Latitude, double Longitude);

/// <summary>
/// open-meteo, which is the reason the two weather widgets could be ported at all: it is free,
/// unauthenticated, and asks for nothing but a coordinate pair.
/// <para>
/// <b>No API key, no account, no token.</b> That is a property of the endpoint and a constraint on
/// this file: if it ever stops being true, the widget stops working and says so, and nothing here
/// quietly substitutes a service that wants a credential.
/// </para>
/// </summary>
internal static class WeatherService
{
    private const string Scope = "weather";

    /// <summary>Conditions now. Documented at <c>open-meteo.com/en/docs</c>.</summary>
    private const string CurrentEndpoint = "https://api.open-meteo.com/v1/forecast";

    /// <summary>Place-name lookup. The same service, a different host.</summary>
    private const string GeocodeEndpoint = "https://geocoding-api.open-meteo.com/v1/search";

    private static readonly WebCache<CurrentWeather> CurrentCache = new(Scope);
    private static readonly WebCache<Forecast> ForecastCache = new(Scope);

    /// <summary>
    /// Conditions at a point. Widgets on the same point, in the same unit, share one request — the
    /// cache key is exactly the set of things that change the answer.
    /// </summary>
    public static Task<Fetched<CurrentWeather>> CurrentAsync(
        double latitude,
        double longitude,
        TemperatureUnit unit,
        TimeSpan freshFor,
        CancellationToken token)
    {
        var key = $"now|{Coordinate(latitude)},{Coordinate(longitude)}|{unit}";

        return CurrentCache.GetAsync(key, freshFor, ct => LoadCurrentAsync(latitude, longitude, unit, ct), token);
    }

    /// <summary>The cached conditions for a point regardless of age, for drawing before a fetch returns.</summary>
    public static Fetched<CurrentWeather>? PeekCurrent(double latitude, double longitude, TemperatureUnit unit)
        => CurrentCache.Peek($"now|{Coordinate(latitude)},{Coordinate(longitude)}|{unit}");

    public static Task<Fetched<Forecast>> ForecastAsync(
        double latitude,
        double longitude,
        TemperatureUnit unit,
        int days,
        TimeSpan freshFor,
        CancellationToken token)
    {
        var key = ForecastKey(latitude, longitude, unit, days);

        return ForecastCache.GetAsync(key, freshFor, ct => LoadForecastAsync(latitude, longitude, unit, days, ct), token);
    }

    public static Fetched<Forecast>? PeekForecast(double latitude, double longitude, TemperatureUnit unit, int days)
        => ForecastCache.Peek(ForecastKey(latitude, longitude, unit, days));

    private static string ForecastKey(double latitude, double longitude, TemperatureUnit unit, int days)
        => $"days|{Coordinate(latitude)},{Coordinate(longitude)}|{unit}|{days}";

    private static async Task<Fetched<CurrentWeather>> LoadCurrentAsync(
        double latitude,
        double longitude,
        TemperatureUnit unit,
        CancellationToken token)
    {
        var uri = new Uri(
            $"{CurrentEndpoint}?latitude={Coordinate(latitude)}&longitude={Coordinate(longitude)}"
            + "&current=temperature_2m,apparent_temperature,relative_humidity_2m,wind_speed_10m,weather_code"
            + "&wind_speed_unit=kmh"
            + $"&temperature_unit={UnitParameter(unit)}");

        var (status, reply, detail) = await WebFetch
            .GetJsonAsync(uri, WebJsonContext.Default.OpenMeteoReply, token)
            .ConfigureAwait(false);

        var now = DateTimeOffset.Now;

        if (status != FetchStatus.Ok || reply is null)
        {
            return Fetched<CurrentWeather>.Failed(status, detail, now);
        }

        // open-meteo answers a bad request with 400 and a reason, but also carries one on some 200s.
        if (!string.IsNullOrEmpty(reply.Reason))
        {
            return Fetched<CurrentWeather>.Failed(FetchStatus.BadResponse, reply.Reason, now);
        }

        var current = reply.Current;

        if (current is null)
        {
            return Fetched<CurrentWeather>.Failed(FetchStatus.Empty, "no current block", now);
        }

        var reading = new CurrentWeather(
            Sane(current.Temperature, -120, 100),
            Sane(current.ApparentTemperature, -150, 130),
            Sane(current.Humidity, 0, 100),
            Sane(current.WindSpeed, 0, 500),
            Code(current.WeatherCode));

        // A "current" block with no temperature in it is not a reading, whatever else it carries.
        return reading.Temperature is null
            ? Fetched<CurrentWeather>.Failed(FetchStatus.Empty, "no temperature in the reply", now)
            : new Fetched<CurrentWeather>(FetchStatus.Ok, reading, null, now);
    }

    private static async Task<Fetched<Forecast>> LoadForecastAsync(
        double latitude,
        double longitude,
        TemperatureUnit unit,
        int days,
        CancellationToken token)
    {
        var wanted = Math.Clamp(days, 1, 14);

        var uri = new Uri(
            $"{CurrentEndpoint}?latitude={Coordinate(latitude)}&longitude={Coordinate(longitude)}"
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum"
            + $"&temperature_unit={UnitParameter(unit)}"
            // The days come back in the location's own calendar, which is what makes "Today" mean
            // today there rather than today here.
            + "&timezone=auto"
            + $"&forecast_days={wanted.ToString(CultureInfo.InvariantCulture)}");

        var (status, reply, detail) = await WebFetch
            .GetJsonAsync(uri, WebJsonContext.Default.OpenMeteoReply, token)
            .ConfigureAwait(false);

        var now = DateTimeOffset.Now;

        if (status != FetchStatus.Ok || reply is null)
        {
            return Fetched<Forecast>.Failed(status, detail, now);
        }

        if (!string.IsNullOrEmpty(reply.Reason))
        {
            return Fetched<Forecast>.Failed(FetchStatus.BadResponse, reply.Reason, now);
        }

        var daily = reply.Daily;
        var dates = daily?.Time;

        if (daily is null || dates is null || dates.Count == 0)
        {
            return Fetched<Forecast>.Failed(FetchStatus.Empty, "no daily block", now);
        }

        var parsed = new List<ForecastDay>(dates.Count);

        for (var i = 0; i < dates.Count && parsed.Count < wanted; i++)
        {
            // The five arrays are supposed to be the same length. Indexed defensively anyway,
            // because "supposed to be" is not a guarantee and an off-by-one here is an exception
            // on the way to a render.
            if (!DateOnly.TryParseExact(dates[i] ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                continue;
            }

            parsed.Add(new ForecastDay(
                day,
                Code(At(daily.WeatherCode, i)),
                Sane(At(daily.Low, i), -120, 100),
                Sane(At(daily.High, i), -120, 100),
                Sane(At(daily.Precipitation, i), 0, 2000)));
        }

        return parsed.Count == 0
            ? Fetched<Forecast>.Failed(FetchStatus.Empty, "no usable days in the reply", now)
            : new Fetched<Forecast>(FetchStatus.Ok, new Forecast(parsed), null, now);
    }

    /// <summary>
    /// Look a place up by name. Used only from the settings page, on a keystroke the user made, so
    /// it is not cached and not on a timer.
    /// </summary>
    public static async Task<(FetchStatus Status, IReadOnlyList<GeocodedPlace> Places, string? Detail)> FindAsync(
        string query,
        CancellationToken token)
    {
        if (!WebInput.IsPlausiblePlace(query))
        {
            return (FetchStatus.Refused, [], "not a place name");
        }

        var uri = new Uri(
            $"{GeocodeEndpoint}?name={Uri.EscapeDataString(query.Trim())}&count=6&language=en&format=json");

        var (status, reply, detail) = await WebFetch
            .GetJsonAsync(uri, WebJsonContext.Default.GeocodeReply, token)
            .ConfigureAwait(false);

        if (status != FetchStatus.Ok || reply is null)
        {
            return (status, [], detail);
        }

        var places = new List<GeocodedPlace>(6);

        foreach (var hit in reply.Results ?? [])
        {
            // A result with no name or no finite coordinates is dropped rather than drawn: it
            // cannot be shown and it cannot be stored, so offering it would only produce a widget
            // pointed at nowhere.
            if (string.IsNullOrWhiteSpace(hit.Name) || !WebInput.AreCoordinates(hit.Latitude, hit.Longitude))
            {
                continue;
            }

            places.Add(new GeocodedPlace(Describe(hit), hit.Latitude!.Value, hit.Longitude!.Value));
        }

        // The geocoder matching nothing is an answer, not a failure, and the two read very
        // differently to somebody who has just mistyped a city.
        return places.Count == 0
            ? (FetchStatus.Empty, places, null)
            : (FetchStatus.Ok, places, null);
    }

    /// <summary>"Zurich, Canton of Zurich, Switzerland", skipping the parts the service left out.</summary>
    private static string Describe(GeocodeHit hit)
    {
        var parts = new List<string>(3) { hit.Name!.Trim() };

        if (!string.IsNullOrWhiteSpace(hit.Admin1) && !hit.Admin1.Equals(hit.Name, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(hit.Admin1.Trim());
        }

        if (!string.IsNullOrWhiteSpace(hit.Country))
        {
            parts.Add(hit.Country.Trim());
        }

        return string.Join(", ", parts);
    }

    private static string UnitParameter(TemperatureUnit unit)
        => unit == TemperatureUnit.Fahrenheit ? "fahrenheit" : "celsius";

    /// <summary>
    /// A coordinate in the URL, to four decimal places — about eleven metres, which is far finer
    /// than any weather model. Invariant, so a machine that writes decimal commas cannot produce a
    /// URL with a stray separator in it.
    /// </summary>
    private static string Coordinate(double value)
        => Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>A reading, if it is a number and inside a range the physical quantity can occupy.</summary>
    private static double? Sane(double? value, double min, double max)
        => value is { } number && double.IsFinite(number) && number >= min && number <= max ? number : null;

    /// <summary>A WMO code, if it is one. Codes run 0 to 99; anything else is not this vocabulary.</summary>
    private static int? Code(int? value) => value is >= 0 and <= 99 ? value : null;

    private static T? At<T>(List<T?>? list, int index) where T : struct
        => list is not null && index < list.Count ? list[index] : null;
}
