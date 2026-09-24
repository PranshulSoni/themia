namespace Themia.Core.Widgets;

/// <summary>
/// What a habit's log of completed days means: whether it is done, how long the run is, and what the
/// last few days looked like.
/// <para>
/// Every function here takes the day to reckon from as an argument. None of them can read the clock,
/// which is the point: a streak that is right in June and wrong on the last Sunday in October is the
/// classic bug in this widget, and it is only catchable by a test that can put "today" anywhere it
/// likes. The day arithmetic is <see cref="LocalDay"/>'s, so the 23-hour and 25-hour days are one
/// day long here like every other.
/// </para>
/// <para>
/// The log is a list of <c>YYYY-MM-DD</c> strings, and it is treated as untrusted on every read: it
/// comes out of a JSON file in the user's profile that anybody can edit. Keys that are not dates are
/// dropped, duplicates collapse, and days in the future are ignored rather than counted — a clock
/// that went backwards, or a hand-edited file, must not be able to mint a streak.
/// </para>
/// </summary>
public static class HabitRules
{
    /// <summary>
    /// How far back <see cref="Recent"/> will ever look, and the ceiling the config clamps to. A
    /// month of squares is already more than a row can draw; past that the number is only a bound on
    /// a render loop that a hand-edited file would otherwise get to choose.
    /// </summary>
    public const int MaxHistoryDays = 31;

    /// <summary>
    /// The days in the log, as a set, with everything unreadable or in the future removed.
    /// <para>
    /// Public because the view builds it once per render and hands it to each of the three functions
    /// below, rather than each of them re-parsing the same list. Parsing per row per tick is the
    /// difference between a widget that costs nothing and one that re-reads a year of history every
    /// minute.
    /// </para>
    /// </summary>
    public static HashSet<LocalDay> Days(IEnumerable<string>? log, LocalDay today)
    {
        var days = new HashSet<LocalDay>();

        if (log is null)
        {
            return days;
        }

        foreach (var key in log)
        {
            // Not an error to report: a key that will not parse is a file somebody edited, and the
            // honest reading of it is "this day is not marked". Dropping it silently is the whole
            // of the recovery, and it cannot cascade because every other key is independent.
            if (!LocalDay.TryParse(key, out var day))
            {
                continue;
            }

            // A day that has not happened yet cannot have been completed. Ignored rather than
            // trusted, so neither a hand-edit nor a clock that jumped forward and back can inflate
            // a streak.
            if (day > today)
            {
                continue;
            }

            days.Add(day);
        }

        return days;
    }

    /// <summary>Whether the habit is marked on <paramref name="day"/>.</summary>
    public static bool IsDone(IReadOnlySet<LocalDay> days, LocalDay day) => days.Contains(day);

    /// <summary>
    /// The current run of completed days ending at <paramref name="today"/>.
    /// <para>
    /// Today not being done does <b>not</b> break the streak — the day is not over, and a counter
    /// that resets itself every midnight and has to be earned back before breakfast is a counter
    /// nobody would keep. So the count starts at today when today is done, and at yesterday when it
    /// is not; a gap anywhere before that ends it.
    /// </para>
    /// </summary>
    public static int Streak(IReadOnlySet<LocalDay> days, LocalDay today)
    {
        if (days.Count == 0)
        {
            return 0;
        }

        var cursor = days.Contains(today) ? today : today.AddDays(-1);
        var streak = 0;

        // Bounded by the log rather than by the calendar: every step must consume one of the days
        // actually in the set, so the loop cannot run past days.Count however far back the log goes.
        while (streak <= days.Count && days.Contains(cursor))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }

        return streak;
    }

    /// <summary>
    /// The last <paramref name="count"/> days, oldest first, with <paramref name="today"/> last.
    /// <para>
    /// Derived from the same set the streak counts, so the squares in a row and the number beside
    /// them can never disagree about which day is which.
    /// </para>
    /// </summary>
    public static IReadOnlyList<bool> Recent(IReadOnlySet<LocalDay> days, LocalDay today, int count)
    {
        var wanted = Math.Clamp(count, 0, MaxHistoryDays);

        if (wanted == 0)
        {
            return [];
        }

        var recent = new bool[wanted];

        for (var i = 0; i < wanted; i++)
        {
            recent[i] = days.Contains(today.AddDays(i - (wanted - 1)));
        }

        return recent;
    }

    /// <summary>
    /// Flip <paramref name="day"/> in a log, and say what it is now.
    /// <para>
    /// Un-marking removes <i>every</i> copy of the day, not the first one found: a log that
    /// somehow holds the same day twice would otherwise take two taps to clear and would read as a
    /// checkbox that ignored a click.
    /// </para>
    /// </summary>
    public static bool Toggle(IList<string> log, LocalDay day)
    {
        ArgumentNullException.ThrowIfNull(log);

        var key = day.Key;
        var removed = false;

        for (var i = log.Count - 1; i >= 0; i--)
        {
            // Compared as parsed days rather than as strings, so a log written by an older build
            // with a differently-shaped key still matches the day it means.
            if (LocalDay.TryParse(log[i], out var logged) && logged == day)
            {
                log.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
        {
            return false;
        }

        log.Add(key);
        return true;
    }

    /// <summary>How many of <paramref name="habits"/> are marked today, for the header's count.</summary>
    public static int DoneToday<T>(IReadOnlyList<T> habits, Func<T, IEnumerable<string>?> log, LocalDay today)
    {
        ArgumentNullException.ThrowIfNull(habits);
        ArgumentNullException.ThrowIfNull(log);

        var done = 0;

        foreach (var habit in habits)
        {
            if (Days(log(habit), today).Contains(today))
            {
                done++;
            }
        }

        return done;
    }
}
