using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>The ping tile's number. Separate from <see cref="FormattingTests"/> only by subject.</summary>
public class LatencyFormattingTests
{
    [Theory]
    [InlineData(0, "0 ms")]
    [InlineData(1, "1 ms")]
    [InlineData(18, "18 ms")]
    [InlineData(18.4, "18 ms")]
    [InlineData(18.6, "19 ms")]
    [InlineData(999, "999 ms")]
    [InlineData(999.4, "999 ms")]
    public void Reads_in_milliseconds_below_a_second(double milliseconds, string expected)
    {
        Assert.Equal(expected, Formatting.Latency(milliseconds));
    }

    [Theory]
    [InlineData(1000, "1.0 s")]
    [InlineData(1438, "1.4 s")]
    [InlineData(2500, "2.5 s")]
    [InlineData(12000, "12.0 s")]
    public void Switches_to_seconds_once_milliseconds_stop_saying_anything(double milliseconds, string expected)
    {
        Assert.Equal(expected, Formatting.Latency(milliseconds));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    public void Says_it_does_not_know_rather_than_printing_nonsense(double milliseconds)
    {
        Assert.Equal("--", Formatting.Latency(milliseconds));
    }

    [Fact]
    public void Never_renders_a_culture_specific_separator()
    {
        // The app builds with InvariantGlobalization, so a comma here would mean the format string
        // went through a culture the rest of the app does not have.
        Assert.DoesNotContain(",", Formatting.Latency(1438));
    }
}
