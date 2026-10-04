using System.Globalization;
using Themia.Core.Model;
using Themia.Core.Localization;

namespace Themia.Core.Widgets;

/// <summary>
/// One day of the heatmap, reduced to what a cell draws and what a screen reader says.
/// </summary>
/// <param name="Day">The calendar day.</param>
/// <param name="Count">How many moods were logged on it.</param>
/// <param name="Average">The mean level, or zero when nothing was logged.</param>
/// <param name="Bucket">
/// 0 for a day with nothing on it, otherwise 1 to 5. A logged day is <b>never</b> bucket 0, so the
/// worst possible day and an untouched one can never end up drawn the same.
/// </param>
public readonly record struct MoodDay(LocalDay Day, int Count, double Average, int Bucket);

/// <summary>
/// What a log of moods means: which bucket a day falls in, how long the run of logged days is, and
/// how to say either of those in words.
/// <para>
/// As with the habits, every function takes the day to reckon from. Nothing here reads a clock, so
/// the midnight and daylight-saving cases are ordinary arguments rather than something that can only
/// be reproduced twice a year.
/// </para>
/// <para>
/// The entry list is the trust boundary: it is a JSON array in the user's profile, so an entry with
/// a level outside the scale, or a stamp at the end of time, is <i>dropped</i> while its valid
/// siblings are kept. Nothing here throws, because it runs on the render path.
/// </para>
/// </summary>
public static class MoodRules
{
    public const int MinLevel = 1;

    public const int MaxLevel = 5;

    /// <summary>The five points of the scale, in the order they are offered.</summary>
    public static IReadOnlyList<string> LevelNames => LevelNamesIn(Strings.Current);

    /// <summary><see cref="LevelNames"/> in <paramref name="locale"/>.</summary>
    public static IReadOnlyList<string> LevelNamesIn(Locale locale)
        => [.. Enumerable.Range(MinLevel, MaxLevel - MinLevel + 1).Select(level => LevelName(locale, level))];

    /// <summary>The name of a level, for a tooltip, a button and a screen reader.</summary>
    public static string LevelName(int level) => LevelName(Strings.Current, level);

    /// <summary><see cref="LevelName(int)"/> in <paramref name="locale"/>.</summary>
    public static string LevelName(Locale locale, int level) => Strings.Get(locale, level switch
    {
        1 => Str.MoodTracker_Level_Awful,
        2 => Str.MoodTracker_Level_Bad,
        3 => Str.WidgetsB_Mood_LevelOk,
        4 => Str.MoodTracker_Level_Good,
        5 => Str.MoodTracker_Level_Great,
        _ => Str.WidgetsB_Mood_NotLogged,
    });

    /// <summary>Whether an entry is one this widget wrote and can still read.</summary>
    public static bool IsValid(MoodEntry? entry)
        => entry is not null && entry.Level >= MinLevel && entry.Level <= MaxLevel;

    /// <summary>
    /// The usable entries, newest first.
    /// <para>
    /// Sorted here once rather than at each of the four places that want an order, and returned as a
    /// list so the callers can walk it repeatedly without re-enumerating a query.
    /// </para>
    /// </summary>
    public static List<MoodEntry> Sanitise(IEnumerable<MoodEntry?>? entries)
    {
        var kept = new List<MoodEntry>();

        if (entries is null)
        {
            return kept;
        }

        foreach (var entry in entries)
        {
            // A level outside the scale is the one thing that cannot be rendered at all: it would
            // index past the five buckets. Dropped, rather than clamped into a mood the user never
            // chose.
            if (IsValid(entry))
            {
                kept.Add(entry!);
            }
        }

        kept.Sort(static (left, right) => right.At.CompareTo(left.At));
        return kept;
    }

    /// <summary>
    /// Fold the entries into one bag per day. Days with nothing on them are simply absent — the grid
    /// asks for the days it wants to draw, so filling in the gaps here would mean knowing the window
    /// twice.
    /// </summary>
    public static Dictionary<LocalDay, MoodDay> ByDay(IEnumerable<MoodEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var totals = new Dictionary<LocalDay, (int Count, int Sum)>();

        foreach (var entry in entries)
        {
            if (!IsValid(entry))
            {
                continue;
            }

            // The day the entry was written in, read out of its own offset.
            var day = LocalDay.From(entry.At);
            var running = totals.TryGetValue(day, out var existing) ? existing : default;
            totals[day] = (running.Count + 1, running.Sum + entry.Level);
        }

        var days = new Dictionary<LocalDay, MoodDay>(totals.Count);

        foreach (var (day, total) in totals)
        {
            var average = (double)total.Sum / total.Count;
            days[day] = new MoodDay(day, total.Count, average, Bucket(average));
        }

        return days;
    }

