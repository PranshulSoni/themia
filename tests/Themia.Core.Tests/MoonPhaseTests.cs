using Themia.Core.Astronomy;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// Moon phase, illumination and the sublunar point. [S-04] [S-08]
/// <para>
/// The phase is a linear synodic model, so it drifts against the real moon by up to about half a
/// day; the published-value cases below state that tolerance rather than hiding it.
/// </para>
/// </summary>
public class MoonPhaseTests
{
    /// <summary>The 2000-01-06 18:14 UTC new moon the model is anchored to.</summary>
    private static readonly DateTimeOffset Anchor = new(2000, 1, 6, 18, 14, 0, TimeSpan.Zero);

    /// <summary>0.02 of a cycle is about 14 hours — the linear model's worst drift against the real moon.</summary>
    private const double PhaseTolerance = 0.02;

    private static DateTimeOffset Utc(int year, int month, int day, int hour = 0, int minute = 0)
        => new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    /// <summary>Distance between two phases the short way round the cycle.</summary>
    private static double PhaseDistance(double a, double b)
    {
        var delta = Math.Abs(a - b) % 1;
        return Math.Min(delta, 1 - delta);
    }

    [Fact]
    public void S_04_Reports_a_new_moon_at_the_anchor()
    {
        var moon = MoonPhase.At(Anchor);

        Assert.True(moon.Phase < 0.01 || moon.Phase > 0.99, $"phase {moon.Phase:F4}");
        Assert.True(moon.Illumination < 0.01, $"illumination {moon.Illumination:F4}");
        Assert.Equal(MoonPhaseName.NewMoon, moon.Name);
    }

    [Fact]
    public void S_04_Reports_a_full_moon_half_a_synodic_month_later()
    {
        var moon = MoonPhase.At(Anchor.AddDays(14.765));

        Assert.InRange(moon.Phase, 0.48, 0.52);
        Assert.True(moon.Illumination > 0.98, $"illumination {moon.Illumination:F4}");
        Assert.Equal(MoonPhaseName.FullMoon, moon.Name);
    }

    [Fact]
    public void S_04_Illumination_stays_inside_zero_to_one_across_a_cycle()
    {
        for (var hours = 0; hours < 24 * 30; hours++)
        {
            var moon = MoonPhase.At(Anchor.AddHours(hours));

            Assert.InRange(moon.Illumination, 0, 1);
            Assert.InRange(moon.Phase, 0, 1);
        }
    }

    /// <summary>
    /// The known-value check for the phase: the published new and full moons of August 2026
    /// (timeanddate.com — the new moon of 2026-08-12 is the total solar eclipse over Europe, and the
    /// full moon of 2026-08-28 is a partial lunar eclipse).
    /// </summary>
    [Theory]
    [InlineData(2026, 8, 12, 17, 36, 0.0, MoonPhaseName.NewMoon)]
    [InlineData(2026, 8, 28, 4, 18, 0.5, MoonPhaseName.FullMoon)]
    public void S_04_Matches_the_published_syzygies_of_August_2026(
        int year, int month, int day, int hour, int minute, double expectedPhase, MoonPhaseName expectedName)
    {
        var moon = MoonPhase.At(Utc(year, month, day, hour, minute));

        Assert.True(
            PhaseDistance(moon.Phase, expectedPhase) <= PhaseTolerance,
            $"phase {moon.Phase:F4} is more than {PhaseTolerance} from the published {expectedPhase}");

        Assert.Equal(expectedName, moon.Name);
    }

    [Fact]
    public void S_04_The_published_new_moon_is_dark_and_the_published_full_moon_is_lit()
    {
        Assert.True(MoonPhase.At(Utc(2026, 8, 12, 17, 36)).Illumination < 0.01);
        Assert.True(MoonPhase.At(Utc(2026, 8, 28, 4, 18)).Illumination > 0.99);
    }

    /// <summary>The eight buckets, at their centres and at the edges between them. [S-04]</summary>
    [Theory]
    [InlineData(0.0, MoonPhaseName.NewMoon)]
    [InlineData(0.125, MoonPhaseName.WaxingCrescent)]
    [InlineData(0.25, MoonPhaseName.FirstQuarter)]
    [InlineData(0.375, MoonPhaseName.WaxingGibbous)]
    [InlineData(0.5, MoonPhaseName.FullMoon)]
    [InlineData(0.625, MoonPhaseName.WaningGibbous)]
    [InlineData(0.75, MoonPhaseName.LastQuarter)]
    [InlineData(0.875, MoonPhaseName.WaningCrescent)]
    [InlineData(0.99, MoonPhaseName.NewMoon)]
    public void S_04_The_phase_falls_into_one_of_eight_buckets(double phase, MoonPhaseName expected)
    {
        // Walk the model forward to the instant that has this phase, rather than asserting on a
        // constructor: the bucket is a property of At(), not of the record.
        var moment = Anchor.AddDays(phase * 29.53058867);
        Assert.Equal(expected, MoonPhase.At(moment).Name);
    }

