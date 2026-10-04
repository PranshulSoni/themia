using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// [S-1421]..[S-1426] The periods, the streak, the dots, the best run, and what a hand-edited log is
/// allowed to do to any of them.
/// <para>
/// Every case puts "now" and the time zone somewhere specific — which is why the rules take both as
/// arguments: the bug this widget is prone to is a streak that is right in June and wrong on the
/// last Sunday in October, and there is no other way to catch that before October.
/// </para>
/// </summary>
public class HabitRulesTests
{
    /// <summary>Central Europe: +01:00 in winter, +02:00 in summer; changes on 29 Mar and 25 Oct 2026.</summary>
    private static readonly TimeZoneInfo Zurich = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");

    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static readonly HabitCadence Daily = HabitCadence.Daily;

    private static readonly DateTimeOffset Noon = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private static HashSet<string> Log(params string?[] keys) => HabitRules.Log(keys);

    private static HabitBucket Day(DateTimeOffset now) => HabitPeriods.At(now, Daily, Utc);

    private static int DailyStreak(params string[] keys) => HabitRules.Streak(Log(keys), Day(Noon), Daily, Utc);

    // -----------------------------------------------------------------------------------------
    // [S-1422] periods: keys, steps back, DST
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S1422_a_day_is_the_local_calendar_day_not_the_utc_one()
    {
        // 23:30 UTC on 15 June is 01:30 on the 16th in Zurich.
        var late = new DateTimeOffset(2026, 6, 15, 23, 30, 0, TimeSpan.Zero);

        Assert.Equal("2026-06-16", HabitPeriods.At(late, Daily, Zurich).Key);
    }

    [Theory]
    [InlineData(HabitPeriod.Hourly, "2026-06-15-H14")]
    [InlineData(HabitPeriod.Daily, "2026-06-15")]
    [InlineData(HabitPeriod.Weekly, "2026-W25")]
    [InlineData(HabitPeriod.Monthly, "2026-06")]
    public void S1422_each_cadence_has_its_own_key_shape(HabitPeriod period, string key)
    {
        var at = new DateTimeOffset(2026, 6, 15, 14, 20, 0, TimeSpan.FromHours(2));

        Assert.Equal(key, HabitPeriods.At(at, new HabitCadence(period), Zurich).Key);
    }

    [Theory]
    [InlineData(2026, 1, 1, "2026-W01")]   // a Thursday: week 1 of 2026
    [InlineData(2027, 1, 1, "2026-W53")]   // a Friday: still the last week of 2026
    [InlineData(2024, 12, 30, "2025-W01")] // a Monday: already week 1 of 2025
    public void S1422_weeks_are_iso_weeks_with_their_own_year(int year, int month, int day, string key)
    {
        var at = new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(key, HabitPeriods.At(at, new HabitCadence(HabitPeriod.Weekly), Utc).Key);
    }

    [Fact]
    public void S1422_custom_windows_are_fixed_and_anchored_to_the_epoch()
    {
        var cadence = new HabitCadence(HabitPeriod.Custom, 30);
        var start = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

        var a = HabitPeriods.At(start, cadence, Utc);
        var b = HabitPeriods.At(start.AddMinutes(29), cadence, Utc);
        var c = HabitPeriods.At(start.AddMinutes(30), cadence, Utc);

        Assert.Equal(a.Key, b.Key);
        Assert.NotEqual(a.Key, c.Key);
        Assert.Equal(start, a.Start);
        Assert.StartsWith("C30m-", a.Key, StringComparison.Ordinal);
    }

    [Fact]
    public void S1422_a_custom_interval_is_clamped_when_read()
    {
        Assert.Equal(1, new HabitCadence(HabitPeriod.Custom, 0).Minutes);
        Assert.Equal(1440, new HabitCadence(HabitPeriod.Custom, 99_999).Minutes);
    }

    [Fact]
    public void S1422_the_day_before_the_spring_change_is_one_step_back()
    {
        // 29 March 2026 is 23 hours long in Zurich; it is still one day.
        var after = HabitPeriods.At(new DateTimeOffset(2026, 3, 30, 9, 0, 0, TimeSpan.FromHours(2)), Daily, Zurich);

        var previous = HabitPeriods.Previous(after, Daily, Zurich);

        Assert.Equal("2026-03-29", previous.Key);
        Assert.Equal("2026-03-28", HabitPeriods.Previous(previous, Daily, Zurich).Key);
    }

