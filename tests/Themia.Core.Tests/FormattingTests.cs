using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

public class FormattingTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(-5, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1610612736, "1.5 GB")]
    public void Bytes_uses_binary_units_like_the_rest_of_windows(double bytes, string expected)
    {
        Assert.Equal(expected, Formatting.Bytes(bytes));
    }

    [Fact]
    public void Bytes_drops_the_decimal_once_it_stops_carrying_information()
    {
        // "931 GB" is useful; "931.0 GB" is noise.
        Assert.Equal("931 GB", Formatting.Bytes(931.4 * 1024 * 1024 * 1024));
        Assert.Equal("1.4 GB", Formatting.Bytes(1.44 * 1024 * 1024 * 1024));
    }

    [Fact]
    public void Bytes_never_runs_out_of_units()
    {
        var huge = Formatting.Bytes(double.MaxValue / 2);

        Assert.EndsWith("PB", huge);
    }

    [Fact]
    public void Bytes_handles_non_finite_input_without_throwing()
    {
        Assert.Equal("0 B", Formatting.Bytes(double.NaN));
        Assert.Equal("0 B", Formatting.Bytes(double.NegativeInfinity));
    }

    [Theory]
    [InlineData(0, "0%")]
    [InlineData(0.5, "50%")]
    [InlineData(1, "100%")]
    [InlineData(2, "100%")]
    [InlineData(-1, "0%")]
    public void Percent_clamps_to_the_zero_to_one_range(double fraction, string expected)
    {
        Assert.Equal(expected, Formatting.Percent(fraction));
    }

    [Fact]
    public void Percent_never_rounds_a_busy_machine_down_to_zero()
    {
        // 0.001 load is not "nothing is happening".
        Assert.Equal("1%", Formatting.Percent(0.001));
    }

    [Fact]
    public void Percent_never_rounds_an_incomplete_value_up_to_one_hundred()
    {
        // A disk at 99.8% is not full, and saying "100%" would read as though it were.
        Assert.Equal("99%", Formatting.Percent(0.998));
    }

    [Fact]
    public void Percent_reports_non_finite_input_as_unknown_rather_than_zero()
    {
        Assert.Equal("--", Formatting.Percent(double.NaN));
    }

    [Theory]
    [InlineData(0, "--")]
    [InlineData(-60, "--")]
    [InlineData(90, "1m")]
    [InlineData(2100, "35m")]
    [InlineData(15600, "4h 20m")]
    public void Duration_reads_as_time_remaining(int seconds, string expected)
    {
        Assert.Equal(expected, Formatting.Duration(TimeSpan.FromSeconds(seconds)));
    }

    // ---- relative timestamps ----------------------------------------------------------------

    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "now")]
    [InlineData(30, "now")]
    [InlineData(4 * 60, "4m")]
    [InlineData(59 * 60, "59m")]
    [InlineData(3 * 3600, "3h")]
    [InlineData(23 * 3600, "23h")]
    public void Ago_counts_up_through_minutes_then_hours(int secondsAgo, string expected)
    {
        Assert.Equal(expected, Formatting.Ago(Now.AddSeconds(-secondsAgo), Now));
    }

    [Fact]
    public void Ago_uses_a_weekday_within_the_last_week()
    {
        // 24 Sep 2026 is a Thursday, so two days earlier is Tuesday.
        Assert.Equal("Tue", Formatting.Ago(Now.AddDays(-2), Now));
    }

    [Fact]
    public void Ago_falls_back_to_a_date_beyond_a_week()
    {
        Assert.Equal("1 Sep", Formatting.Ago(new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero), Now));
    }

    [Fact]
    public void Ago_treats_a_future_timestamp_as_now_rather_than_a_negative_age()
    {
        // Clock skew between a mail provider and this machine is ordinary; "-3m" is not.
        Assert.Equal("now", Formatting.Ago(Now.AddMinutes(5), Now));
    }
}
