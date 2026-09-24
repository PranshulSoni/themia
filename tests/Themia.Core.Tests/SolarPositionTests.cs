using Themia.Core.Astronomy;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// Sunrise, sunset and the countdown. [S-02] [S-03] [S-06] [S-07]
/// <para>
/// Every case pins the instant explicitly: nothing here reads the machine clock or its time zone,
/// so a failure is always the astronomy and never the test host.
/// </para>
/// </summary>
public class SolarPositionTests
{
    private const double Zurich_Lat = 47.3769;
    private const double Zurich_Lon = 8.5417;

    /// <summary>Published tables round to the minute and model refraction slightly differently.</summary>
    private static readonly TimeSpan TableTolerance = TimeSpan.FromMinutes(5);

    private static DateTimeOffset Utc(int year, int month, int day, int hour = 12, int minute = 0)
        => new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    /// <summary>The calendar date at a fixed UTC offset, so an assertion never depends on the host's zone.</summary>
    private static DateTime LocalDate(DateTimeOffset moment, int offsetHours)
        => moment.UtcDateTime.AddHours(offsetHours).Date;

    /// <summary>The instant of hour:30 local time on 2026-08-10 at a place that many hours off UTC.</summary>
    private static DateTimeOffset LocalInstant(int hour, int offsetHours)
        => new DateTimeOffset(2026, 8, 10, 0, 30, 0, TimeSpan.Zero).AddHours(hour - offsetHours);

    [Fact]
    public void S_02_Sunrise_precedes_sunset_at_a_temperate_latitude()
    {
        var times = SolarPosition.TimesFor(Utc(2024, 6, 21), Zurich_Lat, Zurich_Lon);

        Assert.Equal(PolarState.None, times.Polar);
        Assert.NotNull(times.Sunrise);
        Assert.NotNull(times.Sunset);
        Assert.True(times.Sunrise < times.Sunset);
    }

    /// <summary>
    /// The known-value check. Zurich at both solstices, against the published table (gaisma.com,
    /// which lists 05:29/21:26 CEST in June and 08:10/16:38 CET in December).
    /// </summary>
    [Theory]
    [InlineData(2024, 6, 21, 3, 29, 19, 26)]
    [InlineData(2024, 12, 21, 7, 10, 15, 38)]
    public void S_02_Zurich_solstices_match_the_published_table(
        int year, int month, int day, int riseHour, int riseMinute, int setHour, int setMinute)
    {
        var times = SolarPosition.TimesFor(Utc(year, month, day), Zurich_Lat, Zurich_Lon);

        Assert.NotNull(times.Sunrise);
        Assert.NotNull(times.Sunset);

        var expectedRise = Utc(year, month, day, riseHour, riseMinute);
        var expectedSet = Utc(year, month, day, setHour, setMinute);

        Assert.True(
            (times.Sunrise.Value - expectedRise).Duration() <= TableTolerance,
            $"sunrise {times.Sunrise:HH:mm} is more than {TableTolerance.TotalMinutes} min from the published {expectedRise:HH:mm} UTC");

        Assert.True(
            (times.Sunset.Value - expectedSet).Duration() <= TableTolerance,
            $"sunset {times.Sunset:HH:mm} is more than {TableTolerance.TotalMinutes} min from the published {expectedSet:HH:mm} UTC");
    }

    [Fact]
    public void S_02_Zurich_day_length_matches_the_published_table_at_both_solstices()
    {
        var summer = SolarPosition.TimesFor(Utc(2024, 6, 21), Zurich_Lat, Zurich_Lon);
        var winter = SolarPosition.TimesFor(Utc(2024, 12, 21), Zurich_Lat, Zurich_Lon);

        var longest = summer.Sunset!.Value - summer.Sunrise!.Value;
        var shortest = winter.Sunset!.Value - winter.Sunrise!.Value;

        // 15h57m and 8h28m in the published table.
        Assert.True((longest - TimeSpan.FromMinutes(957)).Duration() <= TableTolerance, $"midsummer day {longest}");
        Assert.True((shortest - TimeSpan.FromMinutes(508)).Duration() <= TableTolerance, $"midwinter day {shortest}");
    }

