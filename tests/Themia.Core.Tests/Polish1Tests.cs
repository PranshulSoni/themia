using Themia.Core.Habits;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>[S-1560]..[S-1579] The widget polish pass: keyboard lease in the remaining lists, layout gaps.</summary>
public sealed class Polish1Tests
{
    private static readonly string[] Ranking = ["412", "88", "1290", "7"];

    // ---- [S-1565] a cursor that follows its item ---------------------------------------------------

    [Theory]
    [InlineData(null, WidgetKey.Down, "412")]
    [InlineData(null, WidgetKey.Home, "412")]
    [InlineData(null, WidgetKey.Up, "7")]
    [InlineData(null, WidgetKey.End, "7")]
    [InlineData("88", WidgetKey.Down, "1290")]
    [InlineData("88", WidgetKey.Up, "412")]
    [InlineData("412", WidgetKey.Up, "412")]
    [InlineData("7", WidgetKey.Down, "7")]
    [InlineData("1290", WidgetKey.Home, "412")]
    public void S1565_keys_move_the_cursor_and_clamp_at_the_ends(string? current, WidgetKey key, string expected)
        => Assert.Equal(new KeyedCursorResult(expected, true), KeyedCursor.Move(Ranking, current, key));

    [Fact]
    public void S1565_the_cursor_rides_with_its_item_when_the_list_reshuffles()
    {
        // 88 was second; after a re-rank it is last, and Up moves from where it is now.
        string[] reranked = ["1290", "412", "7", "88"];

        Assert.Equal(3, KeyedCursor.IndexOf(reranked, "88"));
        Assert.Equal(new KeyedCursorResult("7", true), KeyedCursor.Move(reranked, "88", WidgetKey.Up));
    }

    [Fact]
    public void S1565_an_item_that_left_the_list_marks_no_row_and_the_next_step_starts_from_an_end()
    {
        Assert.Null(KeyedCursor.IndexOf(Ranking, "999"));
        Assert.Equal(new KeyedCursorResult("412", true), KeyedCursor.Move(Ranking, "999", WidgetKey.Down));
        Assert.Equal(new KeyedCursorResult("7", true), KeyedCursor.Move(Ranking, "999", WidgetKey.Up));
    }

    [Fact]
    public void S1565_escape_clears_and_says_whether_there_was_anything_to_clear()
    {
        Assert.Equal(new KeyedCursorResult(null, true), KeyedCursor.Move(Ranking, "88", WidgetKey.Escape));
        Assert.Equal(new KeyedCursorResult(null, false), KeyedCursor.Move(Ranking, null, WidgetKey.Escape));
    }

    [Theory]
    [InlineData(WidgetKey.Left)]
    [InlineData(WidgetKey.Right)]
    [InlineData(WidgetKey.Enter)]
    [InlineData(WidgetKey.Tab)]
    [InlineData(WidgetKey.PageDown)]
    public void S1565_keys_that_are_not_a_lists_go_on_and_leave_the_cursor(WidgetKey key)
        => Assert.Equal(new KeyedCursorResult("88", false), KeyedCursor.Move(Ranking, "88", key));

    [Fact]
    public void S1565_an_empty_list_has_no_cursor_to_move()
        => Assert.Equal(new KeyedCursorResult(null, false), KeyedCursor.Move([], null, WidgetKey.Down));

    // ---- [S-1569] Bluetooth detail panel -----------------------------------------------------------

    private static BluetoothRowPlan Bluetooth(double width, double height, int count)
        => BluetoothLayout.Plan(new WidgetArea(width, height), height - 14, count);

    [Theory]
    [InlineData(560, 376, 1, true)]
    [InlineData(560, 376, 4, true)]
    [InlineData(560, 376, 6, false)]
    [InlineData(800, 516, 1, true)]
    [InlineData(800, 516, 14, false)]
    [InlineData(320, 196, 1, false)]
    [InlineData(200, 356, 1, true)]
    [InlineData(200, 356, 6, false)]
    [InlineData(480, 116, 1, false)]
    [InlineData(560, 376, 0, false)]
    public void S1569_a_short_list_on_a_large_tile_gets_the_detail_panel(double width, double height, int count, bool detail)
        => Assert.Equal(detail, Bluetooth(width, height, count).Detail);

    [Fact]
    public void S1569_the_panel_never_shows_on_an_unmeasured_tile()
        => Assert.False(BluetoothLayout.Plan(default, 0, 1).Detail);

    [Fact]
    public void S1569_the_address_is_written_like_windows_settings_writes_it()
    {
        Assert.Equal("A4:C3:F0:85:AC:2E", BluetoothRules.AddressText("BluetoothLE#BluetoothLE00:1a:7d:da:71:13-a4:c3:f0:85:ac:2e"));
        Assert.Equal("00:00:00:00:00:0F", BluetoothRules.AddressText("Bluetooth#x-00000000000f"));
        Assert.Null(BluetoothRules.AddressText("fixture:Pixel 8"));
        Assert.Null(BluetoothRules.AddressText(null));
    }

    [Fact]
    public void S1569_the_panel_shows_the_picked_device_while_it_is_listed_else_the_first()
    {
        BluetoothDeviceEntry[] devices =
        [
            new("Headphones", "a", true, BluetoothDeviceKind.Audio, 80),
            new("Mouse", "b", false, BluetoothDeviceKind.Mouse, null),
        ];

        Assert.Equal("b", BluetoothRules.Selected(devices, "b")?.Id);
        Assert.Equal("a", BluetoothRules.Selected(devices, "gone")?.Id);
        Assert.Equal("a", BluetoothRules.Selected(devices, null)?.Id);
        Assert.Null(BluetoothRules.Selected([], "a"));
    }

    // ---- [S-1571] Habits summary line ---------------------------------------------------------------

    private static HabitLayoutPlan Habits(double width, double height, int count)
        => HabitLayout.Plan(new WidgetArea(width, height), new HabitLayoutInput(count, true, true, 7), 40, 32);

    [Fact]
    public void S1571_m_gives_the_height_no_row_can_use_to_the_summary_line_and_never_overflows()
    {
        foreach (var count in new[] { 1, 2, 3, 6, 12 })
        {
            var plan = Habits(320, 196, count);
            var used = plan.RowHeight /* add row */ + (plan.Rows * plan.RowHeight)
                + (plan.Summary ? (plan.SummaryBar ? HabitLayout.SummaryHeight : HabitLayout.SummaryLineHeight) : 0);

            Assert.True(plan.Summary, $"{count} habits");
            Assert.False(plan.SummaryBar);
            Assert.True(used <= 196 - HabitLayout.ListBottomPadding + Disclosure.Tolerance, $"{count} habits use {used}");
        }
    }

    [Fact]
    public void S1571_the_full_strip_stays_for_expanded_heights_and_compact_heights_get_none()
    {
        Assert.True(Habits(560, 376, 6).SummaryBar);
        Assert.True(Habits(560, 376, 6).Summary);
        Assert.False(Habits(200, 106, 6).Summary);
        Assert.False(Habits(480, 116, 6).Summary);
    }
}
