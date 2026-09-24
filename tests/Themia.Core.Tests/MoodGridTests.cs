using Themia.Core.Model;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The squares the heatmap is drawn on.
/// <para>
/// Two things can go wrong here and both are silent: the padding at the start of a month can be off
/// by a week, which puts every day under the wrong weekday, and the row count can be wrong, which
/// either hides the end of the month or leaves a blank row. The awkward cases are a month that
/// begins exactly on the week's first day (no padding at all) and a February that is exactly four
/// weeks long, so both are pinned here.
/// </para>
/// </summary>
public class MoodGridTests
{
    private static IEnumerable<LocalDay?> Flatten(MoodGrid grid) => grid.Rows.SelectMany(row => row);

    // -----------------------------------------------------------------------------------------
    // [S-327] the rolling window
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S327_a_rolling_window_is_whole_weeks_ending_with_this_one()
    {
        // Wednesday 4 February 2026, weeks starting Monday.
        var today = new LocalDay(new DateOnly(2026, 2, 4));
        var grid = MoodGrid.Rolling(today, 3, DayOfWeek.Monday);

        Assert.Equal(3, grid.Rows.Count);
        Assert.All(grid.Rows, row => Assert.Equal(MoodGrid.Columns, row.Count));

        // Three weeks back from the Monday of this week.
        Assert.Equal("2026-01-19", grid.Rows[0][0]!.Value.Key);
        Assert.Equal("2026-02-02", grid.Rows[2][0]!.Value.Key);
    }

    [Fact]
    public void S327_every_column_is_the_same_weekday()
    {
        var today = new LocalDay(new DateOnly(2026, 2, 4));
        var grid = MoodGrid.Rolling(today, 4, DayOfWeek.Monday);

        for (var column = 0; column < MoodGrid.Columns; column++)
        {
            var weekdays = grid.Rows
                .Select(row => row[column])
                .Where(day => day is not null)
                .Select(day => day!.Value.DayOfWeek)
                .Distinct();

            Assert.Single(weekdays);
        }
    }

    [Fact]
    public void S327_the_rest_of_this_week_is_blank_because_it_has_not_happened()
    {
        // Wednesday, so Thursday, Friday, Saturday and Sunday are still to come.
        var today = new LocalDay(new DateOnly(2026, 2, 4));
        var grid = MoodGrid.Rolling(today, 2, DayOfWeek.Monday);
        var last = grid.Rows[^1];

        Assert.NotNull(last[2]);                     // Wednesday, today
        Assert.Equal(today, last[2]!.Value);
        Assert.All(last.Skip(3), day => Assert.Null(day));
    }

    [Fact]
    public void S327_a_window_ending_on_the_last_day_of_its_week_has_no_blanks()
    {
        // Sunday 8 February 2026, weeks starting Monday: the week is complete.
        var today = new LocalDay(new DateOnly(2026, 2, 8));
        var grid = MoodGrid.Rolling(today, 2, DayOfWeek.Monday);

        Assert.All(Flatten(grid), day => Assert.NotNull(day));
        Assert.Equal(today, grid.Rows[^1][^1]!.Value);
    }

