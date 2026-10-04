using Themia.Core.Developer;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>[S-741][S-742] How the repository widget shares its height between its lists.</summary>
public sealed class DevLayoutTests
{
    private const double Row = 32;
    private const double Heading = 28;

    [Fact]
    public void S741_every_list_gets_a_heading_and_a_row_before_any_gets_a_second()
    {
        // Four lists of three; room for four headings and four rows (240) plus two more rows.
        var quota = DevLayout.Quota(240 + 64, Row, Heading, [3, 3, 3, 3]);

        Assert.Equal([2, 2, 1, 1], quota);
    }

    [Fact]
    public void S741_a_short_tile_shows_the_first_lists_whole_rows_only()
    {
        // 130: two heading+row pairs (120), not a third, and no half row.
        Assert.Equal([1, 1, 0, 0], DevLayout.Quota(130, Row, Heading, [3, 3, 3, 3]));
    }

    [Fact]
    public void S741_an_empty_list_costs_nothing_and_its_room_goes_to_the_others()
    {
        // Two headings and five rows: dealt alternately, the first list getting the odd one.
        Assert.Equal([0, 3, 0, 2], DevLayout.Quota((Heading * 2) + (Row * 5), Row, Heading, [0, 4, 0, 9]));
    }

    [Fact]
    public void S741_a_big_tile_shows_every_list_whole_and_stops()
    {
        Assert.Equal([3, 2, 5, 0], DevLayout.Quota(10_000, Row, Heading, [3, 2, 5, 0]));
    }

    [Fact]
    public void S741_without_headings_rows_are_dealt_in_priority_order()
    {
        // A wide strip: three rows, no headings — the newest of each of the first three lists.
        Assert.Equal([1, 1, 1, 0], DevLayout.Quota(108, Row, 0, [3, 3, 3, 3]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void S741_an_unusable_height_shows_nothing(double height)
    {
        Assert.Equal([0, 0], DevLayout.Quota(height, Row, Heading, [3, 3]));
    }

    [Fact]
    public void S741_rounding_a_fraction_short_still_fits()
    {
        Assert.Equal([1], DevLayout.Quota(Heading + Row - 0.4, Row, Heading, [1]));
    }

    [Theory]
    [InlineData(519.9, 1)]
    [InlineData(520, 2)]
    [InlineData(800, 2)]
    [InlineData(double.NaN, 1)]
    public void S742_two_columns_from_520(double width, int columns)
    {
        Assert.Equal(columns, DevLayout.Columns(width));
    }
}
