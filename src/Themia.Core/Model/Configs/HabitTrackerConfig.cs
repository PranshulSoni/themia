using System.Text.Json.Serialization;
using Themia.Core.Widgets;

namespace Themia.Core.Model;

/// <summary>
/// [S-1420] Which habit list a Habits widget shows, and how.
/// <para>
/// The habits themselves are not here. They live in the device-wide pool beside the state file
/// (<c>habits.json</c>, <see cref="Themia.Core.Habits.HabitListStore"/>), and this config only
/// points into it: two widgets can track the same list, a list outlives the widget that made it, and
/// copying a widget copies the pointer, never a second diverging copy of the history.
/// </para>
/// </summary>
public sealed class HabitTrackerConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.HabitTracker;

    /// <summary>
    /// The list in the pool, or empty for "not chosen yet" — the widget then takes the device's
    /// first list, creating one only if there is none. A list that was deleted is not replaced:
    /// the widget asks for one rather than showing somebody else's habits.
    /// </summary>
    public string ListId { get; set; } = string.Empty;

    /// <summary>[S-1422] How often the boxes clear. Per widget, as in the original: the log keeps every cadence's keys.</summary>
    public HabitPeriod Period { get; set; } = HabitPeriod.Daily;

    /// <summary>[S-1422] The custom window, in minutes (1–1440). Read through <see cref="Cadence"/>, which clamps.</summary>
    public int CustomMinutes { get; set; } = HabitCadence.DefaultCustomMinutes;

    /// <summary>
    /// [S-1426] How many past periods each row shows as dots. Zero removes the strip; a narrow tile
    /// shows fewer, dropping the oldest first.
    /// </summary>
    public int HistoryDots { get; set; } = 7;

    /// <summary>Show the current run beside each habit.</summary>
    public bool ShowStreak { get; set; } = true;

    [JsonIgnore]
    public int EffectiveHistoryDots => Math.Clamp(HistoryDots, 0, HabitRules.MaxHistory);

    [JsonIgnore]
    public HabitCadence Cadence => new(Enum.IsDefined(Period) ? Period : HabitPeriod.Daily, CustomMinutes);

    /// <summary>[S-1422] 15 s for the short cadences, a minute otherwise: only so a past period's tick clears by itself.</summary>
    [JsonIgnore]
    public TimeSpan TickInterval => HabitPeriods.TickInterval(Cadence.Period);

    public override WidgetConfig Clone() => new HabitTrackerConfig
    {
        ListId = ListId,
        Period = Period,
        CustomMinutes = CustomMinutes,
        HistoryDots = HistoryDots,
        ShowStreak = ShowStreak,
    };

    public override IReadOnlyList<string> Repair()
    {
        var repairs = new List<string>();

        if (!Enum.IsDefined(Period))
        {
            repairs.Add($"habit period {(int)Period} is unknown; reset to daily");
            Period = HabitPeriod.Daily;
        }

        if (CustomMinutes != Cadence.Minutes)
        {
            repairs.Add($"habit interval {CustomMinutes} min is out of range; clamped to {Cadence.Minutes}");
            CustomMinutes = Cadence.Minutes;
        }

        if (HistoryDots != EffectiveHistoryDots)
        {
            repairs.Add($"habit history {HistoryDots} is out of range; clamped to {EffectiveHistoryDots}");
            HistoryDots = EffectiveHistoryDots;
        }

        return repairs;
    }
}
