using System.Globalization;

namespace Themia.Core.Widgets;

/// <summary>
/// The day and month names these widgets write, spelled out here rather than asked of a culture.
/// <para>
/// The app builds with <c>InvariantGlobalization</c>: there is no ICU data in the process, so
/// <c>DateTime.ToString("ddd")</c> can only ever answer in English anyway. Writing the names out
/// says that out loud instead of hiding it behind a format string that looks localised and is not,
/// and it fixes the width of every label — a column header that is two characters wide on every row
/// is a column, and one that is sometimes three is a jitter.
/// </para>
/// </summary>
public static class CalendarText
{
    private static readonly string[] WeekdayInitials = ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];

    private static readonly string[] WeekdayNames =
        ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    private static readonly string[] MonthNames =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>Two letters, the width a heatmap column header wants.</summary>
    public static string WeekdayInitial(DayOfWeek day) => WeekdayInitials[Index(day)];

    /// <summary>The full weekday name, for a tooltip or a screen reader.</summary>
    public static string WeekdayName(DayOfWeek day) => WeekdayNames[Index(day)];

    /// <summary>
    /// A <see cref="DayOfWeek"/> is an enum, and an enum in C# holds whatever integer was cast into
    /// it — including one read back out of a settings file. Folded into range rather than throwing
    /// an <see cref="IndexOutOfRangeException"/> out of a render loop.
    /// </summary>
    private static int Index(DayOfWeek day) => (int)day is var raw && raw is >= 0 and < 7 ? raw : 0;

    /// <summary>The seven column headers, in the order a week starting on <paramref name="weekStart"/> runs.</summary>
    public static IReadOnlyList<string> WeekdayHeaders(DayOfWeek weekStart)
    {
        var headers = new string[7];
        var first = Index(weekStart);

        for (var i = 0; i < 7; i++)
        {
            headers[i] = WeekdayInitials[(first + i) % 7];
        }

        return headers;
    }

    /// <summary>A short date: <c>3 Feb</c>. The year is left off, because a heatmap only ever spans one.</summary>
    public static string ShortDate(LocalDay day)
        => $"{day.Day.ToString(CultureInfo.InvariantCulture)} {MonthNames[day.Month - 1]}";

    /// <summary>
    /// The whole day, as a sentence a screen reader reads correctly: <c>Tuesday 3 February 2026</c>
    /// abbreviated to <c>Tuesday 3 Feb 2026</c>. Carries the year, because this is the form used
    /// where there is nothing else on screen to supply it.
    /// </summary>
    public static string FullDate(LocalDay day)
        => $"{WeekdayName(day.DayOfWeek)} {ShortDate(day)} {day.Year.ToString(CultureInfo.InvariantCulture)}";
}
