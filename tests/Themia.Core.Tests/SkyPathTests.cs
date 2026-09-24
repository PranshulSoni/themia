using Themia.Core.Astronomy;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The curve the widget draws is the sun's real path, and the marker on it is where the sun really
/// is. [S-10] [S-11] [S-12]
/// </summary>
public class SkyPathTests
{
    private const double Zurich_Lat = 47.37;
    private const double Zurich_Lon = 8.54;

    /// <summary>Angles are compared to 1e-6 degrees: these are closed-form identities, not fits.</summary>
    private const double AngleTolerance = 1e-6;

    private static DateTimeOffset Utc(int year, int month, int day, int hour = 0, int minute = 0)
        => new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(47.37, 15.6)]
    [InlineData(47.37, -23.4)]
    [InlineData(-33.87, -20)]
    [InlineData(0, 0)]
    [InlineData(64.15, 23.4)]
    public void S_11_The_curve_peaks_at_the_true_noon_altitude(double latitude, double declination)
    {
        Assert.Equal(
            90 - Math.Abs(latitude - declination),
            SkyPath.AltitudeAtHourAngle(0, declination, latitude),
            AngleTolerance);
    }

    [Theory]
    [InlineData(47.37, 15.6)]
    [InlineData(-33.87, -20)]
    [InlineData(10, 5)]
    [InlineData(-54.8, 12)]
    public void S_11_The_curve_crosses_the_horizon_exactly_at_the_ends_of_the_arc(double latitude, double declination)
    {
        var arc = SkyPath.SemiDiurnalArc(declination, latitude);

        Assert.Equal(0, SkyPath.AltitudeAtHourAngle(arc, declination, latitude), AngleTolerance);
        Assert.Equal(0, SkyPath.AltitudeAtHourAngle(-arc, declination, latitude), AngleTolerance);
    }

    [Theory]
    [InlineData(-54.8)]
    [InlineData(-33.87)]
    [InlineData(0)]
    [InlineData(47.37)]
    [InlineData(64.15)]
    public void S_11_An_equinox_gives_a_twelve_hour_day_everywhere(double latitude)
        => Assert.Equal(90, SkyPath.SemiDiurnalArc(0, latitude), AngleTolerance); // 90 degrees of hour angle is 6 hours

    [Fact]
    public void S_03_A_circumpolar_sun_and_a_polar_night_are_the_degenerate_arcs()
    {
        Assert.Equal(180.0, SkyPath.SemiDiurnalArc(23.4, 78)); // Svalbard midsummer: never sets
        Assert.Equal(0.0, SkyPath.SemiDiurnalArc(-23.4, 78));  // ...and never rises in midwinter
    }

    [Fact]
    public void S_11_The_same_place_sits_lower_in_winter_than_in_summer()
    {
        var summer = SkyPath.AltitudeAtHourAngle(0, 23.4, Zurich_Lat);
        var winter = SkyPath.AltitudeAtHourAngle(0, -23.4, Zurich_Lat);

        Assert.Equal(66.03, summer, 1);
        Assert.Equal(19.23, winter, 1);
        Assert.Equal(46.8, summer - winter, 1); // twice the declination swing
    }

    /// <summary>
    /// The whole point of [S-11]. A semicircle would put the sun at sin(pi/4) = 0.71 of its peak a
    /// quarter of the way through the day; the real path, at Zurich in August, is at 0.61, because
    /// the sun climbs fast near the horizon and loiters near its peak.
    /// </summary>
    [Fact]
    public void S_11_The_path_is_not_a_semicircle()
    {
        const double declination = 15.6;

        var peak = SkyPath.AltitudeAtHourAngle(0, declination, Zurich_Lat);
        var arc = SkyPath.SemiDiurnalArc(declination, Zurich_Lat);
        var quarter = SkyPath.AltitudeAtHourAngle(SkyPath.HourAngleAt(0.25, arc, false), declination, Zurich_Lat);

        Assert.Equal(0.61, quarter / peak, 2);
        Assert.Equal(0.71, Math.Sin(Math.PI * 0.25), 2);

        var widest = 0.0;
        foreach (var sample in SkyPath.Samples(declination, Zurich_Lat, false, 40))
        {
            widest = Math.Max(widest, Math.Abs((sample.AltitudeDegrees / peak) - Math.Sin(Math.PI * sample.Progress)));
        }

        Assert.True(widest > 0.05, $"the drawn curve is within {widest:F3} of a semicircle everywhere");
    }

    [Fact]
    public void S_11_The_day_curve_starts_and_ends_on_the_horizon_and_peaks_at_midday()
    {
        const double declination = 15.6;
        var day = SkyPath.Samples(declination, Zurich_Lat, false, 48);

        Assert.Equal(0, day[0].AltitudeDegrees, AngleTolerance);
        Assert.Equal(0, day[^1].AltitudeDegrees, AngleTolerance);

        var peak = day[0];
        foreach (var sample in day)
        {
            if (sample.AltitudeDegrees > peak.AltitudeDegrees)
            {
                peak = sample;
            }
        }

        Assert.Equal(0.5, peak.Progress, 2);
        Assert.Equal(SkyPath.AltitudeAtHourAngle(0, declination, Zurich_Lat), peak.AltitudeDegrees, AngleTolerance);

        // ...and rises then falls, with no wobble in between.
        for (var i = 1; i < day.Count; i++)
        {
            if (day[i].Progress <= 0.5)
            {
                Assert.True(day[i].AltitudeDegrees >= day[i - 1].AltitudeDegrees - 1e-9, $"dipped at {day[i].Progress:F3}");
            }
            else
            {
                Assert.True(day[i].AltitudeDegrees <= day[i - 1].AltitudeDegrees + 1e-9, $"rose again at {day[i].Progress:F3}");
            }
        }
    }

    /// <summary>
    /// The night stretch carries on the same way round the sky — 0 just after setting, 0.5 at the
    /// nadir, 1 about to rise. Drawn left to right instead, the sun would rise where it had just
    /// set. [S-12]
    /// </summary>
    [Fact]
    public void S_12_The_night_curve_runs_the_other_way_and_is_deepest_at_the_nadir()
    {
        const double declination = 15.6;
        var night = SkyPath.Samples(declination, Zurich_Lat, true, 48);

        Assert.Equal(0, night[0].AltitudeDegrees, AngleTolerance);  // just set
        Assert.Equal(0, night[^1].AltitudeDegrees, AngleTolerance); // about to rise

        var nadir = night[0];
        foreach (var sample in night)
        {
            if (sample.AltitudeDegrees < nadir.AltitudeDegrees)
            {
                nadir = sample;
            }
        }

        Assert.Equal(0.5, nadir.Progress, 2);
        Assert.Equal(SkyPath.AltitudeAtHourAngle(180, declination, Zurich_Lat), nadir.AltitudeDegrees, AngleTolerance);
    }

    [Fact]
    public void S_12_Progress_maps_back_to_the_hour_angles_the_arc_is_built_from()
    {
        var arc = SkyPath.SemiDiurnalArc(15.6, Zurich_Lat);

        Assert.Equal(-arc, SkyPath.HourAngleAt(0, arc, false), 9);
        Assert.Equal(0, SkyPath.HourAngleAt(0.5, arc, false), 9);
        Assert.Equal(arc, SkyPath.HourAngleAt(1, arc, false), 9);

        Assert.Equal(arc, SkyPath.HourAngleAt(0, arc, true), 9);
        Assert.Equal(180, SkyPath.HourAngleAt(0.5, arc, true), 9);
        Assert.Equal(-arc, SkyPath.HourAngleAt(1, arc, true), 9);
    }

    [Fact]
    public void S_10_A_body_straight_overhead_is_up_and_mid_arc()
    {
        var position = SkyPath.PositionOf(new GroundPoint(Zurich_Lat, Zurich_Lon), Zurich_Lat, Zurich_Lon);

        Assert.True(position.Up);
        Assert.Equal(0.5, position.Progress, 6);
        Assert.Equal(90, position.AltitudeDegrees, 6);
    }

    [Fact]
    public void S_10_The_antipodal_body_is_at_the_nadir()
    {
        var position = SkyPath.PositionOf(new GroundPoint(-Zurich_Lat, Zurich_Lon - 180), Zurich_Lat, Zurich_Lon);

        Assert.False(position.Up);
        Assert.Equal(0.5, position.Progress, 6);
        Assert.Equal(-90, position.AltitudeDegrees, 6);
    }

    [Fact]
    public void S_10_East_is_the_rising_half_and_west_the_setting_half()
    {
        Assert.True(SkyPath.PositionOf(new GroundPoint(0, 45), 0, 0).Progress < 0.5);
        Assert.True(SkyPath.PositionOf(new GroundPoint(0, -45), 0, 0).Progress > 0.5);
    }

    [Fact]
    public void S_10_Progress_stays_in_range_everywhere_on_the_sphere()
    {
        for (var declination = -28.0; declination <= 28; declination += 4)
        {
            for (var longitude = -180.0; longitude < 180; longitude += 5)
            {
                foreach (var latitude in new[] { -85.0, -70, -33, 0, 47, 70, 85 })
                {
                    var position = SkyPath.PositionOf(new GroundPoint(declination, longitude), latitude, Zurich_Lon);

                    Assert.InRange(position.Progress, 0, 1);
                    Assert.True(double.IsFinite(position.AltitudeDegrees));
                }
            }
        }
    }

    /// <summary>
    /// The drawing and the clock have to tell the same story: if the widget draws the sun above the
    /// horizon, the sunrise equation must agree that it is up. [S-06] [S-10]
    /// </summary>
    [Theory]
    [InlineData(47.37, 8.54)]
    [InlineData(-33.87, 151.21)]
    [InlineData(-36.85, 174.76)]
    [InlineData(21.31, -157.86)]
    public void S_10_The_marker_agrees_with_sunrise_and_sunset_at_every_hour(double latitude, double longitude)
    {
        for (var hour = 0; hour < 24; hour++)
        {
            var now = Utc(2026, 8, 10, hour, 30);
            var times = SolarPosition.TimesFor(now, latitude, longitude);

            if (times.Polar != PolarState.None)
            {
                continue;
            }

            var inDaylight = now >= times.Sunrise!.Value && now <= times.Sunset!.Value;
            var drawn = SkyPath.PositionOf(SolarPosition.SubsolarPoint(now), latitude, longitude).Up;

            Assert.True(inDaylight == drawn, $"lat={latitude} hour={hour}: sunTimes said {inDaylight}, the path said {drawn}");
        }
    }

    [Fact]
    public void S_10_The_sun_sweeps_the_whole_arc_across_a_day()
    {
        var lowest = 1.0;
        var highest = 0.0;
        var start = Utc(2026, 8, 10);

        for (var minutes = 0; minutes < 24 * 60; minutes += 10)
        {
            var position = SkyPath.PositionOf(SolarPosition.SubsolarPoint(start.AddMinutes(minutes)), Zurich_Lat, Zurich_Lon);

            if (!position.Up)
            {
                continue;
            }

            lowest = Math.Min(lowest, position.Progress);
            highest = Math.Max(highest, position.Progress);
        }

        Assert.True(lowest < 0.05, $"the sun never started near sunrise: {lowest:F3}");
        Assert.True(highest > 0.95, $"the sun never reached sunset: {highest:F3}");
    }
}
