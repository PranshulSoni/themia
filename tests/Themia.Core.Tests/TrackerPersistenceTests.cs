using Themia.Core.Geometry;
using Themia.Core.Model;
using Themia.Core.Storage;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The trackers keep their data <b>in their config</b> (habits keep their settings there and their
/// history in <c>habits.json</c>), which means the state file is the only copy of every mood ever
/// logged. So the round trip through the real store is pinned here rather than assumed.
/// <para>
/// It matters more than for a widget whose config is a handful of switches: this is the test that
/// catches a nested list the source-generated serializer never learned about, which under Native AOT
/// is not a compile error but a widget that silently comes back empty on the next launch.
/// </para>
/// </summary>
public sealed class TrackerPersistenceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public TrackerPersistenceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "themia-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "state.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // ignore: temp cleanup is best effort and must not fail a test run
        }
    }

    private AppStateStore NewStore() => new(_path, TimeSpan.Zero);

    /// <summary>Save a widget of this kind with its config filled in, and read it back.</summary>
    private async Task<T> RoundTripAsync<T>(WidgetKind kind, Action<T> fill)
        where T : WidgetConfig
    {
        var state = AppState.CreateDefault();
        var view = state.ActiveView();
        var widget = WidgetInstance.Create(kind, new FracRect(0.1, 0.1, 0.2, 0.2));

        fill((T)widget.Config);
        view.Widgets.Add(widget);

        var store = NewStore();
        store.SaveSoon(state);
        await store.FlushAsync();

        var loaded = NewStore().Load();

        Assert.Equal(LoadOutcome.Loaded, loaded.Outcome);

        var reloaded = loaded.State.ActiveView().Widgets.Single(w => w.Kind == kind);
        return Assert.IsType<T>(reloaded.Config);
    }

    // -----------------------------------------------------------------------------------------
    // [S-305] gallery
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task S305_a_gallery_keeps_every_setting_across_a_restart()
    {
        var config = await RoundTripAsync<GalleryConfig>(WidgetKind.Gallery, gallery =>
        {
            gallery.Path = @"C:\Users\me\Pictures";
            gallery.Recursive = true;
            gallery.Layout = GalleryLayout.Grid;
            gallery.Fit = GalleryFit.Fit;
            gallery.Shuffle = false;
            gallery.SlideshowSeconds = 45;
            gallery.MaxImages = 33;
        });

        Assert.Equal(@"C:\Users\me\Pictures", config.Path);
        Assert.True(config.Recursive);
        Assert.Equal(GalleryLayout.Grid, config.Layout);
        Assert.Equal(GalleryFit.Fit, config.Fit);
        Assert.False(config.Shuffle);
        Assert.Equal(45, config.SlideshowSeconds);
        Assert.Equal(33, config.MaxImages);
    }

    // -----------------------------------------------------------------------------------------
    // [S-1420] habits: the config is a pointer and the widget's own settings; the habits live in
    // habits.json (HabitListStoreTests)
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task S1420_a_habit_widgets_settings_survive_a_restart()
    {
        var config = await RoundTripAsync<HabitTrackerConfig>(WidgetKind.HabitTracker, habits =>
        {
            habits.ListId = "list-morning";
            habits.Period = HabitPeriod.Custom;
            habits.CustomMinutes = 45;
            habits.HistoryDots = 14;
            habits.ShowStreak = false;
        });

        Assert.Equal("list-morning", config.ListId);
        Assert.Equal(HabitPeriod.Custom, config.Period);
        Assert.Equal(45, config.CustomMinutes);
        Assert.Equal(14, config.HistoryDots);
        Assert.False(config.ShowStreak);
    }

    [Fact]
    public void S1422_a_hand_edited_habit_config_is_repaired_to_its_ranges()
    {
        var config = new HabitTrackerConfig { Period = (HabitPeriod)99, CustomMinutes = 0, HistoryDots = 500 };

        var repairs = config.Repair();

        Assert.Equal(3, repairs.Count);
        Assert.Equal(HabitPeriod.Daily, config.Period);
        Assert.Equal(HabitCadence.MinCustomMinutes, config.CustomMinutes);
        Assert.Equal(HabitRules.MaxHistory, config.HistoryDots);
    }

    // -----------------------------------------------------------------------------------------
    // [S-323] moods
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task S323_a_mood_keeps_its_offset_and_so_keeps_its_day()
    {
        // Eleven at night at +02:00. If the offset were dropped on the way through the file, this
        // would come back as the following day in UTC and move on the heatmap.
        var logged = new DateTimeOffset(2026, 6, 15, 23, 30, 0, TimeSpan.FromHours(2));

        var config = await RoundTripAsync<MoodTrackerConfig>(WidgetKind.MoodTracker, mood =>
        {
            mood.Span = MoodSpan.Month;
            mood.WeekStart = MoodWeekStart.Saturday;
            mood.HistoryWeeks = 26;
            mood.ShowNote = false;
            mood.ShowStats = false;

            mood.Entries.Add(new MoodEntry { Id = "a", At = logged, Level = 4, Note = "long day" });
        });

        Assert.Equal(MoodSpan.Month, config.Span);
        Assert.Equal(MoodWeekStart.Saturday, config.WeekStart);
        Assert.Equal(DayOfWeek.Saturday, config.FirstDayOfWeek);
        Assert.Equal(26, config.HistoryWeeks);
        Assert.False(config.ShowNote);
        Assert.False(config.ShowStats);

        var entry = Assert.Single(config.Entries);
        Assert.Equal(4, entry.Level);
        Assert.Equal("long day", entry.Note);
        Assert.Equal(logged.Offset, entry.At.Offset);
        Assert.Equal("2026-06-15", LocalDay.From(entry.At).Key);
    }

    [Fact]
    public void S323_cloning_a_mood_log_does_not_share_its_entries()
    {
        var original = new MoodTrackerConfig();
        original.Entries.Add(new MoodEntry { Id = "a", At = DateTimeOffset.UnixEpoch, Level = 3, Note = "first" });

        var copy = (MoodTrackerConfig)original.Clone();
        copy.Entries[0].Note = "changed";
        copy.Entries.Add(new MoodEntry { Id = "b", At = DateTimeOffset.UnixEpoch, Level = 5 });

        Assert.Equal("first", original.Entries[0].Note);
        Assert.Single(original.Entries);
        Assert.Equal(2, copy.Entries.Count);
    }
}
