using Themia.Core.Storage;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

public sealed class ScreenTimeTrackerTests
{
    private const long Day1Midnight = 1_000_000;
    private const string Day1 = "2026-07-01";
    private const string Day2 = "2026-07-02";

    private static LocalStamp D1(int seconds) => new(Day1Midnight + seconds, Day1, seconds);

    private static LocalStamp D2(int seconds) => new(Day1Midnight + 86_400 + seconds, Day2, seconds);

    private static long SecondsOf(ScreenTimeTracker tracker, string date, string app)
        => tracker.Days.TryGetValue(date, out var per) && per.TryGetValue(app, out var seconds) ? seconds : 0;

    // ---- accrual [S-458] ----------------------------------------------------------------------

    [Fact]
    public void S458_a_foreground_change_switches_bucket()
    {
        var t = new ScreenTimeTracker();
        t.OnForeground(D1(100), "chrome");
        t.OnForeground(D1(160), "code");
        t.OnTick(D1(190), 0);

        Assert.Equal(60, SecondsOf(t, Day1, "chrome"));
        Assert.Equal(30, SecondsOf(t, Day1, "code"));
    }

    [Fact]
    public void S458_names_are_kept_and_unnamed_apps_are_prettified()
    {
        var t = new ScreenTimeTracker();
        t.OnForeground(D1(0), "chrome", "Google Chrome");
        t.OnForeground(D1(10), "code");
        t.OnTick(D1(20), 0);

        var day = t.Day(Day1, null);
        Assert.Equal("Google Chrome", day.Apps.Single(a => a.App == "chrome").Name);
        Assert.Equal("Code", day.Apps.Single(a => a.App == "code").Name);
        Assert.Equal("Unknown", AppNames.Prettify(string.Empty));
    }

    [Fact]
    public void S458_ticks_accumulate_elapsed_time()
    {
        var t = new ScreenTimeTracker();
        t.OnForeground(D1(0), "chrome");
        t.OnTick(D1(30), 0);
        t.OnTick(D1(60), 0);

        Assert.Equal(60, SecondsOf(t, Day1, "chrome"));
        Assert.True(t.Dirty);
    }

    [Fact]
    public void S458_lock_pauses_and_only_unlock_resumes()
    {
        var t = new ScreenTimeTracker();
        t.OnForeground(D1(0), "chrome");
        t.OnLock(D1(50));
        t.OnTick(D1(80), 0);
        t.OnUnlock(D1(100));
        t.OnTick(D1(130), 0);

        Assert.Equal(50 + 30, SecondsOf(t, Day1, "chrome"));
    }

    [Fact]
    public void S458_a_switch_while_locked_moves_the_app_but_accrues_nothing()
    {
        var t = new ScreenTimeTracker();
        t.OnForeground(D1(0), "chrome");
        t.OnLock(D1(50));
        t.OnForeground(D1(60), "code");
        t.OnUnlock(D1(100));
        t.OnTick(D1(130), 0);

        Assert.Equal(50, SecondsOf(t, Day1, "chrome"));
        Assert.Equal(30, SecondsOf(t, Day1, "code"));
    }

    [Fact]
    public void S458_idle_pauses_and_input_resumes_without_crediting_the_gap()
    {
        var t = new ScreenTimeTracker();
        t.OnForeground(D1(0), "chrome");
        t.OnTick(D1(30), 0);

        for (var i = 1; i <= 10; i++)
        {
            t.OnTick(D1(30 + (i * 30)), i * 30); // idle reaches 300 at t=330
        }

        Assert.Equal(330, SecondsOf(t, Day1, "chrome"));
        Assert.Equal(ScreenTimePause.Idle, t.Paused);

        t.OnTick(D1(360), 330); // still idle
        t.OnTick(D1(400), 5);   // back: resumes from 400
        t.OnTick(D1(430), 10);

        Assert.Equal(330 + 30, SecondsOf(t, Day1, "chrome"));
    }

    [Fact]
    public void S458_an_interval_across_midnight_credits_both_days()
    {
        var t = new ScreenTimeTracker();
        t.OnForeground(D1(86_360), "chrome");
        t.OnTick(D2(20), 0);

        Assert.Equal(40, SecondsOf(t, Day1, "chrome"));
        Assert.Equal(20, SecondsOf(t, Day2, "chrome"));
    }

    [Fact]
    public void S458_split_seconds_cases()
    {
        Assert.Equal(20, ScreenTimeTracker.SplitSeconds(D1(86_380), D2(30), Day1));
        Assert.Equal(30, ScreenTimeTracker.SplitSeconds(D1(86_380), D2(30), Day2));
        Assert.Equal(0, ScreenTimeTracker.SplitSeconds(D1(86_380), D2(30), "2026-06-30"));
        Assert.Equal(15, ScreenTimeTracker.SplitSeconds(D1(10), D1(25), Day1));
        Assert.Equal(0, ScreenTimeTracker.SplitSeconds(D1(25), D1(10), Day1));
    }

