using System.Globalization;
using System.Text;
using Themia.Core.Model;

namespace Themia.Core.Widgets;

/// <summary>
/// [S-1450] [S-1451] [S-1454] What a large Mood tile says beside its heatmap, and what a day says when
/// it is hovered or right-clicked. Every function takes its window explicitly and reads no clock.
/// </summary>
public static class MoodInsights
{
    /// <summary>
    /// [S-1454] How many entries of each level fall in <paramref name="from"/>..<paramref name="to"/>:
    /// index 0 is level 1 (awful) … index 4 level 5 (great). Invalid entries are not counted.
    /// </summary>
    public static int[] Distribution(IEnumerable<MoodEntry> entries, LocalDay from, LocalDay to)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var counts = new int[MoodRules.MaxLevel];

        foreach (var entry in entries)
        {
            if (MoodRules.IsValid(entry) && InWindow(entry, from, to))
            {
                counts[entry.Level - MoodRules.MinLevel]++;
            }
        }

        return counts;
    }

    /// <summary>
    /// [S-1454] The mean level for each weekday in the window, in the order a week starting on
    /// <paramref name="weekStart"/> runs; null for a weekday with nothing logged.
    /// </summary>
    public static double?[] WeekdayAverages(IEnumerable<MoodEntry> entries, LocalDay from, LocalDay to, DayOfWeek weekStart)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var sums = new int[7];
        var counts = new int[7];

        foreach (var entry in entries)
        {
            if (!MoodRules.IsValid(entry) || !InWindow(entry, from, to))
            {
                continue;
            }

            var column = Column(LocalDay.From(entry.At).DayOfWeek, weekStart);
            sums[column] += entry.Level;
            counts[column]++;
        }

        var averages = new double?[7];

        for (var i = 0; i < 7; i++)
        {
            averages[i] = counts[i] == 0 ? null : (double)sums[i] / counts[i];
        }

        return averages;
    }

    /// <summary>
    /// [S-1454] The newest entries that carry a thought, newest first, at most <paramref name="max"/>.
    /// </summary>
    public static List<MoodEntry> RecentNotes(IReadOnlyList<MoodEntry> newestFirst, int max)
    {
        ArgumentNullException.ThrowIfNull(newestFirst);

        var notes = new List<MoodEntry>();

        foreach (var entry in newestFirst)
        {
            if (notes.Count >= max)
            {
                break;
            }

            if (MoodRules.IsValid(entry) && !string.IsNullOrWhiteSpace(entry.Note))
            {
                notes.Add(entry);
            }
        }

        return notes;
    }

    /// <summary>[S-1451] Every entry logged on <paramref name="day"/>, oldest first (the order they happened).</summary>
    public static List<MoodEntry> EntriesOn(IEnumerable<MoodEntry> entries, LocalDay day)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var found = new List<MoodEntry>();

        foreach (var entry in entries)
        {
            if (MoodRules.IsValid(entry) && LocalDay.From(entry.At) == day)
            {
                found.Add(entry);
            }
        }

        found.Sort(static (left, right) => left.At.CompareTo(right.At));
        return found;
    }

    /// <summary>
    /// [S-1451] Delete the entry with <paramref name="id"/> from the config's own list (in place).
    /// Returns whether anything was removed; an empty or unknown id removes nothing.
    /// </summary>
    public static bool Delete(List<MoodEntry> entries, string id)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return !string.IsNullOrEmpty(id) && entries.RemoveAll(entry => entry is not null && entry.Id == id) > 0;
    }

    /// <summary>[S-1451] One entry in a few words: "21:40 · Good: long walk".</summary>
    public static string EntryLine(MoodEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var time = $"{entry.At.Hour:D2}:{entry.At.Minute:D2}";
        var level = MoodRules.LevelName(entry.Level);

        return string.IsNullOrWhiteSpace(entry.Note) ? $"{time} · {level}" : $"{time} · {level}: {entry.Note.Trim()}";
    }

    /// <summary>
    /// [S-1450] A day's tooltip: its sentence (date, level, average), then one line per entry with the
    /// thought attached (the original S-12).
    /// </summary>
    public static string Tooltip(MoodDay day, IReadOnlyList<MoodEntry> dayEntries)
    {
        ArgumentNullException.ThrowIfNull(dayEntries);

        var text = new StringBuilder(MoodRules.Describe(day));

        foreach (var entry in dayEntries)
        {
            text.Append('\n').Append(EntryLine(entry));
        }

        return text.ToString();
    }

    /// <summary>[S-1454] A recent-thought row: "Tue 29 Sep · Good: long walk".</summary>
    public static string RecentLine(MoodEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var day = LocalDay.From(entry.At);
        var date = $"{ShortDays[(int)day.DayOfWeek]} {day.Day.ToString(CultureInfo.InvariantCulture)} {ShortMonths[day.Month - 1]}";

        return $"{date} · {MoodRules.LevelName(entry.Level)}: {entry.Note.Trim()}";
    }

    /// <summary>Where a weekday sits in a week that starts on <paramref name="weekStart"/>, 0..6.</summary>
    public static int Column(DayOfWeek day, DayOfWeek weekStart) => (((int)day - (int)weekStart) + 7) % 7;

    private static bool InWindow(MoodEntry entry, LocalDay from, LocalDay to)
    {
        var day = LocalDay.From(entry.At);
        return day >= from && day <= to;
    }

    private static readonly string[] ShortDays = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    private static readonly string[] ShortMonths =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
}
