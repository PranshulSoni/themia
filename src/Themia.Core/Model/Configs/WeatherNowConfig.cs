using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Which scale temperatures are asked for and shown in.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TemperatureUnit>))]
public enum TemperatureUnit
{
    Celsius = 0,
    Fahrenheit = 1,
}

/// <summary>
/// Settings shared by the two weather widgets: where, in what unit, and how often.
/// <para>
/// The place is stored as <b>coordinates plus the name they resolved to</b>, not as the text the
/// user typed. Re-geocoding a free-text place on every refresh would mean two requests instead of
/// one, and a widget whose location silently moves when the geocoder changes its mind about
/// "Springfield". The typed query is kept only so the settings box can show what was searched for.
/// </para>
/// </summary>
public abstract class WeatherConfig : WidgetConfig
{
    /// <summary>What the user last typed into the place box. Display only; never re-sent on a tick.</summary>
    public string Place { get; set; } = string.Empty;

    /// <summary>Degrees north, or null when no location has been chosen. Zero is a real latitude.</summary>
    public double? Latitude { get; set; }

    /// <summary>Degrees east, or null when no location has been chosen. Zero is a real longitude.</summary>
    public double? Longitude { get; set; }

    /// <summary>The name the geocoder gave the chosen point, e.g. "Zürich, Zurich, Switzerland".</summary>
    public string LocationName { get; set; } = string.Empty;

    public TemperatureUnit Unit { get; set; } = TemperatureUnit.Celsius;

    /// <summary>How often to ask open-meteo, in minutes.</summary>
    public int RefreshMinutes { get; set; } = 15;

    /// <summary>
    /// Whether a location has actually been chosen.
    /// <para>
    /// A null test, not a truthiness test: latitude 0 is the equator and longitude 0 is the prime
    /// meridian, and a widget pointed at Accra must not read as unconfigured.
    /// </para>
    /// </summary>
    [JsonIgnore]
    public bool HasLocation => Latitude is { } lat
        && Longitude is { } lon
        && double.IsFinite(lat)
        && double.IsFinite(lon);

    /// <summary>
    /// The cadence, clamped. Five minutes is the floor: open-meteo publishes on a 15-minute cycle
    /// and is free and unauthenticated, so asking faster gets the same answer at somebody else's
    /// expense.
    /// </summary>
    [JsonIgnore]
    public TimeSpan TickInterval => TimeSpan.FromMinutes(Math.Clamp(RefreshMinutes, 5, 240));

    protected void CopyTo(WeatherConfig other)
    {
        other.Place = Place;
        other.Latitude = Latitude;
        other.Longitude = Longitude;
        other.LocationName = LocationName;
        other.Unit = Unit;
        other.RefreshMinutes = RefreshMinutes;
    }
}

/// <summary>Conditions right now, from open-meteo's <c>current</c> block.</summary>
public sealed class WeatherNowConfig : WeatherConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.WeatherNow;

    /// <summary>Show the feels-like, humidity and wind line under the temperature.</summary>
    public bool ShowDetails { get; set; } = true;

    public override WidgetConfig Clone()
    {
        var copy = new WeatherNowConfig { ShowDetails = ShowDetails };
        CopyTo(copy);
        return copy;
    }
}
