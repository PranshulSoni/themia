using System.Globalization;
using Themia.Core.Model;

namespace Themia.Core.Widgets;

/// <summary>
/// What one countdown row shows at one instant.
/// </summary>
/// <param name="Text">The duration, already worded for its format and direction.</param>
/// <param name="Elapsed">
/// True once the target is at or behind <c>now</c>. The view uses it to dim the label and switch the
/// duration to the critical colour. [C-40]
/// </param>
public readonly record struct CountdownReading(string Text, bool Elapsed);

/// <summary>
/// Turns a target instant plus a <see cref="CountdownFormat"/> into the strings a countdown row
/// shows.
/// <para>
/// Every entry point takes <c>now</c> and a <see cref="TimeZoneInfo"/> explicitly. Reading
/// <c>DateTimeOffset.Now</c> or <c>TimeZoneInfo.Local</c> in here would make the thresholds in
/// [C-30] only reachable by waiting for them, and the boundaries are exactly what is worth pinning.
/// </para>
/// <para>
/// Worded explicitly rather than through culture patterns, for the same reason as
/// <see cref="ClockFormatter"/>: the app builds with <c>InvariantGlobalization</c>, so a culture
/// format string does not behave the way a display string needs to.
/// </para>
/// </summary>
public static class CountdownFormatter
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    /// <summary>The name shown for a timer with no label of its own. [C-03]</summary>
    public const string UntitledLabel = "Untitled";

    /// <summary>
    /// The duration between <paramref name="now"/> and <paramref name="target"/>, worded for
    /// <paramref name="format"/>. [C-11]
    /// <para>
    /// A target exactly on <c>now</c> counts as elapsed: the alternative is a row that reads "in a
    /// moment" for one whole tick after the moment has passed.
    /// </para>
    /// </summary>
    public static CountdownReading Read(DateTimeOffset target, DateTimeOffset now, CountdownFormat format)
    {
        var difference = target - now;
        var elapsed = difference <= TimeSpan.Zero;
        var span = difference.Duration();

        var text = format switch
        {
            CountdownFormat.Days => Plural((int)span.TotalDays, "day"),
            CountdownFormat.DaysHoursMinutes => $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m",
            CountdownFormat.DaysHoursMinutesSeconds => $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m {span.Seconds}s",
            _ => Auto(span),
        };

        if (elapsed)
        {
            // [C-41] "in a moment ago" is not English. Under a minute in the past is "just now".
            text = format == CountdownFormat.Auto && span < Minute ? "just now" : $"{text} ago";
        }

        return new CountdownReading(text, elapsed);
    }

    /// <summary>
    /// [C-30] The coarsest wording that still says something useful, picked from how far away the
    /// target is.
    /// </summary>
    private static string Auto(TimeSpan span)
    {
        if (span < Minute)
        {
            return "in a moment";
        }

        if (span < Hour)
        {
            return Plural((int)span.TotalMinutes, "min");
        }

        if (span < Day)
        {
            var hours = (int)span.TotalHours;

            // A round number reads better spelled out: "3 hrs", not "3h 0m".
            return span.Minutes == 0 ? Plural(hours, "hr") : $"{hours}h {span.Minutes}m";
        }

        var days = (int)span.TotalDays;

        if (span < Week)
        {
            return span.Hours == 0 ? Plural(days, "day") : $"{days}d {span.Hours}h";
        }

        // Past a week, hours are noise.
        return Plural(days, "day");
    }

    /// <summary>
    /// The target as an absolute local date and time, for the row subtitle. [C-03]
    /// <para>
    /// The year appears only when it is not the current one: on a countdown to next Tuesday it is
    /// four characters of nothing, and on a countdown to 2031 its absence would be misleading.
    /// </para>
    /// </summary>
    public static string TargetLabel(DateTimeOffset target, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var local = TimeZoneInfo.ConvertTime(target, zone);
        var here = TimeZoneInfo.ConvertTime(now, zone);
        var date = local.Year == here.Year
            ? $"{local.Day} {MonthNames[local.Month - 1]}"
            : $"{local.Day} {MonthNames[local.Month - 1]} {local.Year.ToString(CultureInfo.InvariantCulture)}";

        return $"{date}, {local.Hour:D2}:{local.Minute:D2}";
    }

    /// <summary>The full local date and time, for the row's tooltip. [C-05]</summary>
    public static string FullTargetLabel(DateTimeOffset target, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var local = TimeZoneInfo.ConvertTime(target, zone);
        return $"{DayNames[(int)local.DayOfWeek]}, {local.Day} {MonthNames[local.Month - 1]} " +
            $"{local.Year.ToString(CultureInfo.InvariantCulture)} at {local.Hour:D2}:{local.Minute:D2}";
    }

    /// <summary>The local calendar day of a target, for seeding the settings date picker. [C-53]</summary>
    public static DateTimeOffset LocalDate(DateTimeOffset target, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var local = TimeZoneInfo.ConvertTime(target, zone);
        return new DateTimeOffset(local.Date, local.Offset);
    }

    /// <summary>The local time of day of a target, for seeding the settings time picker. [C-53]</summary>
    public static TimeSpan LocalTimeOfDay(DateTimeOffset target, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return TimeZoneInfo.ConvertTime(target, zone).TimeOfDay;
    }

    /// <summary>
    /// [C-55] Build a UTC target from a picked local day and time of day, or <c>null</c> when there
    /// is nothing usable to build from.
    /// <para>
    /// Null, never "now". The original had a bug here: clearing the date field wrote
    /// <c>new Date()</c> back to the timer, silently retargeting it to the current instant and
    /// refilling the field the user had just cleared. A caller that gets <c>null</c> keeps the
    /// target it already had, which is the only safe reading of "the user cleared the field".
    /// </para>
    /// <para>
    /// A local time that does not exist — the hour a DST jump skips — is also <c>null</c> rather than
    /// a guess, for the same reason.
    /// </para>
    /// </summary>
    public static DateTimeOffset? CombineLocal(DateTimeOffset? date, TimeSpan? timeOfDay, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        if (date is not { } day || timeOfDay is not { } time)
        {
            return null;
        }

        if (time < TimeSpan.Zero || time >= Day)
        {
            return null;
        }

        var midnight = day.DateTime.Date;

        // A picker cannot reach this, but a persisted target can, and DateTime.Add throws on overflow.
        if (midnight > DateTime.MaxValue - time)
        {
            return null;
        }

        var local = DateTime.SpecifyKind(midnight + time, DateTimeKind.Unspecified);

        if (zone.IsInvalidTime(local))
        {
            return null;
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    /// <summary>
    /// [C-51] Where a freshly added timer points: this time tomorrow, on the minute.
    /// <para>
    /// Seconds are zeroed because the pickers cannot express them, so leaving them on would make the
    /// countdown disagree with the target the settings window shows.
    /// </para>
    /// </summary>
    public static DateTimeOffset DefaultTarget(DateTimeOffset now)
    {
        var next = now.ToUniversalTime().AddDays(1);
        return new DateTimeOffset(next.Year, next.Month, next.Day, next.Hour, next.Minute, 0, TimeSpan.Zero);
    }

    /// <summary>The name for a format in the settings picker. [C-11]</summary>
    public static string FormatLabel(CountdownFormat format) => format switch
    {
        CountdownFormat.Days => "Days only",
        CountdownFormat.DaysHoursMinutes => "Days · Hours · Min",
        CountdownFormat.DaysHoursMinutesSeconds => "Days · Hours · Min · Sec",
        _ => "Auto",
    };

    private static string Plural(int count, string unit)
        => $"{count.ToString(CultureInfo.InvariantCulture)} {unit}{(count == 1 ? string.Empty : "s")}";

    private static readonly string[] MonthNames =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private static readonly string[] DayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
}