    [Fact]
    public void S_04_Every_bucket_has_a_label()
    {
        foreach (var name in Enum.GetValues<MoonPhaseName>())
        {
            Assert.False(string.IsNullOrWhiteSpace(MoonPhase.Label(name)));
        }
    }

    [Fact]
    public void S_08_The_sublunar_point_stays_within_the_lunar_declination_limit()
    {
        var start = Utc(2026, 1, 1);

        for (var hours = 0; hours < 24 * 400; hours += 6)
        {
            var point = MoonPhase.SublunarPoint(start.AddHours(hours));

            // Obliquity plus the 5.1 degree inclination of the lunar orbit.
            Assert.InRange(point.Latitude, -28.7, 28.7);
            Assert.InRange(point.Longitude, -180, 180);
        }
    }

    [Fact]
    public void S_08_The_sublunar_point_returns_to_the_same_meridian_a_lunar_day_later()
    {
        var start = Utc(2026, 8, 10);
        var later = start.AddMinutes((24 * 60) + 50);

        var drift = Math.Abs(SolarPositionTests.LongitudeDelta(
            MoonPhase.SublunarPoint(start).Longitude,
            MoonPhase.SublunarPoint(later).Longitude));

        Assert.True(drift < 8, $"the lunar day drifted {drift:F2} degrees"); // the lunar day itself swings by ~15 min
    }

    /// <summary>
    /// The regression [S-08] exists for: computing the moon from the rounded day index the sunrise
    /// equation uses would leave the marker in one place for twenty-four hours. The real ground
    /// track moves about 14.5 degrees of longitude an hour, westward.
    /// </summary>
    [Fact]
    public void S_08_The_sublunar_point_moves_within_the_hour_rather_than_once_a_day()
    {
        var start = Utc(2026, 8, 10, 6);

        var hourly = SolarPositionTests.LongitudeDelta(
            MoonPhase.SublunarPoint(start).Longitude,
            MoonPhase.SublunarPoint(start.AddHours(1)).Longitude);

        Assert.InRange(hourly, -15.5, -13.5);

        var tenMinutes = Math.Abs(SolarPositionTests.LongitudeDelta(
            MoonPhase.SublunarPoint(start).Longitude,
            MoonPhase.SublunarPoint(start.AddMinutes(10)).Longitude));

        Assert.True(tenMinutes > 1.5, $"the moon barely moved in ten minutes: {tenMinutes:F3} degrees");
    }

    /// <summary>
    /// The independent check on the lunar series: at an eclipse the moon is, by definition, in front
    /// of the sun or opposite it. The truncated series drops the evection term, so a degree or two
    /// of slack is expected; six is still two orders of magnitude tighter than a wrong ground track.
    /// [S-08]
    /// </summary>
    [Fact]
    public void S_08_At_the_2026_solar_eclipse_the_moon_is_where_the_sun_is()
    {
        var greatest = Utc(2026, 8, 12, 17, 46); // total solar eclipse, greatest eclipse
        var separation = Separation(MoonPhase.SublunarPoint(greatest), SolarPosition.SubsolarPoint(greatest));

        Assert.True(separation < 6, $"the eclipsing moon was {separation:F2} degrees from the sun");
    }

    [Fact]
    public void S_08_At_the_2026_lunar_eclipse_the_moon_is_opposite_the_sun()
    {
        var greatest = Utc(2026, 8, 28, 4, 14); // partial lunar eclipse, greatest eclipse
        var sun = SolarPosition.SubsolarPoint(greatest);
        var antisolar = new GroundPoint(-sun.Latitude, sun.Longitude > 0 ? sun.Longitude - 180 : sun.Longitude + 180);

        var separation = Separation(MoonPhase.SublunarPoint(greatest), antisolar);

        Assert.True(separation < 6, $"the eclipsed moon was {separation:F2} degrees from the antisolar point");
    }

    /// <summary>Angular distance between two ground points, degrees.</summary>
    private static double Separation(GroundPoint a, GroundPoint b)
    {
        const double deg = Math.PI / 180;

        var cosine =
            (Math.Sin(a.Latitude * deg) * Math.Sin(b.Latitude * deg)) +
            (Math.Cos(a.Latitude * deg) * Math.Cos(b.Latitude * deg) * Math.Cos((a.Longitude - b.Longitude) * deg));

        return Math.Acos(Math.Clamp(cosine, -1, 1)) / deg;
    }
}