    /// <summary>
    /// The bucket an average falls in: 1 to 5, rounded away from zero so 2.5 reads as 3 rather than
    /// as 2. Never 0 — that bucket belongs to days with nothing logged and to nothing else.
    /// </summary>
    public static int Bucket(double average)
    {
        if (!double.IsFinite(average))
        {
            return MinLevel;
        }

        var rounded = (int)Math.Round(average, MidpointRounding.AwayFromZero);
        return Math.Clamp(rounded, MinLevel, MaxLevel);
    }

    /// <summary>What to draw for a day, whether or not anything was logged on it.</summary>
    public static MoodDay Read(IReadOnlyDictionary<LocalDay, MoodDay> days, LocalDay day)
    {
        ArgumentNullException.ThrowIfNull(days);
        return days.TryGetValue(day, out var found) ? found : new MoodDay(day, 0, 0, 0);
    }

    /// <summary>
    /// The run of consecutive days ending today on which something was logged.
    /// <para>
    /// The same bargain the habit streak makes: today being empty does not break the run, because
    /// the day is not over. A gap before that does.
    /// </para>
    /// </summary>
    public static int Streak(IReadOnlyDictionary<LocalDay, MoodDay> days, LocalDay today)
    {
        ArgumentNullException.ThrowIfNull(days);

        if (days.Count == 0)
        {
            return 0;
        }

        var cursor = days.ContainsKey(today) ? today : today.AddDays(-1);
        var streak = 0;

        while (streak <= days.Count && days.ContainsKey(cursor))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }

        return streak;
    }

    /// <summary>
    /// The mean level over every entry from <paramref name="from"/> to <paramref name="to"/>
    /// inclusive, or null when the window holds none.
    /// <para>
    /// Null rather than zero: zero is not a mood on this scale, and a footer reading "0.0" on a
    /// fortnight nobody logged is worse than one reading nothing at all.
    /// </para>
    /// </summary>
    public static double? Average(IEnumerable<MoodEntry> entries, LocalDay from, LocalDay to)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var count = 0;
        var sum = 0;

        foreach (var entry in entries)
        {
            if (!IsValid(entry))
            {
                continue;
            }

            var day = LocalDay.From(entry.At);

            if (day < from || day > to)
            {
                continue;
            }

            count++;
            sum += entry.Level;
        }

        return count == 0 ? null : (double)sum / count;
    }

    /// <summary>The most recent entry logged on a day, or null. What the thought box edits.</summary>
    public static MoodEntry? LatestOn(IReadOnlyList<MoodEntry> newestFirst, LocalDay day)
    {
        ArgumentNullException.ThrowIfNull(newestFirst);

        foreach (var entry in newestFirst)
        {
            if (IsValid(entry) && LocalDay.From(entry.At) == day)
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// Drop the oldest entries until the list is within <see cref="MoodTrackerConfig.MaxEntries"/>.
    /// Mutates in place, because the caller's list is the config's own and replacing it would put a
    /// second copy of the history in the bag.
    /// </summary>
    public static void Prune(List<MoodEntry> entries, int max)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (max < 1 || entries.Count <= max)
        {
            return;
        }

        // Oldest first, so the survivors are the newest regardless of what order the list arrived
        // in — a hand-edited file is under no obligation to be sorted.
        entries.Sort(static (left, right) => left.At.CompareTo(right.At));
        entries.RemoveRange(0, entries.Count - max);
    }

    /// <summary>
    /// A day's whole value as a sentence: the cell's accessible name and its tooltip.
    /// <para>
    /// This is the part that keeps the heatmap readable without colour. The wash on a cell is the
    /// fast channel; this is the exact one, and it is the only channel a screen reader has.
    /// </para>
    /// </summary>
    public static string Describe(MoodDay day) => Describe(Strings.Current, day);

    /// <summary><see cref="Describe(MoodDay)"/> in <paramref name="locale"/>.</summary>
    public static string Describe(Locale locale, MoodDay day)
    {
        var date = LocaleFormat.Date(locale, day.Day.Date, DateStyle.Full);

        if (day.Count == 0)
        {
            return Strings.Format(locale, Str.WidgetsB_Mood_DayNothing, ("date", date));
        }

        var level = Strings.Format(locale, Str.WidgetsB_Mood_LevelOf, ("level", LevelName(locale, day.Bucket)), ("value", day.Bucket), ("max", MaxLevel));

        return day.Count == 1
            ? Strings.Format(locale, Str.WidgetsB_Mood_DayOne, ("date", date), ("level", level))
            : Strings.Plural(locale, Str.WidgetsB_Mood_DayMany, day.Count,
                ("date", date), ("level", level), ("average", LocaleFormat.Number(locale, day.Average, 1)));
    }
}
