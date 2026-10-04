using System.Globalization;

namespace Themia.Core.Astronomy;

/// <summary>
/// The Sun &amp; moon widget's words, written out rather than through culture patterns: the app
/// builds with <c>InvariantGlobalization</c>. Pure, so every edge (polar days, a shrinking day, the
/// countdown's last minute) is a test rather than a screenshot.
/// </summary>
public static class SkyText
{
    private static readonly string[] Days = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>"11h 33m"; "24h" in polar day and "0h" in polar night. [S-161]</summary>
    public static string Daylight(SunTimes times)
    {
        if (SolarPosition.Daylight(times) is not { } span)
        {
            return times.Polar == PolarState.Day ? "24h" : "0h";
        }

        var minutes = (int)Math.Round(span.TotalMinutes);
        return $"{minutes / 60}h {minutes % 60}m";
    }

    /// <summary>
    /// [S-1490] The day against yesterday: "+2m 14s", "−58s" (U+2212), "same as yesterday" when the
    /// change rounds to nothing, which it does for a day or two around each solstice.
    /// </summary>
    public static string Change(TimeSpan change)
    {
        var seconds = (int)Math.Round(change.TotalSeconds);

        if (seconds == 0)
        {
            return "same as yesterday";
        }

        var sign = seconds < 0 ? "−" : "+";
        var magnitude = Math.Abs(seconds);

        return magnitude >= 60
            ? $"{sign}{magnitude / 60}m {magnitude % 60}s"
            : $"{sign}{magnitude}s";
    }

    /// <summary>"in 6h 45m", "in 12m"; never negative. [S-163]</summary>
    public static string Countdown(TimeSpan remaining)
    {
        var minutes = Math.Max(0, (int)Math.Round(remaining.TotalMinutes));
        return minutes >= 60 ? $"in {minutes / 60}h {minutes % 60}m" : $"in {minutes}m";
    }

    /// <summary>"Wed 7 Oct", for a moment already in the zone it should be read in.</summary>
    public static string ShortDate(DateTimeOffset local)
        => $"{Days[(int)local.DayOfWeek]} {local.Day.ToString(CultureInfo.InvariantCulture)} {Months[local.Month - 1]}";

    /// <summary>"34°": whole degrees, which is all a widget can show of a sun position good to a degree.</summary>
    public static string Degrees(double degrees)
        => $"{((int)Math.Round(degrees)).ToString(CultureInfo.InvariantCulture)}°"; // int: no "-0°"
}