    [Fact]
    public void S458_a_suspend_gap_is_never_credited()
    {
        var t = new ScreenTimeTracker();
        t.OnForeground(D1(0), "chrome");
        t.OnTick(D1(30), 0);

        // The lid closed for four hours: none of it is chrome.
        t.OnTick(D1(30 + (4 * 3600)), 5);
        Assert.Equal(30, SecondsOf(t, Day1, "chrome"));

        t.OnTick(D1(30 + (4 * 3600) + 30), 10);
        Assert.Equal(60, SecondsOf(t, Day1, "chrome"));

        // Friday to Monday credits nothing to either day.
        t.OnForeground(D1(86_000), "code");
        t.OnTick(D2(80_000), 5);
        Assert.Equal(0, SecondsOf(t, Day1, "code"));
        Assert.Equal(0, SecondsOf(t, Day2, "code"));
    }

    [Fact]
    public void S458_themias_own_window_closes_the_interval()
    {
        var t = new ScreenTimeTracker();
        t.OnForeground(D1(0), "chrome");
        t.OnTick(D1(30), 0);
        t.OnOwnForeground(D1(30));
        t.OnTick(D1(60), 0);
        t.OnTick(D1(90), 0);

        Assert.Equal(30, SecondsOf(t, Day1, "chrome"));
        Assert.Null(t.OpenApp);

        t.OnForeground(D1(100), "chrome");
        t.OnTick(D1(130), 0);
        Assert.Equal(60, SecondsOf(t, Day1, "chrome"));
    }

    [Fact]
    public void S453_counts_real_foreground_changes_only()
    {
        var t = new ScreenTimeTracker();

        // The first app of a session is a start, not a switch.
        t.OnForeground(D1(0), "chrome");
        Assert.Equal(0, t.Day(Day1, null).Switches);

        t.OnForeground(D1(10), "code");
        t.OnForeground(D1(20), "chrome");
        Assert.Equal(2, t.Day(Day1, null).Switches);

        // The hook repeating the same app is not a switch.
        t.OnForeground(D1(30), "chrome");
        Assert.Equal(2, t.Day(Day1, null).Switches);

        // Behind a lock is not the user switching.
        t.OnLock(D1(40));
        t.OnForeground(D1(50), "code");
        Assert.Equal(2, t.Day(Day1, null).Switches);
        t.OnUnlock(D1(60));
        t.OnForeground(D1(70), "chrome");
        Assert.Equal(3, t.Day(Day1, null).Switches);

        // Counted per date.
        t.OnForeground(D2(10), "code");
        Assert.Equal(1, t.Day(Day2, null).Switches);
        Assert.Equal(3, t.Day(Day1, null).Switches);
    }

    // ---- reading [S-454] ------------------------------------------------------------------------

    [Fact]
    public void S454_a_day_is_busiest_first_and_includes_the_open_interval()
    {
        var t = new ScreenTimeTracker();
        t.OnForeground(D1(0), "code");
        t.OnTick(D1(30), 0);
        t.OnTick(D1(60), 0);
        t.OnTick(D1(100), 0);
        t.OnForeground(D1(100), "chrome");

        var day = t.Day(Day1, D1(160));
        Assert.Equal(160, day.TotalSeconds);
        Assert.Equal(["code", "chrome"], day.Apps.Select(a => a.App));
        Assert.Equal([100L, 60L], day.Apps.Select(a => a.Seconds));

        // Paused: the open interval is not counted, and reading changed nothing.
        t.OnLock(D1(160));
        Assert.Equal(160, t.Day(Day1, D1(300)).TotalSeconds);
        Assert.Equal(60, SecondsOf(t, Day1, "chrome"));
    }

    [Fact]
    public void S454_the_open_interval_is_not_shown_after_a_suspend()
    {
        var t = new ScreenTimeTracker();
        t.OnForeground(D1(0), "chrome");

        // Between waking and the first tick the anchor is hours old.
        Assert.Equal(0, t.Day(Day1, D1(3 * 3600)).TotalSeconds);
    }

    [Fact]
    public void S454_a_range_has_one_entry_per_day_oldest_first_with_gaps_as_zeros()
    {
        var t = new ScreenTimeTracker();
        t.Add(Day1, "chrome", 600);
        t.Add(Day2, "code", 1_200);

        var days = t.Range(new LocalStamp(Day1Midnight + (2 * 86_400), "2026-07-03", 0), 3);

        Assert.Equal([Day1, Day2, "2026-07-03"], days.Select(d => d.Date));
        Assert.Equal([600L, 1_200L, 0L], days.Select(d => d.TotalSeconds));
        Assert.Empty(days[2].Apps);
    }

    [Fact]
    public void S454_a_range_is_clamped_to_retention_and_never_empty()
    {
        var t = new ScreenTimeTracker();
        var now = new LocalStamp(Day1Midnight, "2026-07-03", 0);

        Assert.Single(t.Range(now, 0));
        Assert.Equal(7, t.Range(now, 7).Count);
        Assert.Equal(30, t.Range(now, 30).Count);
        Assert.Equal(ScreenTimeTracker.RetentionDays, t.Range(now, 10_000).Count);
    }

