using Xunit;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.Core.Tests;

/// <summary>
/// [S-1573] The Mood tracker's Month span on a stacked tile: the month is whole or it is replaced
/// (flagged) by the most recent weeks — never a partial month, never clipped.
/// </summary>
public sealed class MoodMonthLayoutTests
{
    private static readonly MoodTrackerConfig Month = new() { Span = MoodSpan.Month };

    private static MoodPlan Plan(double width, double height, int monthWeeks, MoodTrackerConfig? config = null)
        => MoodLayout.Plan(new WidgetArea(width, height), config ?? Month, monthWeeks);

    /// <summary>The height the stacked plan occupies, parts and gaps included.</summary>
    private static double StackedHeight(MoodPlan plan)
        => MoodLayout.Top + MoodLayout.PickerHeight + MoodLayout.Gap
            + (plan.Labels ? MoodLayout.Line + MoodLayout.CellGap : 0)
            + MoodLayout.MapHeight(plan.Weeks, plan.Cell)
            + (plan.Note ? MoodLayout.Gap + MoodLayout.NoteHeight : 0)
            + (plan.Stats ? MoodLayout.Gap + MoodLayout.Line : 0)
            + MoodLayout.Bottom;

    [Theory]
    [InlineData(120, 76, 6)]
    [InlineData(120, 76, 5)]
    [InlineData(120, 76, 4)]
    [InlineData(200, 106, 6)]
    [InlineData(200, 106, 5)]
    [InlineData(200, 106, 4)]
    [InlineData(200, 120, 6)]
    [InlineData(320, 196, 6)]
    [InlineData(200, 356, 6)]
    public void S1573_nothing_runs_past_the_tile_and_squares_stay_legible(double width, double height, int monthWeeks)
    {
        var plan = Plan(width, height, monthWeeks);

        Assert.Equal(MoodArrangement.Stacked, plan.Arrangement);
        Assert.True(plan.Cell >= MoodLayout.MinCell);
        Assert.True(StackedHeight(plan) <= height + Disclosure.Tolerance, $"{StackedHeight(plan)} > {height}");
        Assert.True(plan.Recent ? plan.Weeks < monthWeeks : plan.Weeks == monthWeeks);
    }

    [Theory]
    [InlineData(120, 76, 6)]
    [InlineData(120, 76, 4)]
    [InlineData(200, 106, 6)]
    [InlineData(200, 106, 5)]
    public void S1573_a_tile_too_short_for_the_whole_month_shows_the_recent_weeks_instead(double width, double height, int monthWeeks)
    {
        var plan = Plan(width, height, monthWeeks);

        Assert.True(plan.Recent);
        Assert.InRange(plan.Weeks, 1, monthWeeks - 1);
    }

    [Theory]
    [InlineData(320, 196, 6)]
    [InlineData(320, 196, 5)]
    [InlineData(200, 356, 6)]
    [InlineData(200, 130, 5)]
    public void S1573_a_tile_with_room_shows_the_whole_month(double width, double height, int monthWeeks)
    {
        var plan = Plan(width, height, monthWeeks);

        Assert.False(plan.Recent);
        Assert.Equal(monthWeeks, plan.Weeks);
    }

    [Fact]
    public void S1573_the_thought_box_and_footer_give_way_before_the_month_does()
    {
        // 200 x 130: the whole 5-week month fits at the smallest square, but not with the box too.
        var plan = Plan(200, 130, 5);

        Assert.False(plan.Recent);
        Assert.False(plan.Note);
        Assert.False(plan.Stats);

        // Given the height, the box and footer come back.
        var tall = Plan(320, 260, 5);
        Assert.False(tall.Recent);
        Assert.True(tall.Note && tall.Stats);
    }

    [Fact]
    public void S1573_labels_are_dropped_before_the_squares_shrink_below_a_labelled_size()
    {
        Assert.False(Plan(320, 196, 6).Labels);               // squares ~10: no room for weekday names
        Assert.True(Plan(200, 356, 6).Labels);                // squares ~20: labelled
        Assert.True(Plan(200, 356, 6).Cell >= MoodLayout.LabelCell);
    }

    [Fact]
    public void S1573_the_recent_weeks_are_as_many_as_fit_and_never_zero()
    {
        // 120 x 76: 24 of map height = two rows of 9 + a gap (22).
        Assert.Equal(2, Plan(120, 76, 5).Weeks);

        // Absurdly short: one week, still legible, flagged.
        var tiny = Plan(120, 20, 5);
        Assert.True(tiny.Recent);
        Assert.Equal(1, tiny.Weeks);
        Assert.True(tiny.Cell >= MoodLayout.MinCell);
    }

    [Fact]
    public void S1573_a_rolling_map_is_never_flagged_and_keeps_its_own_rule()
    {
        var rolling = new MoodTrackerConfig { Span = MoodSpan.Rolling };

        foreach (var (w, h) in new[] { (120.0, 76.0), (200.0, 106.0), (320.0, 196.0), (200.0, 356.0) })
        {
            Assert.False(Plan(w, h, 5, rolling).Recent);
        }

        Assert.True(Plan(320, 196, 5, rolling).Weeks >= 3);
    }

    [Theory]
    [InlineData(480, 116, 6)]
    [InlineData(560, 376, 5)]
    [InlineData(800, 516, 4)]
    [InlineData(400, 100, 6)]
    public void S1573_a_split_tile_always_holds_the_whole_month(double width, double height, int monthWeeks)
    {
        var plan = Plan(width, height, monthWeeks);

        Assert.Equal(MoodArrangement.Split, plan.Arrangement);
        Assert.False(plan.Recent);
        Assert.Equal(monthWeeks, plan.Weeks);
        Assert.True(plan.Cell >= MoodLayout.MinCell);
    }

    [Fact]
    public void S1573_the_caption_names_the_month_or_says_what_replaced_it()
    {
        Assert.Equal("Oct", MoodLayout.Caption(Plan(320, 196, 5), Month, 10));
        Assert.Equal("Last 2 weeks", MoodLayout.Caption(Plan(120, 76, 5), Month, 10));
        Assert.Equal("This week", MoodLayout.Caption(Plan(120, 20, 5), Month, 10));
        Assert.Null(MoodLayout.Caption(Plan(320, 196, 5, new MoodTrackerConfig()), new MoodTrackerConfig(), 10));
    }
}
