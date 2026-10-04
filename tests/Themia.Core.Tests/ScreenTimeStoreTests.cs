using Themia.Core.Storage;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

public sealed class ScreenTimeStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "themia-screentime-" + Guid.NewGuid().ToString("n"));

    private string StorePath => Path.Combine(_dir, ScreenTimeStore.FileName);

    private static LocalStamp D1(int seconds) => new(1_000_000 + seconds, "2026-07-01", seconds);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static ScreenTimeTracker ThirtySecondsOfChrome()
    {
        var t = new ScreenTimeTracker();
        t.OnForeground(D1(0), "chrome", "Google Chrome");
        t.OnTick(D1(30), 0);
        return t;
    }

    [Fact]
    public void S459_a_save_is_atomic_and_leaves_no_temp_file()
    {
        var t = ThirtySecondsOfChrome();

        Assert.True(ScreenTimeStore.WriteAtomic(StorePath, ScreenTimeStore.Serialize(t)));

        Assert.Equal([ScreenTimeStore.FileName], Directory.GetFiles(_dir).Select(Path.GetFileName));

        var (loaded, outcome) = ScreenTimeStore.Load(StorePath);
        Assert.Equal(ScreenTimeLoadOutcome.Loaded, outcome);
        Assert.Equal(30, loaded.Days["2026-07-01"]["chrome"]);
        Assert.Equal("Google Chrome", loaded.Names["chrome"]);
        Assert.False(loaded.Dirty);
    }

    /// <summary>
    /// Characterization: pins the exact bytes. Every other test reads back through the same writer,
    /// so a change to the shape would pass them all while orphaning existing histories. Change the
    /// literal only together with a schema version bump and a migration.
    /// </summary>
    [Fact]
    public void S459_the_on_disk_format_is_stable()
    {
        Assert.Equal(
            "{\"schemaVersion\":1,\"days\":{\"2026-07-01\":{\"chrome\":30}},\"names\":{\"chrome\":\"Google Chrome\"}}",
            ScreenTimeStore.Serialize(ThirtySecondsOfChrome()));
    }

    [Fact]
    public void S459_the_on_disk_format_with_paths_and_switches_is_stable()
    {
        var t = ThirtySecondsOfChrome();
        t.NoteExePath("chrome", "C:/apps/chrome.exe");
        t.OnForeground(D1(40), "code");

        Assert.Equal(
            "{\"schemaVersion\":1,\"days\":{\"2026-07-01\":{\"chrome\":40}},\"names\":{\"chrome\":\"Google Chrome\"},"
            + "\"paths\":{\"chrome\":\"C:/apps/chrome.exe\"},\"switches\":{\"2026-07-01\":1}}",
            ScreenTimeStore.Serialize(t));
    }

    [Fact]
    public void S459_corrupt_or_mismatched_files_are_refused()
    {
        Assert.Null(ScreenTimeStore.Parse("{not json"));
        Assert.Null(ScreenTimeStore.Parse("{\"schemaVersion\":999,\"days\":{},\"names\":{}}"));
        Assert.Null(ScreenTimeStore.Parse("{\"days\":{}}"));
        Assert.Null(ScreenTimeStore.Parse("{\"schemaVersion\":1,\"names\":{}}"));
        Assert.Null(ScreenTimeStore.Parse("null"));

        var ok = ScreenTimeStore.Parse("{\"schemaVersion\":1,\"days\":{\"2026-07-01\":{\"chrome\":12}},\"names\":{}}");
        Assert.NotNull(ok);
        Assert.Equal(12, ok.Days["2026-07-01"]["chrome"]);
    }

    [Fact]
    public void S459_an_absent_file_is_a_quiet_empty_history()
    {
        var (tracker, outcome) = ScreenTimeStore.Load(StorePath);
        Assert.Equal(ScreenTimeLoadOutcome.Fresh, outcome);
        Assert.Empty(tracker.Days);
        Assert.False(tracker.Dirty);
    }

    [Fact]
    public void S459_a_corrupt_file_is_set_aside_not_overwritten()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(StorePath, "{not json");

        var (tracker, outcome) = ScreenTimeStore.Load(StorePath);

        Assert.Equal(ScreenTimeLoadOutcome.Recovered, outcome);
        Assert.Empty(tracker.Days);
        Assert.False(File.Exists(StorePath));
        Assert.Equal("{not json", File.ReadAllText(StorePath + ".corrupt"));
    }

    [Fact]
    public void S459_junk_keys_are_dropped_on_load()
    {
        var t = ScreenTimeStore.Parse(
            "{\"schemaVersion\":1,\"days\":{\"2026-07-01\":{\"chrome\":10,\"\":5},\"zzz-junk\":{\"chrome\":7},\"2026-7-1\":{\"x\":1}},"
            + "\"names\":{},\"paths\":{\"chrome\":\"C:/c.exe\",\"\":\"C:/x.exe\",\"code\":\"\"},"
            + "\"switches\":{\"2026-07-01\":4,\"zzz\":9,\"2026-07-02\":0}}");

        Assert.NotNull(t);
        Assert.Equal(["2026-07-01"], t.Days.Keys);
        Assert.Equal(["chrome"], t.Days["2026-07-01"].Keys);
        Assert.Equal(["chrome"], t.Paths.Keys);
        Assert.Equal(["2026-07-01"], t.Switches.Keys);
        Assert.Equal(4, t.Switches["2026-07-01"]);
    }

    [Fact]
    public void S459_an_older_file_without_the_additive_maps_still_loads()
    {
        var t = ScreenTimeStore.Parse("{\"schemaVersion\":1,\"days\":{\"2026-07-01\":{\"chrome\":30}},\"names\":{\"chrome\":\"Google Chrome\"}}");
        Assert.NotNull(t);
        Assert.Empty(t.Paths);
        Assert.Empty(t.Switches);
    }

    [Fact]
    public void S458_names_written_before_the_naming_fixes_heal_on_load()
    {
        var t = ScreenTimeStore.Parse(
            "{\"schemaVersion\":1,\"days\":{},\"names\":{\"spotify\":\"Spotify\\u00008\\u0016\\u0001FileV\","
            + "\"whatsapp\":\"WhatsApp.Root\",\"chrome\":\"Google Chrome\",\"blank\":\"\\u0000x\"}}");

        Assert.NotNull(t);
        Assert.Equal("Spotify", t.Names["spotify"]);
        Assert.Equal("WhatsApp", t.Names["whatsapp"]);
        Assert.Equal("Google Chrome", t.Names["chrome"]);
        Assert.False(t.Names.ContainsKey("blank"));
    }

    [Fact]
    public void S459_saving_prunes_to_retention()
    {
        var t = new ScreenTimeTracker();
        var start = new LocalDay(new DateOnly(2026, 1, 1));

        for (var i = 0; i < ScreenTimeTracker.RetentionDays + 3; i++)
        {
            t.Add(start.AddDays(i).Key, "chrome", 1);
        }

        var reloaded = ScreenTimeStore.Parse(ScreenTimeStore.Serialize(t));
        Assert.NotNull(reloaded);
        Assert.Equal(ScreenTimeTracker.RetentionDays, reloaded.Days.Count);
    }
}
