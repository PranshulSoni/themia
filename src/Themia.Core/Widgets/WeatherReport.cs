using System.Globalization;
using Themia.Core.Model;

namespace Themia.Core.Widgets;

/// <summary>
/// The broad shape of a sky, as a widget needs to draw it.
/// <para>
/// Core does not know what a Segoe code point is, so it names the <i>look</i> and the view picks the
/// glyph. That keeps the WMO table — which is the part with ninety-nine numbered cases and is worth
/// testing — out of a XAML file and away from an icon font.
/// </para>
/// </summary>
public enum WeatherLook
{
    /// <summary>No code, or one this table does not know.</summary>
    Unknown = 0,

    Clear,
    PartlyCloudy,
    Overcast,
    Fog,
    Drizzle,
    Rain,
    Snow,
    Thunderstorm,
}

/// <summary>
/// WMO weather codes, and the strings the two weather widgets print.
/// <para>
/// Open-meteo reports conditions as a WMO 4677 code, which is a number between 0 and 99 with gaps.
/// An unknown code is reported as "Code 78" rather than guessed at or blanked: the reader can see
/// that the service said something this app does not have a word for, which is true, and a blank
/// would read as a broken widget.
/// </para>
/// </summary>
public static class WeatherReport
{
    private static readonly Dictionary<int, (WeatherLook Look, string Text)> Codes = new()
    {
        [0] = (WeatherLook.Clear, "Clear sky"),
        [1] = (WeatherLook.Clear, "Mainly clear"),
        [2] = (WeatherLook.PartlyCloudy, "Partly cloudy"),
        [3] = (WeatherLook.Overcast, "Overcast"),
        [45] = (WeatherLook.Fog, "Fog"),
        [48] = (WeatherLook.Fog, "Icy fog"),
        [51] = (WeatherLook.Drizzle, "Light drizzle"),
        [53] = (WeatherLook.Drizzle, "Drizzle"),
        [55] = (WeatherLook.Drizzle, "Heavy drizzle"),
        [56] = (WeatherLook.Drizzle, "Freezing drizzle"),
        [57] = (WeatherLook.Drizzle, "Heavy freezing drizzle"),
        [61] = (WeatherLook.Rain, "Light rain"),
        [63] = (WeatherLook.Rain, "Rain"),
        [65] = (WeatherLook.Rain, "Heavy rain"),
        [66] = (WeatherLook.Rain, "Freezing rain"),
        [67] = (WeatherLook.Rain, "Heavy freezing rain"),
        [71] = (WeatherLook.Snow, "Light snow"),
        [73] = (WeatherLook.Snow, "Snow"),
        [75] = (WeatherLook.Snow, "Heavy snow"),
        [77] = (WeatherLook.Snow, "Snow grains"),
        [80] = (WeatherLook.Rain, "Light showers"),
        [81] = (WeatherLook.Rain, "Showers"),
        [82] = (WeatherLook.Rain, "Heavy showers"),
        [85] = (WeatherLook.Snow, "Snow showers"),
        [86] = (WeatherLook.Snow, "Heavy snow showers"),
        [95] = (WeatherLook.Thunderstorm, "Thunderstorm"),
        [96] = (WeatherLook.Thunderstorm, "Thunderstorm with hail"),
        [99] = (WeatherLook.Thunderstorm, "Severe thunderstorm"),
    };