    [Fact]
    public void S454_a_range_steps_across_month_and_leap_boundaries()
    {
        var t = new ScreenTimeTracker();
        var days = t.Range(new LocalStamp(0, "2024-03-01", 0), 2);
        Assert.Equal(["2024-02-29", "2024-03-01"], days.Select(d => d.Date));
    }

    // ---- paths and names [S-454] ----------------------------------------------------------------

    [Fact]
    public void S454_the_exe_path_is_recorded_and_returned()
    {
        var t = new ScreenTimeTracker();
        t.OnForeground(D1(0), "chrome", "Google Chrome");
        t.NoteExePath("chrome", "C:/apps/chrome.exe");
        t.OnTick(D1(30), 0);
        t.OnForeground(D1(40), "code");
        t.OnTick(D1(70), 0);

        var day = t.Day(Day1, null);
        Assert.Equal("C:/apps/chrome.exe", day.Apps.Single(a => a.App == "chrome").Path);
        Assert.Null(day.Apps.Single(a => a.App == "code").Path);
    }

    [Fact]
    public void S454_note_exe_path_ignores_empties_and_only_a_move_is_dirty()
    {
        var t = new ScreenTimeTracker();
        t.NoteExePath(string.Empty, "C:/x.exe");
        t.NoteExePath("chrome", string.Empty);
        Assert.Empty(t.Paths);
        Assert.False(t.Dirty);

        t.NoteExePath("chrome", "C:/apps/chrome.exe");
        Assert.True(t.Dirty);
        t.Dirty = false;
        t.NoteExePath("chrome", "C:/apps/chrome.exe");
        Assert.False(t.Dirty);
        t.NoteExePath("chrome", "C:/apps2/chrome.exe");
        Assert.True(t.Dirty);
        Assert.Equal("C:/apps2/chrome.exe", t.Paths["chrome"]);
    }

    [Fact]
    public void S454_lists_history_apps_missing_a_path_or_a_name()
    {
        var t = new ScreenTimeTracker();
        t.Add(Day1, "chrome", 10);
        t.Add(Day1, "spotify", 20);
        t.Add(Day2, "chrome", 5);
        t.NoteExePath("chrome", "C:/apps/chrome.exe");
        t.Names["chrome"] = "Google Chrome";

        Assert.Equal(["spotify"], t.AppsMissingPaths());
        Assert.Equal(["spotify"], t.AppsMissingNames());

        t.NoteExePath("spotify", "C:/apps/spotify.exe");
        Assert.Empty(t.AppsMissingPaths());
        Assert.Equal(["spotify"], t.AppsMissingNames());
    }

    [Fact]
    public void S458_merging_a_loaded_history_keeps_what_accrued_before_it()
    {
        var live = new ScreenTimeTracker();
        live.OnForeground(D1(0), "chrome");
        live.OnTick(D1(30), 0);
        live.Switches[Day1] = 2;

        var loaded = new ScreenTimeTracker();
        loaded.Add("2026-06-30", "code", 500);
        loaded.Names["code"] = "Visual Studio Code";
        loaded.Switches[Day1] = 3;

        live.Merge(loaded);

        Assert.Equal(30, SecondsOf(live, Day1, "chrome"));
        Assert.Equal(500, SecondsOf(live, "2026-06-30", "code"));
        Assert.Equal("Visual Studio Code", live.Names["code"]);
        Assert.Equal(5, live.Switches[Day1]);
        Assert.Equal("chrome", live.OpenApp);
    }

    // ---- retention [S-459] -----------------------------------------------------------------------

    [Fact]
    public void S459_retention_prunes_the_oldest_dates_and_their_switch_counts()
    {
        var t = new ScreenTimeTracker();
        var start = new LocalDay(new DateOnly(2026, 1, 1));

        for (var i = 0; i < ScreenTimeTracker.RetentionDays + 5; i++)
        {
            var date = start.AddDays(i).Key;
            t.Add(date, "chrome", 1);
            t.Switches[date] = 1;
        }

        t.Prune();

        Assert.Equal(ScreenTimeTracker.RetentionDays, t.Days.Count);
        Assert.False(t.Days.ContainsKey(start.Key));
        Assert.True(t.Days.ContainsKey(start.AddDays(ScreenTimeTracker.RetentionDays + 4).Key));
        Assert.Equal(ScreenTimeTracker.RetentionDays, t.Switches.Count);
        Assert.All(t.Switches.Keys, date => Assert.True(t.Days.ContainsKey(date)));
    }

    [Fact]
    public void S458_the_stamp_reads_the_local_date_and_time_of_day()
    {
        var moment = new DateTimeOffset(2026, 7, 1, 23, 59, 30, TimeSpan.FromHours(2));
        var stamp = LocalStamp.From(moment);

        Assert.Equal(Day1, stamp.Date);
        Assert.Equal(86_370, stamp.SecondsIntoDay);
        Assert.Equal(moment.ToUnixTimeSeconds(), stamp.Epoch);
    }
}
