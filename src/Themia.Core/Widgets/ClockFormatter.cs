using System.Globalization;
using Themia.Core.Model;

namespace Themia.Core.Widgets;

/// <summary>What a clock widget shows at one instant. Plain data, so the view is a pure projection.</summary>
public readonly record struct ClockReading(string Time, string? Meridiem, string? Date, double HourAngle, double MinuteAngle, double SecondAngle);

/// <summary>[S-665] What a larger clock adds under its date.</summary>
/// <param name="ShortDate">"Sun 4 Oct": the date for a tile too narrow for the long one.</param>
/// <param name="Week">"Week 40 · Day 278".</param>
/// <param name="Zone">"UTC+02:00 · W. Europe Daylight Time".</param>
public readonly record struct ClockDetails(string ShortDate, string Week, string Zone);

/// <summary>
/// Turns an instant plus a <see cref="ClockConfig"/> into display strings and hand angles.
/// <para>
/// Formatting is done explicitly rather than with culture patterns: the app builds with
/// <c>InvariantGlobalization</c> (no ICU, a large part of why the AOT binary stays small), so
/// "h:mm tt" would render with invariant-culture quirks instead of following the config.
/// </para>
/// </summary>
public static class ClockFormatter
{
    public static ClockReading Read(DateTimeOffset utcNow, ClockConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var zone = config.ResolveTimeZone();
        var local = TimeZoneInfo.ConvertTime(utcNow, zone);

        var hour24 = local.Hour;
        var hour = config.Use24Hour ? hour24 : ToTwelveHour(hour24);

        var time = config.ShowSeconds
            ? $"{Pad(hour, config.Use24Hour)}:{local.Minute:D2}:{local.Second:D2}"
            : $"{Pad(hour, config.Use24Hour)}:{local.Minute:D2}";

        var meridiem = config.Use24Hour ? null : (hour24 < 12 ? "AM" : "PM");
        var date = config.ShowDate ? FormatDate(local) : null;

        // Hands sweep rather than step, so an analog face reads correctly between ticks.
        var secondAngle = local.Second * 6.0;
        var minuteAngle = (local.Minute * 6.0) + (local.Second * 0.1);
        var hourAngle = ((hour24 % 12) * 30.0) + (local.Minute * 0.5);

        return new ClockReading(time, meridiem, date, hourAngle, minuteAngle, secondAngle);
    }

    /// <summary>
    /// [S-665] The lines a clock only shows when it has room for them: the date short enough for a
    /// narrow tile, the week and day of the year, and the time zone. Read once per render rather than
    /// folded into <see cref="ClockReading"/>, because a small clock never draws them.
    /// </summary>
    public static ClockDetails Details(DateTimeOffset utcNow, ClockConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var zone = config.ResolveTimeZone();
        var local = TimeZoneInfo.ConvertTime(utcNow, zone);

        var shortDate = $"{DayNames[(int)local.DayOfWeek][..3]} {local.Day} {MonthNames[local.Month - 1][..3]}";
        var week = $"Week {ISOWeek.GetWeekOfYear(local.DateTime)} · Day {local.DayOfYear}";
        var name = zone.IsDaylightSavingTime(local) ? zone.DaylightName : zone.StandardName;
        var offset = Offset(local.Offset);
        var zoneLine = string.IsNullOrWhiteSpace(name) || name == offset ? offset : $"{offset} · {name}";

        return new ClockDetails(shortDate, week, zoneLine);
    }

    /// <summary>
    /// [S-1482] The label over the time: the custom label when there is one, else the city of a chosen
    /// zone ("Tokyo"), else nothing: a clock on the machine's own zone stays label-free, the classic
    /// look. (The original also dropped the label when the chosen zone <i>was</i> the local one; a
    /// Windows zone covers many cities, so here choosing any city labels the clock with it.)
    /// </summary>
    public static string? Label(ClockConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var custom = config.CustomLabel?.Trim();

        if (!string.IsNullOrEmpty(custom))
        {
            return custom;
        }

        return config.IsWorldClock && ZoneCatalog.Find(config.TimeZoneId) is { } entry ? entry.City : null;
    }

    /// <summary>[S-1485] The offset line for this instant, or null when the mode is None.</summary>
    public static string? OffsetLine(DateTimeOffset utcNow, ClockConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return config.OffsetMode == ClockOffsetMode.None
            ? null
            : OffsetText(config.ResolveTimeZone().GetUtcOffset(utcNow), TimeZoneInfo.Local.GetUtcOffset(utcNow), config.OffsetMode);
    }

    /// <summary>
    /// [S-1485] "UTC+2", "UTC+5:30", "UTC−3" from UTC; "+8h local", "−5:30h local", "same as local"
    /// from the machine's zone. Minus is U+2212, the typographic sign, as the original wrote it.
    /// </summary>
    public static string? OffsetText(TimeSpan zoneOffset, TimeSpan localOffset, ClockOffsetMode mode)
    {
        switch (mode)
        {
            case ClockOffsetMode.Utc:
                return zoneOffset == TimeSpan.Zero ? "UTC" : $"UTC{Signed(zoneOffset)}";

            case ClockOffsetMode.Local:
                var difference = zoneOffset - localOffset;
                return difference == TimeSpan.Zero ? "same as local" : $"{Signed(difference)}h local";

            default:
                return null;
        }
    }

    /// <summary>"+2", "−3", "+5:30": whole hours bare, minutes only when there are some.</summary>
    private static string Signed(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "−" : "+";
        var magnitude = offset.Duration();
        var hours = (int)magnitude.TotalHours;

        return magnitude.Minutes == 0
            ? $"{sign}{hours}"
            : $"{sign}{hours}:{magnitude.Minutes:D2}";
    }

    /// <summary>"UTC", "UTC+02:00", "UTC-03:30".</summary>
    public static string Offset(TimeSpan offset)
    {
        if (offset == TimeSpan.Zero)
        {
            return "UTC";
        }

        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var magnitude = offset.Duration();
        return $"UTC{sign}{magnitude.Hours:D2}:{magnitude.Minutes:D2}";
    }

    /// <summary>12-hour clocks show 12, not 0, for both midnight and noon.</summary>
    public static int ToTwelveHour(int hour24)
    {
        var hour = hour24 % 12;
        return hour == 0 ? 12 : hour;
    }

    /// <summary>24-hour time is zero-padded; 12-hour time is not, which is what both conventions expect.</summary>
    private static string Pad(int hour, bool use24Hour)
        => use24Hour ? hour.ToString("D2", CultureInfo.InvariantCulture) : hour.ToString(CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset local)
        => $"{DayNames[(int)local.DayOfWeek]}, {local.Day} {MonthNames[local.Month - 1]}";

    private static readonly string[] DayNames =
        ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    private static readonly string[] MonthNames =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
}
