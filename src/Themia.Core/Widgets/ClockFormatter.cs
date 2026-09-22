using System.Globalization;
using Themia.Core.Model;

namespace Themia.Core.Widgets;

/// <summary>What a clock widget shows at one instant. Plain data, so the view is a pure projection.</summary>
public readonly record struct ClockReading(string Time, string? Meridiem, string? Date, double HourAngle, double MinuteAngle, double SecondAngle);

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
