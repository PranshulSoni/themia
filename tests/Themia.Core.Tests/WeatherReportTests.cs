using Themia.Core.Model;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The strings the weather widgets print, and the WMO table behind them. Every input here is one
/// the service can genuinely send, including the ones it is not supposed to.
/// </summary>
public class WeatherReportTests
{
    [Theory]
    [InlineData(0, "Clear sky")]
    [InlineData(2, "Partly cloudy")]
    [InlineData(45, "Fog")]
    [InlineData(63, "Rain")]
    [InlineData(75, "Heavy snow")]
    [InlineData(95, "Thunderstorm")]
    public void A_known_code_is_described_in_words(int code, string expected)
        => Assert.Equal(expected, WeatherReport.Describe(code));

    [Fact]
    public void An_unknown_code_says_so_rather_than_being_guessed_at()
    {
        // The reader can see that the service said something this app has no word for, which is
        // true. A blank would read as a broken widget.
        Assert.Equal("Code 78", WeatherReport.Describe(78));
    }

    [Fact]
    public void A_missing_code_is_unknown_rather_than_clear_sky()
        => Assert.Equal("Unknown", WeatherReport.Describe(null));

    [Theory]
    [InlineData(0, WeatherLook.Clear)]
    [InlineData(3, WeatherLook.Overcast)]
    [InlineData(48, WeatherLook.Fog)]
    [InlineData(82, WeatherLook.Rain)]
    [InlineData(86, WeatherLook.Snow)]
    [InlineData(99, WeatherLook.Thunderstorm)]
    [InlineData(78, WeatherLook.Unknown)]
    public void The_icon_family_follows_the_code(int code, WeatherLook expected)
        => Assert.Equal(expected, WeatherReport.Look(code));

    // ---- temperature -----------------------------------------------------------------------

    [Theory]
    [InlineData(18.2, TemperatureUnit.Celsius, "18°C")]
    [InlineData(17.6, TemperatureUnit.Celsius, "18°C")]
    [InlineData(-0.4, TemperatureUnit.Celsius, "0°C")]
    [InlineData(-3.5, TemperatureUnit.Celsius, "-4°C")]
    [InlineData(64.4, TemperatureUnit.Fahrenheit, "64°F")]
    public void A_temperature_is_a_whole_number_and_its_unit(double value, TemperatureUnit unit, string expected)
        => Assert.Equal(expected, WeatherReport.Temperature(value, unit));

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_missing_temperature_prints_and_never_throws(double? value)
        => Assert.Equal("--", WeatherReport.Temperature(value, TemperatureUnit.Celsius));

    [Fact]
    public void A_column_temperature_carries_a_degree_sign_and_no_scale_letter()
        => Assert.Equal("12°", WeatherReport.Degrees(11.7));

    // ---- the supporting numbers ------------------------------------------------------------

    [Theory]
    [InlineData(59.0, "59%")]
    [InlineData(0.0, "0%")]
    [InlineData(100.0, "100%")]
    public void Humidity_is_a_whole_percentage(double value, string expected)
        => Assert.Equal(expected, WeatherReport.Humidity(value));

    [Theory]
    [InlineData(140.0, "100%")]
    [InlineData(-5.0, "0%")]
    public void A_humidity_outside_the_possible_range_is_clamped_rather_than_printed(double value, string expected)
        => Assert.Equal(expected, WeatherReport.Humidity(value));

    [Fact]
    public void Wind_is_in_the_unit_it_was_asked_for()
        => Assert.Equal("7 km/h", WeatherReport.Wind(7.4));

    [Fact]
    public void A_negative_wind_speed_is_not_a_speed()
        => Assert.Equal("--", WeatherReport.Wind(-3));

    [Theory]
    [InlineData(0.0, "")]
    [InlineData(-1.0, "")]
    [InlineData(null, "")]
    public void A_dry_day_shows_nothing_rather_than_a_zero(double? value, string expected)
    {
        // A column of zeroes is noise that makes the one wet day harder to find.
        Assert.Equal(expected, WeatherReport.Precipitation(value));
    }

    [Theory]
    [InlineData(0.2, "0.2 mm")]
    [InlineData(4.5, "4.5 mm")]
    [InlineData(12.0, "12 mm")]
    public void Precipitation_keeps_a_decimal_only_while_it_carries(double value, string expected)
        => Assert.Equal(expected, WeatherReport.Precipitation(value));

    // ---- day labels ------------------------------------------------------------------------

    [Fact]
    public void The_current_day_is_labelled_today()
    {
        var today = new DateOnly(2026, 9, 24);

        Assert.Equal("Today", WeatherReport.DayLabel(today, today));
    }

    [Fact]
    public void A_forecast_restored_on_a_later_day_does_not_still_say_today()
    {
        // The label is computed against the current day rather than baked in at fetch time. Getting
        // this wrong means a cache saved last night labels a stale row "Today" this morning, and
        // "Today" is the one label a reader acts on without checking.
        var fetchedOn = new DateOnly(2026, 9, 24);
        var todayNow = new DateOnly(2026, 9, 25);

        Assert.Equal("Thu", WeatherReport.DayLabel(fetchedOn, todayNow));
        Assert.Equal("Today", WeatherReport.DayLabel(todayNow, todayNow));
    }

    [Theory]
    [InlineData(2026, 9, 27, "Sun")]
    [InlineData(2026, 9, 28, "Mon")]
    [InlineData(2026, 10, 2, "Fri")]
    public void Other_days_get_their_short_weekday_name(int year, int month, int day, string expected)
        => Assert.Equal(expected, WeatherReport.DayLabel(new DateOnly(year, month, day), new DateOnly(2026, 9, 24)));

    // ---- the range bar ---------------------------------------------------------------------

    [Fact]
    public void A_day_at_the_bottom_of_the_week_starts_the_bar_at_the_left()
    {
        var (start, end) = WeatherReport.RangeBar(low: 5, high: 10, coldest: 5, warmest: 25);

        Assert.Equal(0, start, 3);
        Assert.Equal(0.25, end, 3);
    }

    [Fact]
    public void A_day_in_the_middle_sits_in_the_middle()
    {
        var (start, end) = WeatherReport.RangeBar(low: 10, high: 20, coldest: 0, warmest: 40);

        Assert.Equal(0.25, start, 3);
        Assert.Equal(0.5, end, 3);
    }

    [Fact]
    public void A_week_with_no_spread_draws_a_full_bar_rather_than_dividing_by_zero()
    {
        var (start, end) = WeatherReport.RangeBar(low: 14, high: 14, coldest: 14, warmest: 14);

        Assert.Equal(0, start, 3);
        Assert.Equal(1, end, 3);
    }

    [Fact]
    public void A_day_whose_low_and_high_arrived_the_wrong_way_round_still_draws()
    {
        var (start, end) = WeatherReport.RangeBar(low: 20, high: 10, coldest: 0, warmest: 40);

        Assert.True(start <= end);
    }

    [Theory]
    [InlineData(double.NaN, 10, 0, 40)]
    [InlineData(5, double.PositiveInfinity, 0, 40)]
    [InlineData(5, 10, double.NaN, 40)]
    public void A_non_finite_reading_cannot_produce_a_non_finite_bar(double low, double high, double coldest, double warmest)
    {
        var (start, end) = WeatherReport.RangeBar(low, high, coldest, warmest);

        Assert.True(double.IsFinite(start));
        Assert.True(double.IsFinite(end));
    }
}
