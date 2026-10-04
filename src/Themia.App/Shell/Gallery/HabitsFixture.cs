#if DEBUG
using Themia.App.Services;
using Themia.App.Services.Habits;
using Themia.Core.Habits;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-1420] An in-memory habit pool for the debug gallery. The plain page gets an empty pool — the
/// widget then makes its first list, exactly as on a fresh install — and the <c>+fixture</c> page a
/// list of five habits with a few weeks of history relative to today. Nothing is written.
/// </summary>
internal static class HabitsFixture
{
    private const string ListId = "fixture-habits";

    public static void Install(bool populated)
    {
        var today = LocalDay.From(DateTimeOffset.Now);

        HabitList[] lists = populated
            ?
            [
                new(ListId, "Habits",
                [
                    Habit("read", "Read 20 pages", today, 0, 1, 2, 4, 5, 6, 7, 8, 9, 12, 13, 15, 16, 20),
                    Habit("walk", "Walk", today, 1, 2, 3, 5, 6, 8, 9, 10, 11, 12, 14, 17, 18, 19, 21, 22),
                    Habit("phone", "No phone after 22:00", today, 0, 2, 3, 4, 5, 6, 10, 11, 13, 16, 18, 24, 25, 26),
                    Habit("water", "Two litres of water", today, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12),
                    Habit("stretch", "Stretch", today, 3, 7, 14, 15),
                ]),
            ]
            : [];

        AppServices.Habits = new HabitPool(store: null, lists);
    }

    public static HabitTrackerConfig Config() => new() { ListId = ListId };

    private static Habit Habit(string id, string name, LocalDay today, params int[] daysAgo)
        => new(id, name, [.. daysAgo.Select(ago => today.AddDays(-ago).Key)]);
}
#endif