    /// <summary>What the sky is doing, in words.</summary>
    public static string Describe(int? code)
    {
        if (code is not { } value)
        {
            return "Unknown";
        }

        return Codes.TryGetValue(value, out var entry)
            ? entry.Text
            : $"Code {value.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>Which icon family the condition belongs to.</summary>
    public static WeatherLook Look(int? code)
        => code is { } value && Codes.TryGetValue(value, out var entry) ? entry.Look : WeatherLook.Unknown;

    /// <summary>The degree symbol and scale letter, e.g. <c>°C</c>.</summary>
    public static string UnitSymbol(TemperatureUnit unit)
        => unit == TemperatureUnit.Fahrenheit ? "°F" : "°C";

    /// <summary>
    /// A temperature as a whole number and its unit: <c>18°C</c>.
    /// <para>
    /// Rounded, because a tenth of a degree is below what any forecast can actually claim and
    /// "17.3°C" reads as a measurement of this room rather than of the weather. A reading that came
    /// back as null or NaN prints as <c>--</c> and never throws on the render path.
    /// </para>
    /// </summary>
    public static string Temperature(double? value, TemperatureUnit unit)
    {
        if (value is not { } number || !double.IsFinite(number))
        {
            return "--";
        }

        return $"{Whole(number)}{UnitSymbol(unit)}";
    }

    /// <summary>A temperature with no unit, for a column where the unit is on the heading.</summary>
    public static string Degrees(double? value)
    {
        if (value is not { } number || !double.IsFinite(number))
        {
            return "--";
        }

        return Whole(number) + "°";
    }

    /// <summary>
    /// A reading as a whole number.
    /// <para>
    /// Negative zero is flattened on the way out. Rounding -0.4 away from zero yields the double
    /// -0.0, which formats as "-0" — so a mild frost showed as "-0°C", which reads as a typo
    /// rather than as a temperature.
    /// </para>
    /// </summary>
    private static string Whole(double value)
    {
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);

        if (rounded == 0)
        {
            rounded = 0;
        }

        return rounded.ToString("F0", CultureInfo.InvariantCulture);
    }

