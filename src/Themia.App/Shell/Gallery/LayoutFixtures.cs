#if DEBUG
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// Populated configs for the ported widgets whose default is an empty state, so the gallery can
/// show the layout a user actually sees once the widget has something in it. Reached through
/// <see cref="GalleryFixtures"/> (<c>--gallery-png Kind+fixture</c>).
/// <para>
/// Debug builds only. The weather fixture is a fixed reading ([S-731]); the stock and feed
/// fixtures fetch from the same public services the widgets use in real life; the rest are local
/// and fixed.
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
        WidgetKind.Clipboard or
        WidgetKind.Shortcuts or
        WidgetKind.HabitTracker or
        WidgetKind.MoodTracker or
        WidgetKind.Gallery or
        WidgetKind.Notes;

    public static WidgetConfig? Create(WidgetKind kind)
    {
        var now = DateTimeOffset.Now;

        switch (kind)
        {
            case WidgetKind.WeatherNow:
                // [S-731] A fixed reading seeded into the cache: offline and the same every run.
                return WeatherFixture.NowConfig();

            case WidgetKind.WeatherForecast:
                return WeatherFixture.ForecastConfig();

            case WidgetKind.Stocks:
                return new StocksConfig { Symbol = "MSFT", DisplayName = "Microsoft" };

            case WidgetKind.Rss:
                return new RssConfig { FeedUrl = "https://feeds.bbci.co.uk/news/world/rss.xml" };

            case WidgetKind.Countdown:
                return new CountdownConfig
                {
                    Timers =
                    [
                        // [S-1448] Created stamps, so the large tiers draw their progress bars.
                        CountdownTimer.Create("Product launch", now.AddHours(3).AddMinutes(25), now.AddDays(-2)),
                        CountdownTimer.Create("Holiday", now.AddDays(12), now.AddDays(-30)),
                        CountdownTimer.Create("New Year", new DateTimeOffset(now.Year + 1, 1, 1, 0, 0, 0, now.Offset), now.AddDays(-200)),
                        CountdownTimer.Create("Dentist", now.AddDays(-2), now.AddDays(-20)),
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
                // [S-1420] The habits live in a pool, not the config: the fixture is a data source.
                HabitsFixture.Install(populated: true);
                return HabitsFixture.Config();

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

            case WidgetKind.Clipboard:
                // [S-1443] The history is per widget and in memory: the fixture is a seed.
                ClipboardFixture.Install(populated: true);
                return new ClipboardConfig();

            case WidgetKind.Gallery:
                return new GalleryConfig { Path = @"C:\Windows\Web\Wallpaper", Recursive = true };

            case WidgetKind.Notes:
                return new NotesConfig { Text = "Call the landlord about the boiler\nBook dentist (Tue or Thu)\nRenew passport before March\nIdeas: weekend in Lucerne, ask Anna" };

            default:
                return null;
        }
    }
}
#endif
