using Themia.Core.Model;

namespace Themia.Core.Widgets;

/// <summary>
/// The squares a heatmap is drawn on: rows of weeks, seven columns wide, in the order the chosen
/// week start runs.
/// <para>
/// A cell is either a day or nothing. "Nothing" is a real value here rather than a day outside the
/// window quietly folded onto the edge: a month that begins on a Thursday genuinely has three blank
/// squares before it, and a rolling window genuinely has blank squares after today, because the rest
/// of this week has not happened. Both are drawn as gaps, so the weekday columns line up.
/// </para>
/// <para>
/// One shape for both spans, so the view has one renderer. The difference between a rolling window
/// and a calendar month is entirely in which days land in which square, which is what this file is.
/// </para>
/// </summary>
/// <param name="Rows">Weeks, oldest first. Each is exactly seven cells.</param>
/// <param name="WeekStart">The day each row begins on, so the headers can be labelled.</param>
/// <param name="First">The earliest day the grid holds. Equals <see cref="Last"/> for an empty grid.</param>
/// <param name="Last">The latest day the grid holds.</param>
public sealed record MoodGrid(
    IReadOnlyList<IReadOnlyList<LocalDay?>> Rows,
    DayOfWeek WeekStart,
    LocalDay First,
    LocalDay Last)
{
    /// <summary>Seven, and it is not a setting: a week is seven days and the columns are the weekdays.</summary>
    public const int Columns = 7;

    /// <summary>
    /// A grid of one shape or the other, so a caller with a config in hand does not have to switch.
    /// </summary>
    /// <param name="span">Which shape.</param>
    /// <param name="today">The day to end a rolling window on, and the month a month window shows.</param>
    /// <param name="weeks">How many rows a rolling window has. Ignored by a month.</param>
    /// <param name="weekStart">Which day a row begins on.</param>
    public static MoodGrid Build(MoodSpan span, LocalDay today, int weeks, DayOfWeek weekStart)
        => span == MoodSpan.Month
            ? Month(today.Year, today.Month, weekStart)
            : Rolling(today, weeks, weekStart);

    /// <summary>
    /// The <paramref name="weeks"/> weeks ending with the one <paramref name="today"/> is in.
    /// <para>
    /// The last row is this week, not "the last seven days": a heatmap whose columns are not fixed
    /// weekdays is a strip, and the whole reason to draw a calendar is that Tuesday is under
    /// Tuesday. Days after today are left blank, because a future day is not an unlogged day.
    /// </para>
    /// </summary>
    public static MoodGrid Rolling(LocalDay today, int weeks, DayOfWeek weekStart)
    {
        var rowCount = Math.Clamp(weeks, 1, MoodTrackerConfig.MaxHistoryWeeks);
        var start = today.StartOfWeek(weekStart).AddDays(-Columns * (rowCount - 1));
        var rows = new List<IReadOnlyList<LocalDay?>>(rowCount);

        for (var row = 0; row < rowCount; row++)
        {
            var week = new LocalDay?[Columns];

            for (var column = 0; column < Columns; column++)
            {
                var day = start.AddDays((row * Columns) + column);
                week[column] = day > today ? null : day;
            }

            rows.Add(week);
        }

        return new MoodGrid(rows, weekStart, start, today);
    }

    /// <summary>
    /// One calendar month, padded to whole weeks.
    /// <para>
    /// The number of rows is the month's own: February in a common year that starts on the week's
    /// first day fits in exactly four, and a 31-day month that starts on the last day of the week
    /// needs six. Computing it rather than assuming five or six is what keeps the cells square.
    /// </para>
    /// </summary>
    public static MoodGrid Month(int year, int month, DayOfWeek weekStart)
    {
        var safeYear = Math.Clamp(year, 1, 9999);
        var safeMonth = Math.Clamp(month, 1, 12);

        var first = new LocalDay(new DateOnly(safeYear, safeMonth, 1));

        // The calendar's own answer, which is what makes a leap February 29 days and not 28.
        var last = first.AddDays(DateTime.DaysInMonth(safeYear, safeMonth) - 1);

        var start = first.StartOfWeek(weekStart);

        // Whole weeks from the padded start through the padded end, so the arithmetic works the
        // same whether the month needs four rows or six.
        var rowCount = (start.DaysUntil(last) / Columns) + 1;
        var rows = new List<IReadOnlyList<LocalDay?>>(rowCount);

        for (var row = 0; row < rowCount; row++)
        {
            var week = new LocalDay?[Columns];

            for (var column = 0; column < Columns; column++)
            {
                var day = start.AddDays((row * Columns) + column);
                week[column] = day < first || day > last ? null : day;
            }

            rows.Add(week);
        }

        return new MoodGrid(rows, weekStart, first, last);
    }

    /// <summary>The seven column headers, in this grid's own order.</summary>
    public IReadOnlyList<string> Headers => CalendarText.WeekdayHeaders(WeekStart);
}
