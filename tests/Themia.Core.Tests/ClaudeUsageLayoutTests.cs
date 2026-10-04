using Themia.Core.Developer;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>[S-790]..[S-795] How the Claude usage widget uses the room it has.</summary>
public sealed class ClaudeUsageLayoutTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static UsageBar Bar(string key) => new(key, key, key, key, new UsageWindow(40, Now.AddHours(1)));

    [Fact]
    public void S790_leftover_height_goes_to_the_bar_then_the_percentage_then_the_gaps()
    {
        // Four Full rows need 4 × 50 + 3 × 12 = 236.
        var none = ClaudeUsageRules.Spread(236, 4, UsageDensity.Full);
        Assert.Equal(ClaudeUsageRules.BarMin, none.Bar);
        Assert.Equal(FluentType.Body, none.Value);
        Assert.Equal(12, none.Gap);

        var some = ClaudeUsageRules.Spread(236 + 24, 4, UsageDensity.Full);
        Assert.Equal(ClaudeUsageRules.BarMax, some.Bar);
        Assert.Equal(FluentType.Body, some.Value);

        var lots = ClaudeUsageRules.Spread(340, 4, UsageDensity.Full);
        Assert.Equal(ClaudeUsageRules.BarMax, lots.Bar);
        Assert.Equal(FluentType.Subtitle, lots.Value);
        Assert.Equal(ClaudeUsageRules.GapMax, lots.Gap);
        Assert.Equal(340 - 236 - 24 - 32 - (3 * 12), lots.Slack, 3);
    }

    [Fact]
    public void S794_a_stack_of_full_rows_is_fifty_each_twelve_apart()
    {
        Assert.Equal(0, ClaudeUsageRules.StackHeight(0, UsageDensity.Full));
        Assert.Equal(50, ClaudeUsageRules.StackHeight(1, UsageDensity.Full));
        Assert.Equal(236, ClaudeUsageRules.StackHeight(4, UsageDensity.Full));
    }

    [Fact]
    public void S790_one_line_densities_are_not_spread()
    {
        var spread = ClaudeUsageRules.Spread(500, 4, UsageDensity.Tight);
        Assert.Equal(ClaudeUsageRules.BarMin, spread.Bar);
        Assert.Equal(0, spread.Slack);
        Assert.Equal(ClaudeUsageRules.BarMin, ClaudeUsageRules.Spread(double.NaN, 2, UsageDensity.Full).Bar);
    }

    [Fact]
    public void S791_a_wide_strip_gets_a_reset_column_and_its_labels_make_room_for_it()
    {
        Assert.True(ClaudeUsageRules.ResetColumn(448, UsageDensity.Tight));
        Assert.False(ClaudeUsageRules.ResetColumn(288, UsageDensity.Tight));
        Assert.False(ClaudeUsageRules.ResetColumn(448, UsageDensity.Full));

        UsageBar[] bars = [new("five_hour", "Session (5h)", "Session", "5h", UsageWindow.Unused)];
        var without = ClaudeUsageRules.LabelSize(bars, 200, UsageDensity.Tight);
        var with = ClaudeUsageRules.LabelSize(bars, 200, UsageDensity.Tight, resetColumn: true);
        Assert.True(with >= without);
    }

    [Fact]
    public void S792_the_session_hero_is_for_a_narrow_tile_with_at_most_two_bars()
    {
        Assert.True(ClaudeUsageRules.SessionHero(new WidgetArea(120, 76), 2));
        Assert.False(ClaudeUsageRules.SessionHero(new WidgetArea(120, 76), 4));
        Assert.False(ClaudeUsageRules.SessionHero(new WidgetArea(200, 106), 2));
        Assert.False(ClaudeUsageRules.SessionHero(default, 2));
    }

    [Fact]
    public void S793_elapsed_comes_from_the_reset_and_the_fixed_window_length()
    {
        var session = ClaudeUsageRules.WindowLength(Bar("five_hour"));
        Assert.Equal(TimeSpan.FromHours(5), session);
        Assert.Equal(TimeSpan.FromDays(7), ClaudeUsageRules.WindowLength(Bar("opus")));

        Assert.Equal(0.8, ClaudeUsageRules.Elapsed(session, Now.AddHours(1), Now)!.Value, 6);
        Assert.Null(ClaudeUsageRules.Elapsed(session, null, Now));
        Assert.Null(ClaudeUsageRules.Elapsed(session, Now.AddMinutes(-1), Now));
        Assert.Equal(0, ClaudeUsageRules.Elapsed(session, Now.AddHours(9), Now)!.Value);
    }

    [Fact]
    public void S793_pace_compares_usage_with_the_share_of_the_window_gone()
    {
        Assert.Equal(UsagePace.Ahead, ClaudeUsageRules.Pace(60, 0.4));
        Assert.Equal(UsagePace.On, ClaudeUsageRules.Pace(43, 0.4));
        Assert.Equal(UsagePace.Under, ClaudeUsageRules.Pace(10, 0.4));
        Assert.Equal("ahead of pace", ClaudeUsageRules.PaceText(UsagePace.Ahead));
    }

    [Fact]
    public void S793_the_reset_clock_names_the_day_only_when_it_is_not_today()
    {
        Assert.Equal("14:20", ClaudeUsageRules.ResetClock(Now.AddHours(2).AddMinutes(20), Now, TimeZoneInfo.Utc));
        Assert.Equal("Wed 16:00", ClaudeUsageRules.ResetClock(Now.AddDays(3).AddHours(4), Now, TimeZoneInfo.Utc));
    }

    [Fact]
    public void S794_S795_the_gauge_takes_a_panel_or_a_tall_column_and_a_footer_from_regular_height()
    {
        Assert.Equal(UsageGaugePlace.Beside, ClaudeUsageRules.GaugePlace(new WidgetArea(800, 516), 4));
        Assert.Equal(UsageGaugePlace.Beside, ClaudeUsageRules.GaugePlace(new WidgetArea(560, 376), 2));
        Assert.Equal(UsageGaugePlace.None, ClaudeUsageRules.GaugePlace(new WidgetArea(560, 376), 4));
        Assert.Equal(UsageGaugePlace.Above, ClaudeUsageRules.GaugePlace(new WidgetArea(200, 356), 2));
        Assert.Equal(UsageGaugePlace.None, ClaudeUsageRules.GaugePlace(new WidgetArea(200, 356), 4));
        Assert.Equal(UsageGaugePlace.None, ClaudeUsageRules.GaugePlace(new WidgetArea(320, 196), 2));
        Assert.Equal(UsageGaugePlace.None, ClaudeUsageRules.GaugePlace(new WidgetArea(800, 516), 0));
        Assert.True(ClaudeUsageRules.Footer(new WidgetArea(320, 196)));
        Assert.False(ClaudeUsageRules.Footer(new WidgetArea(480, 116)));
    }

    [Fact]
    public void S794_the_gauge_opens_at_the_bottom_and_sweeps_clockwise()
    {
        var (sx, sy) = UsageGauge.Point(0, 0, 10, 0);
        Assert.True(sx < 0 && sy > 0);
        var (mx, my) = UsageGauge.Point(0, 0, 10, 0.5);
        Assert.Equal(0, mx, 6);
        Assert.Equal(-10, my, 6);
        var (ex, ey) = UsageGauge.Point(0, 0, 10, 1);
        Assert.True(ex > 0 && ey > 0);
        Assert.False(UsageGauge.IsLargeArc(0.5));
        Assert.True(UsageGauge.IsLargeArc(0.7));
    }
}