    /// <summary>Both poles, both seasons: the sun that never sets and the sun that never rises. [S-03]</summary>
    [Theory]
    [InlineData(78, 15, 2024, 6, 21, PolarState.Day)]    // Svalbard, midsummer
    [InlineData(78, 15, 2024, 12, 21, PolarState.Night)] // Svalbard, midwinter
    [InlineData(-75, 0, 2024, 6, 21, PolarState.Night)]  // Antarctica, June is midwinter
    [InlineData(-75, 0, 2024, 12, 21, PolarState.Day)]
    [InlineData(89.9, 0, 2024, 6, 21, PolarState.Day)]
    [InlineData(-89.9, 0, 2024, 6, 21, PolarState.Night)]
    public void S_03_Polar_day_and_night_report_no_times_and_a_marker(
        double latitude, double longitude, int year, int month, int day, PolarState expected)
    {
        var times = SolarPosition.TimesFor(Utc(year, month, day), latitude, longitude);

        Assert.Equal(expected, times.Polar);
        Assert.Null(times.Sunrise);
        Assert.Null(times.Sunset);
    }

    /// <summary>
    /// Regression for the original's issue #105, "doesn't seem to work for southern hemisphere". The
    /// astronomy was right in both hemispheres; the day index was the UTC day, so at far-east
    /// longitudes the whole local morning reported the previous day. [S-06]
    /// </summary>
    [Theory]
    [InlineData("Sydney", -33.87, 151.21, 10)]
    [InlineData("Auckland", -36.85, 174.76, 12)]
    [InlineData("Tokyo", 35.68, 139.65, 9)]
    [InlineData("Honolulu", 21.31, -157.86, -10)]
    [InlineData("Buenos Aires", -34.6, -58.38, -3)]
    public void S_06_Sunrise_and_sunset_land_on_the_local_solar_day(string place, double lat, double lon, int offsetHours)
    {
        var expected = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Unspecified);

        for (var hour = 1; hour < 24; hour++)
        {
            var now = LocalInstant(hour, offsetHours);
            var times = SolarPosition.TimesFor(now, lat, lon);

            Assert.NotNull(times.Sunrise);
            Assert.NotNull(times.Sunset);
            Assert.Equal(expected, LocalDate(times.Sunrise.Value, offsetHours));
            Assert.Equal(expected, LocalDate(times.Sunset.Value, offsetHours));
        }

