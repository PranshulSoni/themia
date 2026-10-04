#if DEBUG
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// Populated configs for the ported widgets whose default is an empty state, so the gallery can
/// show the layout a user actually sees once the widget has something in it. Reached through
/// <see cref="GalleryFixtures"/> (<c>--gallery-png Kind+fixture</c>).
/// <para>
/// Debug builds only. The weather, stock and feed fixtures fetch from the same public services the
/// widgets use in real life; the rest are local and fixed.
/// </para>
/// </summary>
internal static class LayoutFixtures
{
    public static bool Has(WidgetKind kind) => kind is
        WidgetKind.WeatherNow or
        WidgetKind.WeatherForecast or
        WidgetKind.Stocks or
        WidgetKind.Rss or
        WidgetKind.Countdown or
        WidgetKind.Shortcuts or
        WidgetKind.HabitTracker or
        WidgetKind.MoodTracker or
        WidgetKind.Gallery or
        WidgetKind.Notes;

    public static WidgetConfig? Create(WidgetKind kind)
    {
        var now = DateTimeOffset.Now;
        var today = LocalDay.From(now);

        switch (kind)
        {
            case WidgetKind.WeatherNow:
                return new WeatherNowConfig { Place = "Zurich", LocationName = "Zurich", Latitude = 47.37, Longitude = 8.54 };

            case WidgetKind.WeatherForecast:
                return new WeatherForecastConfig { Place = "Zurich", LocationName = "Zurich", Latitude = 47.37, Longitude = 8.54 };

            case WidgetKind.Stocks:
                return new StocksConfig { Symbol = "MSFT", DisplayName = "Microsoft" };

            case WidgetKind.Rss:
                return new RssConfig { FeedUrl = "https://feeds.bbci.co.uk/news/world/rss.xml" };

            case WidgetKind.Countdown:
                return new CountdownConfig
                {
                    Timers =
                    [
                        CountdownTimer.Create("Product launch", now.AddHours(3).AddMinutes(25)),
                        CountdownTimer.Create("Holiday", now.AddDays(12)),
                        CountdownTimer.Create("New Year", new DateTimeOffset(now.Year + 1, 1, 1, 0, 0, 0, now.Offset)),
                    ],
                };

            case WidgetKind.Shortcuts:
                return new ShortcutsConfig
                {
                    Items =
                    [
                        ShortcutItem.Create(@"C:\Windows\notepad.exe", "Notepad"),
                        ShortcutItem.Create(@"C:\Windows", "Windows folder"),
                        ShortcutItem.Create("https://github.com", "GitHub"),
                        ShortcutItem.Create(@"C:\Windows\System32\calc.exe", "Calculator"),
                        ShortcutItem.Create("https://learn.microsoft.com", "Microsoft Learn"),
                    ],
                };

            case WidgetKind.HabitTracker:
                return new HabitTrackerConfig
                {
                    Habits =
                    [
                        Habit("Read 20 pages", today, 0, 1, 2, 4, 5),
                        Habit("Walk", today, 1, 2, 3),
                        Habit("No phone after 22:00", today, 0, 2, 3, 4, 5, 6),
                    ],
                };

            case WidgetKind.MoodTracker:
                var mood = new MoodTrackerConfig();
                for (var day = 1; day <= 20; day++)
                {
                    mood.Entries.Add(new MoodEntry
                    {
                        Id = $"fixture-{day}",
                        At = now.AddDays(-day),
                        Level = 1 + ((day * 7) % 5),
                    });
                }

                return mood;

            case WidgetKind.Gallery:
                return new GalleryConfig { Path = @"C:\Windows\Web\Wallpaper", Recursive = true };

            case WidgetKind.Notes:
                return new NotesConfig { Text = "Call the landlord about the boiler\nBook dentist (Tue or Thu)\nRenew passport before March\nIdeas: weekend in Lucerne, ask Anna" };

            default:
                return null;
        }
    }

    private static HabitRecord Habit(string name, LocalDay today, params int[] daysAgo) => new()
    {
        Id = HabitRecord.NewId(),
        Name = name,
        Days = [.. daysAgo.Select(ago => today.AddDays(-ago).Key)],
    };
}
#endif
