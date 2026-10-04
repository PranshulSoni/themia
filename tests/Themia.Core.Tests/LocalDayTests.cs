using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The calendar arithmetic all three tracker widgets stand on.
/// <para>
/// The interesting cases are the ones where doing this on instants goes wrong: the 23-hour and
/// 25-hour days either side of a daylight-saving change, a stamp written in one offset and read in
/// another, and February in a leap year. Each of those has a test here because each of them is a
/// place a streak silently gains or loses a day, twice a year, on the machines of the users who
/// happen to live in the wrong place.
/// </para>
/// </summary>
public class LocalDayTests
{
    // -----------------------------------------------------------------------------------------
    // [S-330] the day is read in the stamp's own offset
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S330_reads_the_day_in_the_stamps_own_offset()
    {
        // Eleven at night in Zurich. In UTC this is the 3rd, and a widget that compared UTC dates
        // would file the evening under the wrong day for everyone east of Greenwich.
        var evening = new DateTimeOffset(2026, 2, 3, 23, 30, 0, TimeSpan.FromHours(2));

        Assert.Equal("2026-02-03", LocalDay.From(evening).Key);
    }

    [Fact]
    public void S330_the_same_instant_in_two_offsets_is_two_days()
    {
        var instant = new DateTimeOffset(2026, 2, 4, 0, 30, 0, TimeSpan.Zero);

        Assert.Equal("2026-02-04", LocalDay.From(instant).Key);
        Assert.Equal("2026-02-03", LocalDay.From(instant.ToOffset(TimeSpan.FromHours(-2))).Key);
    }

    // -----------------------------------------------------------------------------------------
    // [S-331] daylight saving: a short day and a long day are both one day
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S331_a_23_hour_spring_forward_day_is_still_one_day()
    {
        // Central Europe, 29 March 2026: the clocks go forward at 02:00, so this calendar day is
        // 23 hours long.
        var before = new DateTimeOffset(2026, 3, 28, 23, 30, 0, TimeSpan.FromHours(1));
        var after = new DateTimeOffset(2026, 3, 29, 23, 30, 0, TimeSpan.FromHours(2));

        // The trap, stated out loud: less than a whole 24 hours separates these two evenings, so
        // anything that divides a duration by a day answers "the same day".
        Assert.True((after - before).TotalDays < 1);

        Assert.Equal(1, LocalDay.From(before).DaysUntil(LocalDay.From(after)));
        Assert.Equal(LocalDay.From(after), LocalDay.From(before).AddDays(1));
    }

    [Fact]
    public void S331_a_25_hour_fall_back_day_is_still_one_day()
    {
        // 25 October 2026: the clocks go back, so this calendar day is 25 hours long.
        var before = new DateTimeOffset(2026, 10, 24, 23, 30, 0, TimeSpan.FromHours(2));
        var after = new DateTimeOffset(2026, 10, 25, 23, 30, 0, TimeSpan.FromHours(1));

        Assert.True((after - before).TotalDays > 1);

        Assert.Equal(1, LocalDay.From(before).DaysUntil(LocalDay.From(after)));
        Assert.Equal(LocalDay.From(after), LocalDay.From(before).AddDays(1));
    }

    [Fact]
    public void S331_the_repeated_hour_after_a_fall_back_stays_on_its_own_day()
    {
        // 02:30 happens twice on 25 October, once at +02:00 and once at +01:00. Both are that day.
        var first = new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(2));
        var second = new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(1));

        Assert.Equal(LocalDay.From(first), LocalDay.From(second));
        Assert.Equal("2026-10-25", LocalDay.From(second).Key);
    }

    // -----------------------------------------------------------------------------------------
    // [S-332] leap years and month ends
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S332_february_29_exists_in_a_leap_year_and_not_otherwise()
    {
        Assert.True(LocalDay.TryParse("2024-02-29", out var leap));
        Assert.Equal(new LocalDay(new DateOnly(2024, 2, 29)), leap);

        Assert.False(LocalDay.TryParse("2025-02-29", out _));
    }

    [Fact]
    public void S332_stepping_over_a_leap_day_counts_it()
    {
        var february = new LocalDay(new DateOnly(2024, 2, 28));

        Assert.Equal("2024-02-29", february.AddDays(1).Key);
        Assert.Equal("2024-03-01", february.AddDays(2).Key);
        Assert.Equal(2, february.DaysUntil(new LocalDay(new DateOnly(2024, 3, 1))));
    }

    [Fact]
    public void S332_stepping_over_a_common_february_skips_the_29th()
    {
        var february = new LocalDay(new DateOnly(2025, 2, 28));

        Assert.Equal("2025-03-01", february.AddDays(1).Key);
    }

    // -----------------------------------------------------------------------------------------
    // [S-333] the persisted key is strict on the way in and stable on the way out
    // -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("2026-01-02")]
    [InlineData("0001-01-01")]
    [InlineData("9999-12-31")]
    public void S333_a_key_round_trips(string key)
    {
        Assert.True(LocalDay.TryParse(key, out var day));
        Assert.Equal(key, day.Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-1-2")]        // not zero padded
    [InlineData("2026/01/02")]      // wrong separator
    [InlineData("2026-01-02T00")]   // an instant, not a day
    [InlineData("02-01-2026")]      // a different order
    [InlineData("2026-13-01")]      // no such month
    [InlineData("2026-00-01")]
    [InlineData("2026-01-32")]
    [InlineData("2026-04-31")]      // no such day in April
    [InlineData("202X-01-01")]
    [InlineData(" 2026-01-01")]
    public void S333_anything_that_is_not_exactly_a_key_is_refused(string? key)
        => Assert.False(LocalDay.TryParse(key, out _));

    // -----------------------------------------------------------------------------------------
    // [S-334] week starts
    // -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(DayOfWeek.Monday, "2026-02-02")]
    [InlineData(DayOfWeek.Sunday, "2026-02-01")]
    [InlineData(DayOfWeek.Saturday, "2026-01-31")]
    public void S334_start_of_week_depends_on_the_chosen_first_day(DayOfWeek weekStart, string expected)
    {
        // Wednesday 4 February 2026.
        var wednesday = new LocalDay(new DateOnly(2026, 2, 4));

        Assert.Equal(expected, wednesday.StartOfWeek(weekStart).Key);
    }

    [Fact]
    public void S334_a_day_that_is_already_the_week_start_does_not_move()
    {
        var monday = new LocalDay(new DateOnly(2026, 2, 2));

        Assert.Equal(monday, monday.StartOfWeek(DayOfWeek.Monday));
    }

    [Fact]
    public void S334_start_of_week_crosses_a_year_boundary()
    {
        // Friday 1 January 2027; the Monday before it is in the previous year.
        var newYear = new LocalDay(new DateOnly(2027, 1, 1));

        Assert.Equal("2026-12-28", newYear.StartOfWeek(DayOfWeek.Monday).Key);
    }

    // -----------------------------------------------------------------------------------------
    // [S-335] the ends of the calendar do not throw
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S335_stepping_past_the_ends_clamps_rather_than_throwing()
    {
        var first = new LocalDay(DateOnly.MinValue);
        var last = new LocalDay(DateOnly.MaxValue);

        Assert.Equal(first, first.AddDays(-1));
        Assert.Equal(last, last.AddDays(1));
        Assert.Equal(first, first.AddDays(int.MinValue));
        Assert.Equal(last, last.AddDays(int.MaxValue));
    }
}