    /// <summary>A relative humidity as a whole percentage, clamped to the range it can occupy.</summary>
    public static string Humidity(double? percent)
    {
        if (percent is not { } value || !double.IsFinite(value))
        {
            return "--";
        }

        var clamped = Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 100);
        return $"{clamped.ToString("F0", CultureInfo.InvariantCulture)}%";
    }

    /// <summary>A wind speed in km/h. Negative is not a speed, so it reads as missing.</summary>
    public static string Wind(double? kilometresPerHour)
    {
        if (kilometresPerHour is not { } value || !double.IsFinite(value) || value < 0)
        {
            return "--";
        }

        return $"{Math.Round(value, MidpointRounding.AwayFromZero).ToString("F0", CultureInfo.InvariantCulture)} km/h";
    }

    /// <summary>
    /// A precipitation total in millimetres, or empty when there is none.
    /// <para>
    /// Empty rather than "0 mm": a dry day should look dry at a glance, and a column of zeroes is
    /// noise that makes the one wet day harder to find.
    /// </para>
    /// </summary>
    public static string Precipitation(double? millimetres)
    {
        if (millimetres is not { } value || !double.IsFinite(value) || value <= 0)
        {
            return string.Empty;
        }

        // A tenth below 10mm, where the difference between 0.2 and 2 is the difference between
        // damp and wet; whole millimetres above it, where it no longer carries.
        var digits = value < 10 ? 1 : 0;
        return $"{value.ToString($"F{digits}", CultureInfo.InvariantCulture)} mm";
    }

    /// <summary>
    /// The label on a forecast row: <c>Today</c>, then the short weekday name.
    /// <para>
    /// Computed against the current day, never baked into the cached forecast. A forecast fetched
    /// yesterday evening and restored this morning would otherwise label a stale row "Today", which
    /// is the one label a reader will act on without checking.
    /// </para>
    /// </summary>
    public static string DayLabel(DateOnly day, DateOnly today)
        => DayLabel(day, today, CultureInfo.CurrentCulture);

    /// <summary>
    /// [S-721] The same label with the weekday in <paramref name="culture"/>'s own abbreviation
    /// ("Do." on a German machine). "Today" stays a word of this app's, like every other string it
    /// prints, until there is a localisation layer to translate it.
    /// </summary>
    public static string DayLabel(DateOnly day, DateOnly today, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        if (day == today)
        {
            return "Today";
        }

        var names = culture.DateTimeFormat.AbbreviatedDayNames;
        var index = (int)day.DayOfWeek;

        // A culture is a trust boundary of sorts: a custom one can carry an odd array. The English
        // name is a better answer than an exception on the render path.
        return index < names.Length && !string.IsNullOrWhiteSpace(names[index])
            ? names[index]
            : DayNames[index];
    }

    /// <summary>
    /// A precipitation amount as the bare number, for a column whose droplet says what it is
    /// ("0.3"). Empty when dry, for the same reason <see cref="Precipitation"/> is. [S-727]
    /// </summary>
    public static string PrecipitationAmount(double? millimetres)
    {
        var text = Precipitation(millimetres);
        return text.Length == 0 ? text : text[..^3];
    }

    /// <summary>A chance of precipitation, "40%", or empty when the service did not say.</summary>
    public static string Chance(double? percent)
        => percent is { } value && double.IsFinite(value) ? Humidity(value) : string.Empty;

    /// <summary>A share of the sky, "75%".</summary>
    public static string Cover(double? percent) => Humidity(percent);

    /// <summary>Sea-level pressure in hectopascals, "1013 hPa". Outside 850..1100 is not weather.</summary>
    public static string Pressure(double? hectopascals)
    {
        if (hectopascals is not { } value || !double.IsFinite(value) || value < 850 || value > 1100)
        {
            return "--";
        }

        return $"{Math.Round(value, MidpointRounding.AwayFromZero).ToString("F0", CultureInfo.InvariantCulture)} hPa";
    }

    /// <summary>A UV index as a whole number; the scale has no use for a tenth.</summary>
    public static string Uv(double? index)
    {
        if (index is not { } value || !double.IsFinite(value) || value < 0)
        {
            return "--";
        }

        return Math.Round(value, MidpointRounding.AwayFromZero).ToString("F0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Where the wind comes from, on the eight-point rose: "NW". Empty for a missing or impossible
    /// bearing rather than a guess.
    /// </summary>
    public static string Compass(double? degrees)
    {
        if (degrees is not { } value || !double.IsFinite(value) || value < 0 || value > 360)
        {
            return string.Empty;
        }

        var sector = (int)Math.Round(value / 45, MidpointRounding.AwayFromZero) % 8;
        return Points[sector];
    }

    /// <summary>Wind speed and, when known, its direction: "6 km/h NW".</summary>
    public static string WindFrom(double? kilometresPerHour, double? degrees)
    {
        var speed = Wind(kilometresPerHour);
        var from = Compass(degrees);
        return speed == "--" || from.Length == 0 ? speed : $"{speed} {from}";
    }

    /// <summary>A sunrise or sunset in the culture's short time pattern, or "--" for a polar day.</summary>
    public static string Clock(TimeOnly? time, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return time is { } value ? value.ToString(culture.DateTimeFormat.ShortTimePattern, culture) : "--";
    }

    /// <summary>"H 24°  L 14°" for today's line beside the condition, or empty when neither is known.</summary>
    public static string HighLow(double? high, double? low)
    {
        if (high is null && low is null)
        {
            return string.Empty;
        }

        return $"H {Degrees(high)}  L {Degrees(low)}";
    }

    /// <summary>
    /// Where a day's high and low sit in the range covered by the whole forecast, as two fractions
    /// of 0..1 for the bar that shows it.
    /// <para>
    /// Scaled against the <i>set</i> of days rather than against each day's own range, which is the
    /// entire point of the bar: a week where every day is 12° to 14° should draw seven short bars in
    /// the same place, not seven full-width ones.
    /// </para>
    /// </summary>
    public static (double Start, double End) RangeBar(double low, double high, double coldest, double warmest)
    {
        if (!double.IsFinite(low) || !double.IsFinite(high) || !double.IsFinite(coldest) || !double.IsFinite(warmest))
        {
            return (0, 1);
        }

        var span = warmest - coldest;

        // Every day identical, or a single day: one full bar says "no spread" better than a
        // division by zero does.
        if (span <= 0)
        {
            return (0, 1);
        }

        var start = Math.Clamp((low - coldest) / span, 0, 1);
        var end = Math.Clamp((high - coldest) / span, 0, 1);

        return end < start ? (end, start) : (start, end);
    }

    private static readonly string[] DayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    private static readonly string[] Points = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
}