    [Fact]
    public void S1422_an_hour_skipped_in_spring_is_never_a_step()
    {
        // At 02:00 the clocks jump to 03:00: the hour before 03:00 is 01:00.
        var three = HabitPeriods.At(new DateTimeOffset(2026, 3, 29, 3, 30, 0, TimeSpan.FromHours(2)), new HabitCadence(HabitPeriod.Hourly), Zurich);

        Assert.Equal("2026-03-29-H03", three.Key);
        Assert.Equal("2026-03-29-H01", HabitPeriods.Previous(three, new HabitCadence(HabitPeriod.Hourly), Zurich).Key);
    }

    [Fact]
    public void S1422_an_hour_repeated_in_autumn_is_one_step()
    {
        // 02:00–03:00 happens twice on 25 October; walking back from 03:30 meets it once.
        var hourly = new HabitCadence(HabitPeriod.Hourly);
        var three = HabitPeriods.At(new DateTimeOffset(2026, 10, 25, 3, 30, 0, TimeSpan.FromHours(1)), hourly, Zurich);

        var two = HabitPeriods.Previous(three, hourly, Zurich);
        var one = HabitPeriods.Previous(two, hourly, Zurich);

        Assert.Equal("2026-10-25-H02", two.Key);
        Assert.Equal("2026-10-25-H01", one.Key);
    }

    [Theory]
    [InlineData(HabitPeriod.Hourly)]
    [InlineData(HabitPeriod.Daily)]
    [InlineData(HabitPeriod.Weekly)]
    [InlineData(HabitPeriod.Monthly)]
    [InlineData(HabitPeriod.Custom)]
    public void S1422_a_key_reads_back_into_the_period_it_names(HabitPeriod period)
    {
        var cadence = new HabitCadence(period, 45);
        var bucket = HabitPeriods.At(new DateTimeOffset(2026, 6, 15, 14, 20, 0, TimeSpan.FromHours(2)), cadence, Zurich);

        Assert.True(HabitPeriods.TryParse(bucket.Key, cadence, Zurich, out var parsed));
        Assert.Equal(bucket, parsed);
    }

    [Theory]
    [InlineData(HabitPeriod.Daily, "2026-06")]
    [InlineData(HabitPeriod.Daily, "2025-02-29")]
    [InlineData(HabitPeriod.Hourly, "2026-06-15-H24")]
    [InlineData(HabitPeriod.Hourly, "2026-06-15")]
    [InlineData(HabitPeriod.Weekly, "2026-W54")]
    [InlineData(HabitPeriod.Weekly, "2027-W53")]
    [InlineData(HabitPeriod.Monthly, "2026-13")]
    [InlineData(HabitPeriod.Custom, "C30m-100")]
    [InlineData(HabitPeriod.Custom, "C45m-0100")]
    [InlineData(HabitPeriod.Custom, "C45m--3")]
    [InlineData(HabitPeriod.Daily, "")]
    public void S1422_a_key_of_another_cadence_or_shape_is_refused(HabitPeriod period, string key)
        => Assert.False(HabitPeriods.TryParse(key, new HabitCadence(period, 45), Zurich, out _));

    [Theory]
    [InlineData("30", true, 30)]
    [InlineData(" 1440 ", true, 1440)]
    [InlineData("0", false, 0)]
    [InlineData("1441", false, 0)]
    [InlineData("-5", false, 0)]
    [InlineData("2.5", false, 0)]
    [InlineData("", false, 0)]
    public void S1422_a_typed_interval_is_whole_minutes_in_range_or_refused(string text, bool ok, int expected)
    {
        Assert.Equal(ok, HabitCadence.TryParseMinutes(text, out var minutes, out var refusal));
        Assert.Equal(expected, minutes);
        Assert.Equal(ok, refusal is null);
    }

