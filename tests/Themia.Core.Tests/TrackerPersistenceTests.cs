using Themia.Core.Geometry;
using Themia.Core.Model;
using Themia.Core.Storage;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The three trackers keep their data <b>in their config</b>, which means the state file is the only
/// copy of a habit's whole history and of every mood ever logged. So the round trip through the
/// real store is pinned here rather than assumed.
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
    // [S-314] habits
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task S314_a_habits_whole_history_survives_a_restart()
    {
        var config = await RoundTripAsync<HabitTrackerConfig>(WidgetKind.HabitTracker, habits =>
        {
            habits.HistoryDays = 14;
            habits.ShowStreak = false;

            habits.Habits.Add(new HabitRecord
            {
                Id = "walk",
                Name = "Walk the dog",
                Days = ["2026-06-13", "2026-06-14", "2026-06-15"],
            });

            habits.Habits.Add(new HabitRecord { Id = "read", Name = "Read" });
        });

        Assert.Equal(14, config.HistoryDays);
        Assert.False(config.ShowStreak);
        Assert.Equal(2, config.Habits.Count);

        Assert.Equal("walk", config.Habits[0].Id);
        Assert.Equal("Walk the dog", config.Habits[0].Name);
        Assert.Equal(new[] { "2026-06-13", "2026-06-14", "2026-06-15" }, config.Habits[0].Days);
        Assert.Empty(config.Habits[1].Days);

        // The streak has to survive too, which is the whole point of persisting the days.
        var today = new LocalDay(new DateOnly(2026, 6, 15));
        Assert.Equal(3, HabitRules.Streak(HabitRules.Days(config.Habits[0].Days, today), today));
    }

    [Fact]
    public void S314_cloning_a_habit_list_does_not_share_its_days()
    {
        var original = new HabitTrackerConfig();
        original.Habits.Add(new HabitRecord { Id = "walk", Name = "Walk", Days = ["2026-06-15"] });

        var copy = (HabitTrackerConfig)original.Clone();
        copy.Habits[0].Days.Add("2026-06-14");
        copy.Habits[0].Name = "Jog";

        // Two widgets pasted from one must not tick each other's boxes.
        Assert.Single(original.Habits[0].Days);
        Assert.Equal("Walk", original.Habits[0].Name);
        Assert.Equal(2, copy.Habits[0].Days.Count);
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
