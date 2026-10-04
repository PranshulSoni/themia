using System.Text.Json.Serialization;
using Themia.Core.Widgets;

namespace Themia.Core.Model;

/// <summary>
/// One habit, and the days it was done on.
/// <para>
/// The days live <b>inside</b> the habit rather than in a separate id-keyed map beside it. The
/// original app kept <c>habits</c> and <c>log</c> apart and needed a rule — and a test — saying that
/// deleting a habit must also delete its log, because otherwise a later habit that happened to reuse
/// the id inherited a dead streak. Holding the two together makes that rule unnecessary: there is no
/// way to remove the habit and keep the days, and no orphan to prune.
/// </para>
/// </summary>
public sealed class HabitRecord
{
    /// <summary>Stable identity, so renaming a habit keeps its history.</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The local calendar days this habit was completed on, as <c>YYYY-MM-DD</c>. Untrusted on
    /// read: <see cref="HabitRules.Days"/> is the boundary that parses it.
    /// </summary>
    public List<string> Days { get; set; } = [];

    /// <summary>A fresh id for a newly added habit. Opaque; nothing reads anything out of it.</summary>
    public static string NewId() => Guid.NewGuid().ToString("n");

    public HabitRecord Copy() => new()
    {
        Id = Id,
        Name = Name,

        // A new list, not the same one: a cloned config that shared its day lists would have two
        // widgets ticking each other's boxes.
        Days = [.. Days],
    };
}

/// <summary>
/// A short list of habits, a box to tick each day, and how long the run is.
/// <para>
/// The data <b>is</b> the config. There is no habit store and no sync, so a habit list travels with
/// copy and paste, with the view it sits in, and with a backup of the state file — and the widget
/// writes its own config when a box is ticked, which few widgets here do.
/// </para>
/// </summary>
public sealed class HabitTrackerConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.HabitTracker;

    public List<HabitRecord> Habits { get; set; } = [];

    /// <summary>
    /// How many past days each row shows as squares. Zero removes the strip from the row entirely,
    /// rather than leaving an empty element still claiming its width — a habit list is allowed to be
    /// just a list.
    /// </summary>
    public int HistoryDays { get; set; } = 7;

    /// <summary>Show the current run beside each habit.</summary>
    public bool ShowStreak { get; set; } = true;

    [JsonIgnore]
    public int EffectiveHistoryDays => Math.Clamp(HistoryDays, 0, HabitRules.MaxHistoryDays);

    /// <summary>
    /// One wake a minute, and only so the widget rolls over at midnight without being touched: a box
    /// ticked yesterday has to clear itself when the day does. Nothing else here moves with time.
    /// </summary>
    [JsonIgnore]
    public TimeSpan TickInterval => TimeSpan.FromMinutes(1);

    public override WidgetConfig Clone()
    {
        var copy = new HabitTrackerConfig
        {
            HistoryDays = HistoryDays,
            ShowStreak = ShowStreak,
        };

        foreach (var habit in Habits)
        {
            copy.Habits.Add(habit.Copy());
        }

        return copy;
    }
}