    [Fact]
    public void S327_the_week_start_moves_the_columns_not_the_days()
    {
        var today = new LocalDay(new DateOnly(2026, 2, 4));

        var monday = MoodGrid.Rolling(today, 2, DayOfWeek.Monday);
        var sunday = MoodGrid.Rolling(today, 2, DayOfWeek.Sunday);

        Assert.Equal(DayOfWeek.Monday, monday.Rows[0][0]!.Value.DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, sunday.Rows[0][0]!.Value.DayOfWeek);
        Assert.Equal(today, monday.Last);
        Assert.Equal(today, sunday.Last);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    [InlineData(1, 1)]
    [InlineData(12, 12)]
    [InlineData(500, MoodTrackerConfig.MaxHistoryWeeks)]
    public void S327_the_row_count_is_clamped_because_it_bounds_a_render_loop(int asked, int expected)
        => Assert.Equal(expected, MoodGrid.Rolling(new LocalDay(new DateOnly(2026, 2, 4)), asked, DayOfWeek.Monday).Rows.Count);

    [Fact]
    public void S327_a_rolling_window_crosses_a_year_boundary()
    {
        // Friday 1 January 2027; the window reaches back into 2026.
        var today = new LocalDay(new DateOnly(2027, 1, 1));
        var grid = MoodGrid.Rolling(today, 2, DayOfWeek.Monday);

        Assert.Equal("2026-12-21", grid.First.Key);
        Assert.Equal("2027-01-01", grid.Last.Key);
    }

    // -----------------------------------------------------------------------------------------
    // [S-328] a calendar month
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S328_a_month_starting_on_the_weeks_first_day_needs_no_padding()
    {
        // February 2026 begins on a Sunday, so with weeks starting Sunday the first square is the
        // 1st: the case where an off-by-one padding rule adds a blank week nobody asked for.
        var grid = MoodGrid.Month(2026, 2, DayOfWeek.Sunday);

        Assert.Equal("2026-02-01", grid.Rows[0][0]!.Value.Key);
        Assert.Equal(DayOfWeek.Sunday, grid.Rows[0][0]!.Value.DayOfWeek);
    }

    [Fact]
    public void S328_the_same_month_on_a_monday_week_pads_six_days()
    {
        // The same February, weeks starting Monday: the 1st is a Sunday, so it lands in the last
        // column of the first row and six squares before it are blank.
        var grid = MoodGrid.Month(2026, 2, DayOfWeek.Monday);

        Assert.All(grid.Rows[0].Take(6), day => Assert.Null(day));
        Assert.Equal("2026-02-01", grid.Rows[0][6]!.Value.Key);
    }

    [Fact]
    public void S328_a_28_day_february_starting_on_the_weeks_first_day_is_exactly_four_rows()
    {
        // 2026 is a common year and February begins on a Sunday, so a Sunday-start month is four
        // whole weeks with no padding at either end. The tightest fit there is.
        var grid = MoodGrid.Month(2026, 2, DayOfWeek.Sunday);

        Assert.Equal(4, grid.Rows.Count);
        Assert.All(Flatten(grid), day => Assert.NotNull(day));
    }

    [Fact]
    public void S328_a_leap_february_holds_29_days()
    {
        var grid = MoodGrid.Month(2024, 2, DayOfWeek.Monday);
        var days = Flatten(grid).Where(day => day is not null).ToList();

        Assert.Equal(29, days.Count);
        Assert.Equal("2024-02-01", grid.First.Key);
        Assert.Equal("2024-02-29", grid.Last.Key);
        Assert.Equal("2024-02-29", days[^1]!.Value.Key);
    }

    [Fact]
    public void S328_a_common_february_holds_28_days()
    {
        var grid = MoodGrid.Month(2025, 2, DayOfWeek.Monday);

        Assert.Equal(28, Flatten(grid).Count(day => day is not null));
        Assert.Equal("2025-02-28", grid.Last.Key);
    }

    [Fact]
    public void S328_a_31_day_month_that_needs_six_rows_gets_six()
    {
        // May 2027 begins on a Saturday; with weeks starting Monday that is five days of padding
        // and 31 days, which does not fit in five rows.
        var grid = MoodGrid.Month(2027, 5, DayOfWeek.Monday);

        Assert.Equal(6, grid.Rows.Count);
        Assert.Equal(31, Flatten(grid).Count(day => day is not null));
    }

    [Fact]
    public void S328_a_month_holds_each_of_its_days_exactly_once_and_in_order()
    {
        var grid = MoodGrid.Month(2026, 3, DayOfWeek.Monday);
        var days = Flatten(grid).Where(day => day is not null).Select(day => day!.Value).ToList();

        Assert.Equal(31, days.Count);
        Assert.Equal(days.Count, days.Distinct().Count());

        for (var i = 1; i < days.Count; i++)
        {
            Assert.Equal(1, days[i - 1].DaysUntil(days[i]));
        }
    }

    [Fact]
    public void S328_nothing_outside_the_month_leaks_into_it()
    {
        var grid = MoodGrid.Month(2026, 3, DayOfWeek.Monday);

        Assert.All(
            Flatten(grid).Where(day => day is not null),
            day => Assert.Equal(3, day!.Value.Month));
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(0, 6)]
    public void S328_an_impossible_month_is_folded_into_range_rather_than_throwing(int year, int month)
    {
        var grid = MoodGrid.Month(year, month, DayOfWeek.Monday);

        Assert.NotEmpty(grid.Rows);
        Assert.All(grid.Rows, row => Assert.Equal(MoodGrid.Columns, row.Count));
    }

    // -----------------------------------------------------------------------------------------
    // [S-329] one shape for both spans
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S329_build_picks_the_span_and_both_come_back_the_same_shape()
    {
        var today = new LocalDay(new DateOnly(2026, 2, 4));

        var rolling = MoodGrid.Build(MoodSpan.Rolling, today, 5, DayOfWeek.Monday);
        var month = MoodGrid.Build(MoodSpan.Month, today, 5, DayOfWeek.Monday);

        Assert.Equal(5, rolling.Rows.Count);
        Assert.Equal(MoodGrid.Rolling(today, 5, DayOfWeek.Monday).First, rolling.First);
        Assert.Equal(MoodGrid.Month(2026, 2, DayOfWeek.Monday).Rows.Count, month.Rows.Count);
        Assert.All(month.Rows, row => Assert.Equal(MoodGrid.Columns, row.Count));
    }

    [Fact]
    public void S329_the_headers_run_in_the_grids_own_order()
    {
        Assert.Equal(
            new[] { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" },
            MoodGrid.Rolling(new LocalDay(new DateOnly(2026, 2, 4)), 2, DayOfWeek.Monday).Headers);

        Assert.Equal(
            new[] { "Su", "Mo", "Tu", "We", "Th", "Fr", "Sa" },
            MoodGrid.Rolling(new LocalDay(new DateOnly(2026, 2, 4)), 2, DayOfWeek.Sunday).Headers);
    }
}