        Assert.True(true, place);
    }

    [Theory]
    [InlineData("Sydney", -33.87, 151.21, 10)]
    [InlineData("Auckland", -36.85, 174.76, 12)]
    [InlineData("Tokyo", 35.68, 139.65, 9)]
    [InlineData("Honolulu", 21.31, -157.86, -10)]
    [InlineData("Zurich", Zurich_Lat, Zurich_Lon, 2)]
    public void S_07_The_next_event_is_strictly_ahead_at_every_hour(string place, double lat, double lon, int offsetHours)
    {
        for (var hour = 0; hour < 24; hour++)
        {
            var now = LocalInstant(hour, offsetHours);
            var next = SolarPosition.NextEvent(now, lat, lon);

            Assert.True(next.HasValue, $"{place} at {hour}:30 reported no next event");
            Assert.True(next!.Value.At > now, $"{place} at {hour}:30 reported a past event at {next.Value.At:u}");

            // ...and within a day, so it is the *next* one and not a later day's.
            Assert.True(next.Value.At - now < TimeSpan.FromDays(1), $"{place} at {hour}:30 skipped a day");
        }
    }

    /// <summary>
    /// The bug the countdown showed: a stale sunrise from the UTC day, already past, rendered as
    /// <c>0m</c> all morning. [S-07]
    /// </summary>
    [Fact]
    public void S_07_Sydney_morning_counts_down_to_sunset_not_a_stale_sunrise()
    {
        var now = LocalInstant(9, 10);
        var next = SolarPosition.NextEvent(now, -33.87, 151.21);

        Assert.True(next.HasValue);
        Assert.Equal(SunEventKind.Sunset, next!.Value.Kind);

        var minutes = (next.Value.At - now).TotalMinutes;
        Assert.InRange(minutes, 7 * 60, 9 * 60); // ~7h50m to a ~17:20 sunset
    }

    /// <summary>
    /// Straight across a sunset: the event flips from sunset to sunrise the moment the sun goes
    /// down, and the remaining time never reaches zero or turns negative on the way. [S-07]
    /// </summary>
    [Fact]
    public void S_07_Across_a_sunset_boundary_the_countdown_never_sticks_at_zero()
    {
        var sunset = SolarPosition.TimesFor(Utc(2026, 8, 10), Zurich_Lat, Zurich_Lon).Sunset;
        Assert.NotNull(sunset);

        for (var offset = -10.0; offset <= 10.0; offset += 0.5)
        {
            var now = sunset.Value.AddMinutes(offset);
            var next = SolarPosition.NextEvent(now, Zurich_Lat, Zurich_Lon);

            Assert.True(next.HasValue, $"no event {offset} min from sunset");
            Assert.True(next!.Value.At > now, $"a past event {offset} min from sunset");
            Assert.True((next.Value.At - now).TotalSeconds > 0, $"a zero countdown {offset} min from sunset");

            var expected = now < sunset.Value ? SunEventKind.Sunset : SunEventKind.Sunrise;
            Assert.Equal(expected, next.Value.Kind);
        }
    }

    [Fact]
    public void S_07_No_event_inside_the_polar_window_returns_null()
    {
        Assert.Null(SolarPosition.NextEvent(Utc(2024, 12, 21), -85, 0));
        Assert.Null(SolarPosition.NextEvent(Utc(2024, 6, 21), 85, 0));
    }

    /// <summary>
    /// The subsolar point is the instantaneous one, so it has to move between ticks — and at the
    /// solstice its latitude is the obliquity. [S-08]
    /// </summary>
    [Fact]
    public void S_08_The_subsolar_point_sits_at_the_tropic_on_a_solstice()
    {
        var june = SolarPosition.SubsolarPoint(Utc(2024, 6, 20, 20, 51)); // 2024 June solstice
        var december = SolarPosition.SubsolarPoint(Utc(2024, 12, 21, 9, 21));

        Assert.True(Math.Abs(june.Latitude - 23.44) < 0.2, $"June subsolar latitude {june.Latitude:F2}");
        Assert.True(Math.Abs(december.Latitude + 23.44) < 0.2, $"December subsolar latitude {december.Latitude:F2}");
    }

    [Fact]
    public void S_08_The_subsolar_point_crosses_the_equator_at_an_equinox()
    {
        var march = SolarPosition.SubsolarPoint(Utc(2024, 3, 20, 3, 6));
        Assert.True(Math.Abs(march.Latitude) < 0.2, $"equinox subsolar latitude {march.Latitude:F2}");
    }

    /// <summary>
    /// A rounded day index would pin the marker to one moment and freeze it for the whole day. The
    /// sun's ground track moves ~15° of longitude an hour, westward. [S-08]
    /// </summary>
    [Fact]
    public void S_08_The_subsolar_point_moves_with_the_hour_rather_than_the_day()
    {
        var start = Utc(2026, 8, 10, 6);
        var drift = LongitudeDelta(
            SolarPosition.SubsolarPoint(start).Longitude,
            SolarPosition.SubsolarPoint(start.AddHours(1)).Longitude);

        Assert.InRange(drift, -15.5, -14.5);
    }

    [Fact]
    public void S_08_The_subsolar_point_is_overhead_at_local_solar_noon()
    {
        // Solar noon at Greenwich on this date is a few minutes off 12:00 UTC (the equation of
        // time); the sub-point's longitude must be within a degree or so of the meridian.
        var noon = SolarPosition.SubsolarPoint(Utc(2026, 8, 10, 12, 5));
        Assert.True(Math.Abs(noon.Longitude) < 2, $"subsolar longitude at noon UTC was {noon.Longitude:F2}");
    }

    /// <summary>Signed difference b - a, wrapped into -180..180.</summary>
    internal static double LongitudeDelta(double a, double b) => (((b - a + 540) % 360) + 360) % 360 - 180;
}
