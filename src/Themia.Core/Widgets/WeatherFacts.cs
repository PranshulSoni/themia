using System.Globalization;
using Themia.Core.Model;

namespace Themia.Core.Widgets;

/// <summary>What one secondary reading on the current-conditions widget is; the view maps it to a glyph.</summary>
public enum WeatherFactKind
{
    FeelsLike,
    Humidity,
    Wind,
    Precipitation,
    Pressure,
    Uv,
    CloudCover,
    Gusts,
    Sunrise,
    Sunset,
}

/// <summary>One labelled reading: "Humidity" / "63%".</summary>
public readonly record struct WeatherFact(WeatherFactKind Kind, string Label, string Value);

/// <summary>
/// [S-722] The current-conditions widget's secondary readings, in the order they earn their place.
/// <para>
/// The original's three — feels-like, humidity, wind — come first, so every size that shows any
/// detail shows those; the extras the larger tiles add follow in the order a reader is most likely
/// to want them. A reading the service did not send is left out rather than drawn as "--": a
/// tile has a fixed number of cells, and a blank one is a cell a real reading could have used.
/// </para>
/// </summary>
public static class WeatherFacts
{
    /// <summary>How many facts the widget can ever show; the cell grids size against it.</summary>
    public const int Max = 10;

    public static IReadOnlyList<WeatherFact> For(CurrentWeather reading, TemperatureUnit unit, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(culture);

        var facts = new List<WeatherFact>(Max);

        void Add(WeatherFactKind kind, string label, string value)
        {
            if (value.Length > 0 && value != "--")
            {
                facts.Add(new WeatherFact(kind, label, value));
            }
        }

        var today = reading.Today;

        Add(WeatherFactKind.FeelsLike, "Feels like", WeatherReport.Temperature(reading.FeelsLike, unit));
        Add(WeatherFactKind.Humidity, "Humidity", WeatherReport.Humidity(reading.Humidity));
        Add(WeatherFactKind.Wind, "Wind", WeatherReport.WindFrom(reading.WindKilometresPerHour, reading.WindDirection));

        // Today's chance reads better than the last hour's millimetres on a dry afternoon; the
        // amount wins once it is actually raining.
        var precipitation = reading.Precipitation is > 0
            ? WeatherReport.Precipitation(reading.Precipitation)
            : WeatherReport.Chance(today?.PrecipitationChance);
        Add(WeatherFactKind.Precipitation, "Precipitation", precipitation);

        Add(WeatherFactKind.Pressure, "Pressure", WeatherReport.Pressure(reading.Pressure));
        Add(WeatherFactKind.Uv, "UV index", WeatherReport.Uv(reading.UvIndex ?? today?.UvMax));
        Add(WeatherFactKind.CloudCover, "Cloud cover", WeatherReport.Cover(reading.CloudCover));
        Add(WeatherFactKind.Gusts, "Gusts", WeatherReport.Wind(reading.WindGusts));

        if (today is not null)
        {
            Add(WeatherFactKind.Sunrise, "Sunrise", today.Sunrise is null ? string.Empty : WeatherReport.Clock(today.Sunrise, culture));
            Add(WeatherFactKind.Sunset, "Sunset", today.Sunset is null ? string.Empty : WeatherReport.Clock(today.Sunset, culture));
        }

        return facts;
    }
}
