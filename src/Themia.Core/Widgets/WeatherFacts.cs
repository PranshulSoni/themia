using Themia.Core.Localization;
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

    public static IReadOnlyList<WeatherFact> For(CurrentWeather reading, TemperatureUnit unit)
        => For(Strings.Current, reading, unit);

    /// <summary>The readings, labelled and written in <paramref name="locale"/>.</summary>
    public static IReadOnlyList<WeatherFact> For(Locale locale, CurrentWeather reading, TemperatureUnit unit)
    {
        ArgumentNullException.ThrowIfNull(reading);

        var facts = new List<WeatherFact>(Max);

        void Add(WeatherFactKind kind, Str label, string value)
        {
            if (value.Length > 0 && value != "--")
            {
                facts.Add(new WeatherFact(kind, Strings.Get(locale, label), value));
            }
        }

        var today = reading.Today;

        Add(WeatherFactKind.FeelsLike, Str.Weather_FeelsLike, WeatherReport.Temperature(reading.FeelsLike, unit));
        Add(WeatherFactKind.Humidity, Str.Weather_Humidity, WeatherReport.Humidity(locale, reading.Humidity));
        Add(WeatherFactKind.Wind, Str.Weather_Wind, WeatherReport.WindFrom(locale, reading.WindKilometresPerHour, reading.WindDirection));

        // Today's chance reads better than the last hour's millimetres on a dry afternoon; the
        // amount wins once it is actually raining.
        var precipitation = reading.Precipitation is > 0
            ? WeatherReport.Precipitation(locale, reading.Precipitation)
            : WeatherReport.Chance(locale, today?.PrecipitationChance);
        Add(WeatherFactKind.Precipitation, Str.WidgetsC_Weather_Fact_Precipitation, precipitation);

        Add(WeatherFactKind.Pressure, Str.WidgetsC_Weather_Fact_Pressure, WeatherReport.Pressure(reading.Pressure));
        Add(WeatherFactKind.Uv, Str.WidgetsC_Weather_Fact_UvIndex, WeatherReport.Uv(reading.UvIndex ?? today?.UvMax));
        Add(WeatherFactKind.CloudCover, Str.WidgetsC_Weather_Fact_CloudCover, WeatherReport.Cover(locale, reading.CloudCover));
        Add(WeatherFactKind.Gusts, Str.WidgetsC_Weather_Fact_Gusts, WeatherReport.Wind(reading.WindGusts));

        if (today is not null)
        {
            Add(WeatherFactKind.Sunrise, Str.WidgetUi_Sunrise, today.Sunrise is null ? string.Empty : WeatherReport.Clock(locale, today.Sunrise));
            Add(WeatherFactKind.Sunset, Str.WidgetUi_Sunset, today.Sunset is null ? string.Empty : WeatherReport.Clock(locale, today.Sunset));
        }

        return facts;
    }
}
