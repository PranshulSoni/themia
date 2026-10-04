using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The streak, the squares, and what a log that has been hand-edited is allowed to do to either.
/// <para>
/// Every case here puts "today" somewhere specific, which is the whole reason these rules take it as
/// an argument: the bug this widget is prone to is a streak that is right in June and wrong at the
/// end of October, and there is no other way to catch that before October.
/// </para>
/// </summary>
public class HabitRulesTests
{
    private static readonly LocalDay Today = new(new DateOnly(2026, 6, 15));

    private static HashSet<LocalDay> Log(params string[] keys) => HabitRules.Days(keys, Today);

    private static HashSet<LocalDay> Log(LocalDay today, params string[] keys) => HabitRules.Days(keys, today);

    // -----------------------------------------------------------------------------------------
    // [S-311] the streak ends today or yesterday, and a gap ends it
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S311_an_empty_log_has_no_streak()
        => Assert.Equal(0, HabitRules.Streak(Log(), Today));

    [Fact]
    public void S311_a_run_ending_today_counts_today()
    {
        var days = Log("2026-06-13", "2026-06-14", "2026-06-15");

        Assert.Equal(3, HabitRules.Streak(days, Today));
    }

    [Fact]
    public void S311_a_run_ending_yesterday_survives_an_unticked_today()
    {
        // The day is not over. A counter that resets at midnight and has to be earned back before
        // breakfast is a counter nobody would keep.
        var days = Log("2026-06-13", "2026-06-14");

        Assert.Equal(2, HabitRules.Streak(days, Today));
    }

    [Fact]
    public void S311_a_run_that_stopped_the_day_before_yesterday_is_over()
    {
        var days = Log("2026-06-12", "2026-06-13");

        Assert.Equal(0, HabitRules.Streak(days, Today));
    }

    [Fact]
    public void S311_only_the_run_ending_now_counts_not_the_longest_one()
    {
        // A fortnight in May, and then two days this week. The answer is 2.
        var days = Log(
            "2026-05-01", "2026-05-02", "2026-05-03", "2026-05-04", "2026-05-05",
            "2026-06-14", "2026-06-15");

        Assert.Equal(2, HabitRules.Streak(days, Today));
    }

    [Fact]
    public void S311_today_alone_is_a_streak_of_one()
        => Assert.Equal(1, HabitRules.Streak(Log("2026-06-15"), Today));

    [Fact]
    public void S311_a_streak_counts_across_a_month_boundary()
    {
        var day = new LocalDay(new DateOnly(2026, 7, 2));
        var days = Log(day, "2026-06-30", "2026-07-01", "2026-07-02");

        Assert.Equal(3, HabitRules.Streak(days, day));
    }

    [Fact]
    public void S311_a_streak_counts_across_a_leap_february()
    {
        var day = new LocalDay(new DateOnly(2024, 3, 1));
        var days = Log(day, "2024-02-28", "2024-02-29", "2024-03-01");

        Assert.Equal(3, HabitRules.Streak(days, day));
    }

    [Fact]
    public void S311_a_common_february_has_no_29th_to_break_the_run_on()
    {
        var day = new LocalDay(new DateOnly(2025, 3, 1));
        var days = Log(day, "2025-02-27", "2025-02-28", "2025-03-01");

        Assert.Equal(3, HabitRules.Streak(days, day));
    }

    // -----------------------------------------------------------------------------------------
    // [S-312] a log is untrusted: duplicates, junk and the future
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S312_the_same_day_logged_twice_counts_once()
    {
        var days = Log("2026-06-14", "2026-06-14", "2026-06-15", "2026-06-15", "2026-06-15");

        Assert.Equal(2, days.Count);
        Assert.Equal(2, HabitRules.Streak(days, Today));
    }

    [Fact]
    public void S312_a_day_in_the_future_is_ignored_rather_than_counted()
    {
        // A hand-edited file, or a clock that jumped forward and back. Either way a day that has
        // not happened cannot have been completed, and must not be able to mint a streak.
        var days = Log("2026-06-16", "2026-06-17", "2026-06-18");

        Assert.Empty(days);
        Assert.Equal(0, HabitRules.Streak(days, Today));
    }

    [Fact]
    public void S312_a_future_day_does_not_bridge_a_gap()
    {
        // Without the future filter, "tomorrow" would sit next to today and the walk back would
        // still start at the wrong end.
        var days = Log("2026-06-16", "2026-06-14", "2026-06-15");

        Assert.Equal(2, HabitRules.Streak(days, Today));
    }

