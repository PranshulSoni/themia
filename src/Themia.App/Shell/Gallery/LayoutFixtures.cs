#if DEBUG
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// Populated configs for the ported widgets whose default is an empty state, so the gallery can
/// show the layout a user actually sees once the widget has something in it. Reached through
/// <see cref="GalleryFixtures"/> (<c>--gallery-png Kind+fixture</c>).
/// <para>
/// Debug builds only. The weather, stock and feed fixtures are fixed answers seeded into their
/// caches ([S-731], [S-1305], [S-1311]); the rest are local and fixed.
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
        WidgetKind.Notes ||
        DeskFixtures.Has(kind);

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
                // [S-1311] A fixed mid-session quote with extended hours, seeded: offline, every run alike.
                return FeedStockFixture.StocksConfig();

            case WidgetKind.Rss:
                // [S-1305] Two fixed feeds, merged, seeded: offline, every run alike.
                return FeedStockFixture.RssConfig();

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
                        // [S-1383] Real shell icons, the bin's badge, a missing file ([S-1386]) and
                        // a refused target, so every state the tiles draw is in the fixture.
                        ShortcutItem.Create("shell:RecycleBinFolder"),
                        ShortcutItem.Create("shell:MyComputerFolder"),
                        ShortcutItem.Create("shell:Downloads"),
                        DemoAppLayout(),
                        ShortcutItem.Create(@"C:\Windows\notepad.exe", "Notepad"),
                        ShortcutItem.Create(@"C:\Windows", "Windows folder"),
                        ShortcutItem.Create("https://github.com", "GitHub"),
                        ShortcutItem.Create(@"C:\Windows\System32\calc.exe", "Calculator"),
                        ShortcutItem.Create("https://learn.microsoft.com", "Microsoft Learn"),
                        ShortcutItem.Create(@"C:\Windows\win.ini"),
                        ShortcutItem.Create(@"C:\Users\Public\Documents\Old report.docx"),
                        ShortcutItem.Create("shell:ControlPanelFolder"),
                        ShortcutItem.Create("javascript:alert(1)", "Tampered"),
                    ],
                };

            case WidgetKind.HabitTracker:
                // [S-1420] The habits live in a pool, not the config: the fixture is a data source.
                HabitsFixture.Install(populated: true);
                return HabitsFixture.Config();

            case WidgetKind.MoodTracker:
                // [S-1454] Twelve weeks with gaps, a few thoughts and two moods on some days, so the
                // large tiers' insights and recent thoughts have something to say.
                string[] thoughts = ["Long walk by the lake", "Deadline moved, finally", "Slept badly", "Dinner with Anna", "Rainy, stayed in"];

                // [S-1573] THEMIA_MOOD_SPAN=month renders the Month span (default Rolling).
                var mood = new MoodTrackerConfig
                {
                    Span = string.Equals(Environment.GetEnvironmentVariable("THEMIA_MOOD_SPAN"), "month", StringComparison.OrdinalIgnoreCase)
                        ? MoodSpan.Month
                        : MoodSpan.Rolling,
                };
                for (var day = 0; day <= 84; day++)
                {
                    if (day % 6 == 4)
                    {
                        continue;
                    }

                    mood.Entries.Add(new MoodEntry
                    {
                        Id = $"fixture-{day}",
                        At = now.AddDays(-day).AddHours(day == 0 ? 0 : -2),
                        Level = 1 + ((day * 7 + (day / 9)) % 5),
                        Note = day % 3 == 0 ? thoughts[(day / 3) % thoughts.Length] : string.Empty,
                    });

                    if (day % 5 == 1)
                    {
                        mood.Entries.Add(new MoodEntry { Id = $"fixture-{day}b", At = now.AddDays(-day).AddHours(-8), Level = 3 });
                    }
                }

                return mood;

            case WidgetKind.Clipboard:
                // [S-1443] The history is per widget and in memory: the fixture is a seed.
                ClipboardFixture.Install(populated: true);
                return new ClipboardConfig();

            case WidgetKind.Gallery:
                return new GalleryConfig { Path = @"C:\Windows\Web\Wallpaper", Recursive = true };

            case WidgetKind.Notes:
                // [S-1368] The notes live in a store, not the config: a throwaway one for the gallery.
                NotesFixture.Install(populated: true);
                return NotesFixture.Config();

            default:
                // [S-1489] Clock, Sun & moon, Quote and Scratchpad, in their own file.
                return DeskFixtures.Create(kind);
        }
    }

    /// <summary>
    /// [S-1405] An app layout for the Shortcuts fixture: three panes (an app, a site, a folder), so
    /// its tile shows the stacked pane icons. Never launched: the gallery's cards are previews.
    /// </summary>
    internal static ShortcutItem DemoAppLayout()
    {
        var item = ShortcutItem.CreateLayout("Morning");
        var root = item.Layout!;
        (root, _) = LayoutTree.Split(root, root.Id, SplitDirection.Vertical);
        LayoutTree.SetRatio(root, root.Id, 0.6);
        (root, _) = LayoutTree.Split(root, root.B!.Id, SplitDirection.Horizontal);
        LayoutTree.Assign(root, root.A!.Id, new LayoutAssignment { Source = LayoutSource.App, Target = @"C:\Windows\notepad.exe", Label = "Notepad" });
        LayoutTree.Assign(root, root.B!.A!.Id, new LayoutAssignment { Source = LayoutSource.Url, Target = "https://github.com" });
        LayoutTree.Assign(root, root.B!.B!.Id, new LayoutAssignment { Source = LayoutSource.Folder, Target = @"C:\Windows" });
        item.Layout = root;
        return item;
    }
}
#endif
