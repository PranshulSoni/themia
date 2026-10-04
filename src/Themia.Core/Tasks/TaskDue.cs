using System.Globalization;
using System.Text.RegularExpressions;

namespace Themia.Core.Tasks;

/// <summary>[S-594] Which label a due date earns.</summary>
public enum DueLabel
{
    Overdue,
    Today,
    Tomorrow,

    /// <summary>Two to six days out: the short weekday.</summary>
    Weekday,

    /// <summary>A week or more out: the date.</summary>
    Date,
}

/// <summary>A due date resolved against one "today".</summary>
/// <param name="Day">The calendar day the task is due.</param>
/// <param name="Days">Whole days from today; negative when overdue.</param>
public readonly record struct Due(DateOnly Day, DueLabel Label, int Days)
{
    public bool IsPast => Days < 0;
}

/// <summary>
/// [S-593] A To Do due date is a calendar <b>day</b>, but neither provider says so in its type.
/// <para>
/// What arrives is one of two encodings. <b>Midnight of the intended day</b> — Graph's zoneless
/// <c>2026-08-10T00:00:00.0000000</c>, Google's <c>2026-08-10T00:00:00.000Z</c> (its time part is
/// documented as meaningless), or a bare date — whose date part <i>is</i> the day: converting it
/// through UTC would push every viewer west of Greenwich back a day. Or <b>the creator's local
/// midnight already converted to UTC</b> — the Microsoft To Do mobile app writes
/// <c>2026-08-09T17:00:00.0000000</c> for a UTC+7 user's "due the 10th" — which is a real instant,
/// read as UTC when zoneless, whose day is the viewer's local day. Comparing strings, or reading
/// that second shape as local time, is how a task due today showed "overdue" (classic issue #101).
/// </para>
/// <para>
/// Pure, with the viewer's zone and "today" passed in, so the rule is tested in real zones against a
/// fixed clock rather than against whatever the build machine's day happens to be.
/// </para>
/// </summary>
public static partial class TaskDue
{
    private static readonly string[] DayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    private static readonly string[] MonthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>The intended day of a due value, or null when it is absent or not a date.</summary>
    public static DateOnly? Day(string? raw, TimeZoneInfo viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var match = DuePattern().Match(raw);
        if (!match.Success)
        {
            return null;
        }

        if (!TryDate(match, out var written))
        {
            return null;
        }

        var hasTime = match.Groups[4].Success;
        var midnight = !hasTime
            || (match.Groups[4].Value == "00" && match.Groups[5].Value == "00"
                && (!match.Groups[6].Success || match.Groups[6].Value == "00"));

        if (midnight)
        {
            // Date-only under every encoding: the written date is the day meant.
            return written;
        }

        // A real instant. Zoneless means UTC, as the calendar reads Graph's own zoneless times.
        if (!DateTimeOffset.TryParse(
                raw.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var instant))
        {
            return null;
        }

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, viewer).DateTime);
    }

    /// <summary>[S-594] Resolve a due value against <paramref name="today"/>.</summary>
    public static Due? Resolve(string? raw, DateOnly today, TimeZoneInfo viewer)
    {
        if (Day(raw, viewer) is not { } day)
        {
            return null;
        }

        // Day numbers, not instants: a DST change between the two days cannot shift the count.
        var days = day.DayNumber - today.DayNumber;
        var label = days switch
        {
            < 0 => DueLabel.Overdue,
            0 => DueLabel.Today,
            1 => DueLabel.Tomorrow,
            < 7 => DueLabel.Weekday,
            _ => DueLabel.Date,
        };

        return new Due(day, label, days);
    }

    /// <summary>[S-594] True when the due day is before today. An undated task is never past.</summary>
    public static bool IsPast(string? raw, DateOnly today, TimeZoneInfo viewer)
        => Resolve(raw, today, viewer) is { IsPast: true };

    /// <summary>
    /// The label in words. Overdue is said as a word, not only coloured ([S-186]): a state a reader
    /// can only get from a colour is one some readers do not get at all.
    /// </summary>
    public static string Text(Due due) => due.Label switch
    {
        DueLabel.Overdue => "Overdue",
        DueLabel.Today => "Today",
        DueLabel.Tomorrow => "Tomorrow",
        DueLabel.Weekday => DayNames[(int)due.Day.DayOfWeek],
        _ => ShortDate(due.Day),
    };

    /// <summary>
    /// [S-789] The label with its date, for a due column with room for it: <c>Overdue · 2 Oct</c>,
    /// <c>Today</c>, <c>Tomorrow</c>, <c>Thu 8 Oct</c>. Today and tomorrow need no date to be exact.
    /// </summary>
    public static string WideText(Due due) => due.Label switch
    {
        DueLabel.Overdue => $"Overdue · {ShortDate(due.Day)}",
        DueLabel.Today => "Today",
        DueLabel.Tomorrow => "Tomorrow",
        _ => LongText(due),
    };

    /// <summary>[S-780] Whether a due date is worth a line on a tile with room for almost nothing.</summary>
    public static bool IsUrgent(Due due) => due.Label is DueLabel.Overdue or DueLabel.Today;

    /// <summary>The whole date, for a tooltip: <c>Wed 14 Oct</c>.</summary>
    public static string LongText(Due due) => $"{DayNames[(int)due.Day.DayOfWeek]} {ShortDate(due.Day)}";

    /// <summary>A day as the list writes it, <c>14 Oct</c>, the same shape every other widget here uses.</summary>
    public static string ShortDate(DateOnly day) => $"{day.Day} {MonthNames[day.Month - 1]}";

    /// <summary>
    /// [S-597] A day as the one encoding both providers read back as the day meant: bare
    /// <c>yyyy-MM-dd</c>, wrapped in each provider's field shape by <see cref="TaskWire"/>.
    /// </summary>
    public static string Wire(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool TryDate(Match match, out DateOnly day)
    {
        day = default;
        var year = int.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
        var month = int.Parse(match.Groups[2].ValueSpan, CultureInfo.InvariantCulture);
        var dayOfMonth = int.Parse(match.Groups[3].ValueSpan, CultureInfo.InvariantCulture);

        if (year < 1 || month is < 1 or > 12 || dayOfMonth < 1 || dayOfMonth > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        day = new DateOnly(year, month, dayOfMonth);
        return true;
    }

    /// <summary><c>YYYY-MM-DD</c>, optionally followed by a time; the wall clock exactly as written.</summary>
    [GeneratedRegex(@"^\s*(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2})(?::(\d{2}))?)?", RegexOptions.CultureInvariant)]
    private static partial Regex DuePattern();
}