    [Fact]
    public void S1422_the_tick_is_faster_only_for_the_short_cadences()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), HabitPeriods.TickInterval(HabitPeriod.Hourly));
        Assert.Equal(TimeSpan.FromSeconds(15), HabitPeriods.TickInterval(HabitPeriod.Custom));
        Assert.Equal(TimeSpan.FromMinutes(1), HabitPeriods.TickInterval(HabitPeriod.Daily));
        Assert.Equal(TimeSpan.FromMinutes(1), HabitPeriods.TickInterval(HabitPeriod.Monthly));
    }

    // -----------------------------------------------------------------------------------------
    // [S-1423] the streak ends now or one period back, and a gap ends it
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S1423_an_empty_log_has_no_streak() => Assert.Equal(0, DailyStreak());

    [Fact]
    public void S1423_a_run_ending_today_counts_today()
        => Assert.Equal(3, DailyStreak("2026-06-13", "2026-06-14", "2026-06-15"));

    [Fact]
    public void S1423_a_run_ending_yesterday_survives_an_unticked_today()
        => Assert.Equal(2, DailyStreak("2026-06-13", "2026-06-14"));

    [Fact]
    public void S1423_a_run_that_stopped_the_day_before_yesterday_is_over()
        => Assert.Equal(0, DailyStreak("2026-06-12", "2026-06-13"));

    [Fact]
    public void S1423_only_the_run_ending_now_counts_not_the_longest_one()
        => Assert.Equal(2, DailyStreak("2026-05-01", "2026-05-02", "2026-05-03", "2026-05-04", "2026-06-14", "2026-06-15"));

    [Fact]
    public void S1423_a_streak_counts_across_a_leap_february()
    {
        var now = new DateTimeOffset(2028, 3, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(3, HabitRules.Streak(Log("2028-02-28", "2028-02-29", "2028-03-01"), Day(now), Daily, Utc));
    }

    [Fact]
    public void S1423_a_daily_streak_spans_the_short_and_the_long_day()
    {
        var now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(1));
        var log = Log("2026-10-24", "2026-10-25", "2026-10-26");

        Assert.Equal(3, HabitRules.Streak(log, HabitPeriods.At(now, Daily, Zurich), Daily, Zurich));
    }

    [Fact]
    public void S1423_a_weekly_streak_counts_weeks()
    {
        var weekly = new HabitCadence(HabitPeriod.Weekly);
        var now = HabitPeriods.At(Noon, weekly, Utc);

        Assert.Equal(2, HabitRules.Streak(Log("2026-W23", "2026-W24"), now, weekly, Utc));
        Assert.Equal(3, HabitRules.Streak(Log("2026-W23", "2026-W24", "2026-W25"), now, weekly, Utc));
    }

    [Fact]
    public void S1423_an_hourly_streak_runs_through_the_skipped_spring_hour()
    {
        var hourly = new HabitCadence(HabitPeriod.Hourly);
        var now = HabitPeriods.At(new DateTimeOffset(2026, 3, 29, 3, 30, 0, TimeSpan.FromHours(2)), hourly, Zurich);

        Assert.Equal(3, HabitRules.Streak(Log("2026-03-29-H00", "2026-03-29-H01", "2026-03-29-H03"), now, hourly, Zurich));
    }

    [Fact]
    public void S1423_the_same_key_logged_twice_counts_once()
        => Assert.Equal(2, DailyStreak("2026-06-14", "2026-06-14", "2026-06-15"));

    [Fact]
    public void S1423_a_future_key_is_never_counted_and_bridges_nothing()
    {
        Assert.Equal(1, DailyStreak("2026-06-15", "2026-06-16", "2026-06-17"));
        Assert.Equal(0, DailyStreak("2026-06-13", "2026-06-16"));
    }

    [Fact]
    public void S1423_unreadable_and_foreign_keys_are_ignored_and_their_siblings_kept()
        => Assert.Equal(2, DailyStreak("2026-06-14", "not a day", "2026-W24", null!, "", "2026-06-15"));

    // -----------------------------------------------------------------------------------------
    // [S-1424] the best run
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S1424_the_best_run_is_the_longest_anywhere_in_the_log()
    {
        var log = Log("2026-05-01", "2026-05-02", "2026-05-03", "2026-05-04", "2026-06-14", "2026-06-15");

        Assert.Equal(4, HabitRules.BestStreak(log, Day(Noon), Daily, Utc));
    }

    [Fact]
    public void S1424_the_best_run_ignores_the_future_and_other_cadences()
    {
        var log = Log("2026-06-15", "2026-06-16", "2026-06-17", "2026-06-18", "2026-W24", "2026-06-15-H09");

        Assert.Equal(1, HabitRules.BestStreak(log, Day(Noon), Daily, Utc));
        Assert.Equal(0, HabitRules.BestStreak(Log(), Day(Noon), Daily, Utc));
    }

    [Fact]
    public void S1424_months_of_different_lengths_are_consecutive()
    {
        var monthly = new HabitCadence(HabitPeriod.Monthly);

        Assert.Equal(3, HabitRules.BestStreak(Log("2026-01", "2026-02", "2026-03", "2026-05"), HabitPeriods.At(Noon, monthly, Utc), monthly, Utc));
    }

    // -----------------------------------------------------------------------------------------
    // [S-1426] the dots
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S1426_the_dots_are_oldest_first_with_now_last()
    {
        var dots = HabitRules.Recent(Log("2026-06-13", "2026-06-15"), Day(Noon), Daily, Utc, 4);

        Assert.Equal(new[] { "2026-06-12", "2026-06-13", "2026-06-14", "2026-06-15" }, dots.Select(d => d.Bucket.Key));
        Assert.Equal(new[] { false, true, false, true }, dots.Select(d => d.Done));
    }

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(0, 0)]
    [InlineData(7, 7)]
    [InlineData(500, HabitRules.MaxHistory)]
    public void S1426_the_count_is_clamped_because_it_bounds_a_render_loop(int asked, int expected)
        => Assert.Equal(expected, HabitRules.Recent(Log(), Day(Noon), Daily, Utc, asked).Count);

    [Fact]
    public void S1426_a_dot_is_described_in_its_cadences_own_units()
    {
        var at = new DateTimeOffset(2026, 9, 30, 14, 20, 0, TimeSpan.FromHours(2));

        Assert.Equal("Wednesday 30 Sep 2026", HabitPeriods.Describe(HabitPeriods.At(at, Daily, Zurich), Daily));
        Assert.Equal("14:00, Wednesday 30 Sep 2026", HabitPeriods.Describe(HabitPeriods.At(at, new HabitCadence(HabitPeriod.Hourly), Zurich), new HabitCadence(HabitPeriod.Hourly)));
        Assert.Equal("Week of Monday 28 Sep 2026", HabitPeriods.Describe(HabitPeriods.At(at, new HabitCadence(HabitPeriod.Weekly), Zurich), new HabitCadence(HabitPeriod.Weekly)));
        Assert.Equal("September 2026", HabitPeriods.Describe(HabitPeriods.At(at, new HabitCadence(HabitPeriod.Monthly), Zurich), new HabitCadence(HabitPeriod.Monthly)));

        var custom = new HabitCadence(HabitPeriod.Custom, 30);
        Assert.Equal("14:00 to 14:30, Wednesday 30 Sep 2026", HabitPeriods.Describe(HabitPeriods.At(at, custom, Zurich), custom));
    }

    [Fact]
    public void S1427_done_in_counts_the_window_including_now()
        => Assert.Equal(2, HabitRules.DoneIn(Log("2026-06-08", "2026-06-09", "2026-06-15"), Day(Noon), Daily, Utc, 7));

    // -----------------------------------------------------------------------------------------
    // [S-1421] ticking
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S1421_toggling_an_unticked_period_ticks_it()
        => Assert.Equal(new[] { "2026-06-14", "2026-06-15" }, HabitRules.Toggle(["2026-06-14"], "2026-06-15"));

    [Fact]
    public void S1421_toggling_a_ticked_period_clears_every_copy()
        => Assert.Equal(new[] { "2026-06-14" }, HabitRules.Toggle(["2026-06-15", "2026-06-14", "2026-06-15"], "2026-06-15"));

    [Fact]
    public void S1421_toggling_leaves_other_keys_alone_and_round_trips()
    {
        IReadOnlyList<string> log = ["junk", "2026-W24"];

        var twice = HabitRules.Toggle(HabitRules.Toggle(log, "2026-06-15"), "2026-06-15");

        Assert.Equal(log, twice);
    }
}
