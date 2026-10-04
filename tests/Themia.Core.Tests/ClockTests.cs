using Themia.Core.Model;
using Themia.Core.Time;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

public class TickScheduleTests
{
    [Fact]
    public void A_delay_always_lands_after_the_boundary_never_before()
    {
        // Sweep a whole second in 1 ms steps: every wake must land past the next boundary, or the
        // formatter renders the previous second.
        for (var ms = 0; ms < 1000; ms++)
        {
            var now = new DateTimeOffset(2026, 1, 1, 10, 30, 15, ms, TimeSpan.Zero);
            var delay = TickSchedule.DelayToNextBoundary(now, TimeSpan.FromSeconds(1));
            var wake = now + delay;

            Assert.True(wake.Millisecond >= 0 && wake.Second != now.Second, $"wake at {ms}ms landed in the same second");
            Assert.True(delay > TimeSpan.Zero);
        }
    }

    [Fact]
    public void A_delay_never_exceeds_one_period_plus_the_bias()
    {
        var interval = TimeSpan.FromSeconds(1);

        for (var ms = 0; ms < 1000; ms++)
        {
            var now = new DateTimeOffset(2026, 1, 1, 10, 30, 15, ms, TimeSpan.Zero);
            var delay = TickSchedule.DelayToNextBoundary(now, interval);

            Assert.True(delay <= interval + interval + TickSchedule.Bias, $"delay {delay} at {ms}ms is too long");
        }
    }

    [Fact]
    public void A_minute_subscriber_fires_once_per_minute_not_once_per_second()
    {
        var minute = TimeSpan.FromMinutes(1);
        var start = new DateTimeOffset(2026, 1, 1, 10, 30, 0, TimeSpan.Zero);

        var indices = Enumerable.Range(0, 60)
            .Select(s => TickSchedule.PeriodIndex(start.AddSeconds(s), minute))
            .Distinct()
            .Count();

        Assert.Equal(1, indices);
        Assert.NotEqual(
            TickSchedule.PeriodIndex(start, minute),
            TickSchedule.PeriodIndex(start.AddMinutes(1), minute));
    }

    [Fact]
    public void A_degenerate_interval_does_not_produce_a_zero_or_negative_delay()
    {
        var delay = TickSchedule.DelayToNextBoundary(DateTimeOffset.Now, TimeSpan.Zero);

        Assert.True(delay > TimeSpan.Zero);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(30, 1)]
    [InlineData(60, 60)]
    [InlineData(3600, 60)]
    public void Normalise_collapses_every_request_to_a_second_or_a_minute(int requestedSeconds, int expectedSeconds)
    {
        var normalised = TickSchedule.Normalise(TimeSpan.FromSeconds(requestedSeconds));

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), normalised);
    }
}

public class ClockFormatterTests
{
    private static DateTimeOffset At(int hour, int minute, int second = 0)
        => new(2026, 3, 9, hour, minute, second, TimeSpan.Zero);

    private static ClockConfig Utc(Action<ClockConfig>? tweak = null)
    {
        var config = new ClockConfig { TimeZoneId = "UTC", ShowDate = false };
        tweak?.Invoke(config);
        return config;
    }

    [Fact]
    public void Twenty_four_hour_time_is_zero_padded()
    {
        var reading = ClockFormatter.Read(At(9, 5), Utc(c => c.Use24Hour = true));

        Assert.Equal("09:05", reading.Time);
        Assert.Null(reading.Meridiem);
    }

    [Fact]
    public void Twelve_hour_time_is_not_padded_and_carries_a_meridiem()
    {
        var reading = ClockFormatter.Read(At(9, 5), Utc(c => c.Use24Hour = false));

        Assert.Equal("9:05", reading.Time);
        Assert.Equal("AM", reading.Meridiem);
    }

    [Theory]
    [InlineData(0, 12, "AM")]
    [InlineData(12, 12, "PM")]
    [InlineData(13, 1, "PM")]
    [InlineData(23, 11, "PM")]
    [InlineData(11, 11, "AM")]
    public void Midnight_and_noon_read_as_twelve_not_zero(int hour24, int expectedHour, string expectedMeridiem)
    {
        var reading = ClockFormatter.Read(At(hour24, 30), Utc(c => c.Use24Hour = false));

        Assert.Equal($"{expectedHour}:30", reading.Time);
        Assert.Equal(expectedMeridiem, reading.Meridiem);
        Assert.Equal(expectedHour, ClockFormatter.ToTwelveHour(hour24));
    }

    [Fact]
    public void Seconds_appear_only_when_asked_for()
    {
        Assert.Equal("14:07", ClockFormatter.Read(At(14, 7, 42), Utc(c => c.ShowSeconds = false)).Time);
        Assert.Equal("14:07:42", ClockFormatter.Read(At(14, 7, 42), Utc(c => c.ShowSeconds = true)).Time);
    }

    [Fact]
    public void The_date_is_omitted_unless_enabled()
    {
        Assert.Null(ClockFormatter.Read(At(14, 7), Utc(c => c.ShowDate = false)).Date);
        Assert.Equal("Monday, 9 March", ClockFormatter.Read(At(14, 7), Utc(c => c.ShowDate = true)).Date);
    }

    [Fact]
    public void Hands_sweep_between_ticks_rather_than_stepping()
    {
        var onTheHour = ClockFormatter.Read(At(3, 0), Utc());
        var halfPast = ClockFormatter.Read(At(3, 30), Utc());

        Assert.Equal(90, onTheHour.HourAngle, 3);
        Assert.Equal(105, halfPast.HourAngle, 3); // the hour hand has moved half a division
        Assert.Equal(180, halfPast.MinuteAngle, 3);
    }

    [Fact]
    public void All_hand_angles_stay_within_one_revolution()
    {
        for (var hour = 0; hour < 24; hour++)
        {
            for (var minute = 0; minute < 60; minute += 7)
            {
                var reading = ClockFormatter.Read(At(hour, minute, 33), Utc());

                Assert.InRange(reading.HourAngle, 0, 360);
                Assert.InRange(reading.MinuteAngle, 0, 360);
                Assert.InRange(reading.SecondAngle, 0, 360);
            }
        }
    }

    [Fact]
    public void An_unknown_time_zone_degrades_to_local_instead_of_throwing()
    {
        var config = new ClockConfig { TimeZoneId = "Mars/Olympus_Mons" };

        var zone = config.ResolveTimeZone();

        Assert.Equal(TimeZoneInfo.Local.Id, zone.Id);
        _ = ClockFormatter.Read(DateTimeOffset.UtcNow, config);
    }

    [Fact]
    public void A_clock_without_seconds_only_asks_to_be_woken_once_a_minute()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), new ClockConfig { ShowSeconds = false, Face = ClockFace.Digital }.TickInterval);
        Assert.Equal(TimeSpan.FromSeconds(1), new ClockConfig { ShowSeconds = true }.TickInterval);

        // An analog face sweeps its second hand, so it needs seconds regardless of the digital toggle.
        Assert.Equal(TimeSpan.FromSeconds(1), new ClockConfig { ShowSeconds = false, Face = ClockFace.Analog }.TickInterval);
    }
}
