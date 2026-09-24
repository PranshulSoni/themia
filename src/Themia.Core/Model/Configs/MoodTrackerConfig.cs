using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Which stretch of the calendar the heatmap covers.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MoodSpan>))]
public enum MoodSpan
{
    /// <summary>The weeks up to and including this one, as many as the tile has room for.</summary>
    Rolling = 0,

    /// <summary>One calendar month, whole, with the days either side of it left blank.</summary>
    Month = 1,
}

/// <summary>
/// Which day a week starts on.
/// <para>
/// A closed set rather than a <see cref="DayOfWeek"/>, because only three of the seven are a week
/// start anybody uses and a settings file should not be able to ask for a week that begins on a
/// Wednesday. It exists at all because <c>InvariantGlobalization</c> leaves no culture to read the
/// answer from: the invariant culture says Sunday to everyone, which is wrong for most of the world,
/// so the user is asked instead of guessed at.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<MoodWeekStart>))]
public enum MoodWeekStart
{
    Monday = 0,
    Sunday = 1,
    Saturday = 2,
}

/// <summary>
/// One logged mood: when, how it felt, and the thought behind it.
/// <para>
/// <see cref="At"/> keeps the offset it was written with, and the day it counts for is read out of
/// that offset rather than out of the current one. A mood logged at eleven at night belongs to that
/// evening for good, and must not slide onto the day before because the laptop has since been
/// carried west.
/// </para>
/// </summary>
public sealed class MoodEntry
{
    public string Id { get; set; } = string.Empty;

    public DateTimeOffset At { get; set; }

    /// <summary>1 (awful) to 5 (great). A number, so the scale cannot be re-interpreted under existing history.</summary>
    public int Level { get; set; }

    public string Note { get; set; } = string.Empty;

    public static string NewId() => Guid.NewGuid().ToString("n");

    public MoodEntry Copy() => new()
    {
        Id = Id,
        At = At,
        Level = Level,
        Note = Note,
    };
}

/// <summary>
/// How you felt, logged in a tap, read back as a calendar heatmap.
/// <para>
/// Like the habit list, the data is the config: the entries live in this bag and nowhere else, so
/// they travel with the widget and there is nothing to sync. That is also why they are capped — the
/// state file is rewritten whenever any widget on the desktop is moved, and an unbounded log inside
/// it would make every drag more expensive than the last.
/// </para>
/// </summary>
public sealed class MoodTrackerConfig : WidgetConfig
{
    /// <summary>
    /// The most entries kept. At several a day this is years of history, and it bounds what a widget
    /// drag has to rewrite. Past it the oldest are dropped.
    /// </summary>
    public const int MaxEntries = 2000;

    public const int MinHistoryWeeks = 2;

    /// <summary>A year of weeks, and the ceiling on the number of rows a render loop can be asked for.</summary>
    public const int MaxHistoryWeeks = 53;

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.MoodTracker;

    public List<MoodEntry> Entries { get; set; } = [];

    public MoodSpan Span { get; set; } = MoodSpan.Rolling;

    public MoodWeekStart WeekStart { get; set; } = MoodWeekStart.Monday;

    /// <summary>
    /// The most weeks the rolling heatmap will show. It is a ceiling, not a count: the tile shows as
    /// many of them as it can draw at a legible cell size, which is how the same widget reads at a
    /// quarter of the screen and at a postage stamp.
    /// </summary>
    public int HistoryWeeks { get; set; } = 12;

    /// <summary>Show the box for the thought behind today's mood.</summary>
    public bool ShowNote { get; set; } = true;

    /// <summary>Show the run of logged days and the average under the heatmap.</summary>
    public bool ShowStats { get; set; } = true;

    [JsonIgnore]
    public int EffectiveHistoryWeeks => Math.Clamp(HistoryWeeks, MinHistoryWeeks, MaxHistoryWeeks);

    /// <summary>The week start as the calendar functions want it.</summary>
    [JsonIgnore]
    public DayOfWeek FirstDayOfWeek => WeekStart switch
    {
        MoodWeekStart.Sunday => DayOfWeek.Sunday,
        MoodWeekStart.Saturday => DayOfWeek.Saturday,
        _ => DayOfWeek.Monday,
    };

    /// <summary>One wake a minute, and only so the window rolls over at midnight untouched.</summary>
    [JsonIgnore]
    public TimeSpan TickInterval => TimeSpan.FromMinutes(1);

    public override WidgetConfig Clone()
    {
        var copy = new MoodTrackerConfig
        {
            Span = Span,
            WeekStart = WeekStart,
            HistoryWeeks = HistoryWeeks,
            ShowNote = ShowNote,
            ShowStats = ShowStats,
        };

        foreach (var entry in Entries)
        {
            copy.Entries.Add(entry.Copy());
        }

        return copy;
    }
}