    [Fact]
    public void S312_unreadable_keys_are_dropped_and_their_siblings_kept()
    {
        var days = HabitRules.Days(["2026-06-15", "not a date", string.Empty, "2026-13-01", "2026-06-14"], Today);

        Assert.Equal(2, days.Count);
        Assert.Equal(2, HabitRules.Streak(days, Today));
    }

    [Fact]
    public void S312_a_null_log_is_an_empty_one()
    {
        Assert.Empty(HabitRules.Days(null, Today));
        Assert.Equal(0, HabitRules.Streak(HabitRules.Days(null, Today), Today));
    }

    // -----------------------------------------------------------------------------------------
    // [S-311] the streak crosses a daylight-saving change without noticing it
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S311_a_streak_spans_the_short_and_the_long_day()
    {
        // 29 March 2026 is 23 hours long in Central Europe and 25 October is 25. Both are one
        // square on the calendar, so a run through either is unbroken and uninflated.
        var spring = new LocalDay(new DateOnly(2026, 3, 30));
        Assert.Equal(3, HabitRules.Streak(Log(spring, "2026-03-28", "2026-03-29", "2026-03-30"), spring));

        var autumn = new LocalDay(new DateOnly(2026, 10, 26));
        Assert.Equal(3, HabitRules.Streak(Log(autumn, "2026-10-24", "2026-10-25", "2026-10-26"), autumn));
    }

    // -----------------------------------------------------------------------------------------
    // [S-315] the squares
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S315_the_squares_are_oldest_first_with_today_last()
    {
        var days = Log("2026-06-15", "2026-06-13");
        var recent = HabitRules.Recent(days, Today, 4);

        // 12th, 13th, 14th, 15th.
        Assert.Equal(new[] { false, true, false, true }, recent);
    }

    [Fact]
    public void S315_asking_for_no_squares_returns_none()
        => Assert.Empty(HabitRules.Recent(Log("2026-06-15"), Today, 0));

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(7, 7)]
    [InlineData(31, 31)]
    [InlineData(400, HabitRules.MaxHistoryDays)]
    public void S315_the_count_is_clamped_because_it_bounds_a_render_loop(int asked, int expected)
        => Assert.Equal(expected, HabitRules.Recent(Log(), Today, asked).Count);

    [Fact]
    public void S315_the_squares_and_the_streak_agree_about_which_day_is_which()
    {
        var days = Log("2026-06-13", "2026-06-14");
        var recent = HabitRules.Recent(days, Today, 3);

        // The streak is 2 and ends yesterday; so the last square (today) is empty and the two
        // before it are filled.
        Assert.Equal(2, HabitRules.Streak(days, Today));
        Assert.Equal(new[] { true, true, false }, recent);
    }

    // -----------------------------------------------------------------------------------------
    // [S-313] toggling
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S313_toggling_an_unmarked_day_marks_it()
    {
        var log = new List<string>();

        Assert.True(HabitRules.Toggle(log, Today));
        Assert.Equal(new[] { "2026-06-15" }, log);
    }

    [Fact]
    public void S313_toggling_a_marked_day_clears_it()
    {
        var log = new List<string> { "2026-06-15" };

        Assert.False(HabitRules.Toggle(log, Today));
        Assert.Empty(log);
    }

    [Fact]
    public void S313_clearing_removes_every_copy_of_the_day()
    {
        // A log that somehow holds the day twice would otherwise need two taps to clear, which
        // reads as a checkbox that ignored a click.
        var log = new List<string> { "2026-06-15", "2026-06-14", "2026-06-15" };

        Assert.False(HabitRules.Toggle(log, Today));
        Assert.Equal(new[] { "2026-06-14" }, log);
    }

    [Fact]
    public void S313_toggling_leaves_unreadable_keys_alone()
    {
        // They are somebody's data, whatever they are, and dropping them here would be this
        // function quietly cleaning a file it was not asked to clean.
        var log = new List<string> { "junk", "2026-06-15" };

        Assert.False(HabitRules.Toggle(log, Today));
        Assert.Equal(new[] { "junk" }, log);
    }

    [Fact]
    public void S313_a_toggle_round_trips_to_where_it_started()
    {
        var log = new List<string>();

        Assert.True(HabitRules.Toggle(log, Today));
        Assert.False(HabitRules.Toggle(log, Today));
        Assert.Empty(log);
    }
}
