namespace Themia.Core.Astronomy;

/// <summary>
/// The eight phase buckets the widget names. [S-04]
/// <para>
/// A closed set, so an enum rather than a string: the view maps it to a label, and a value outside
/// the set would be a bug rather than a new phase of the moon.
/// </para>
/// </summary>
public enum MoonPhaseName
{
    NewMoon = 0,
    WaxingCrescent = 1,
    FirstQuarter = 2,
    WaxingGibbous = 3,
    FullMoon = 4,
    WaningGibbous = 5,
    LastQuarter = 6,
    WaningCrescent = 7,
}

/// <summary>What the moon looks like at one instant.</summary>
/// <param name="Phase">0..1 through the lunar cycle: 0 new, 0.5 full.</param>
/// <param name="Illumination">0..1 fraction of the disc lit.</param>
/// <param name="Name">Which of the eight buckets the phase falls in.</param>
public readonly record struct MoonInfo(double Phase, double Illumination, MoonPhaseName Name);

/// <summary>
/// Moon phase, illumination and where the moon is, computed offline.
/// <para>
/// The phase comes from the synodic month counted off a known new moon: a linear model, good to
/// well under a day, which is all a phase glyph and a percentage can show. The sublunar point is a
/// truncated lunar series instead, because that one has to move between ticks.
/// </para>
/// </summary>
public static class MoonPhase
{
    /// <summary>Mean length of a lunation, days.</summary>
    private const double SynodicMonth = 29.53058867;

    /// <summary>The new moon of 2000-01-06 18:14 UTC, as a Julian day. The anchor of the model.</summary>
    private const double KnownNewMoon = 2451550.1;

    /// <summary>Phase, illumination and name at <paramref name="moment"/>. [S-04]</summary>
    public static MoonInfo At(DateTimeOffset moment)
    {
        var phase = (AstroMath.ToJulian(moment) - KnownNewMoon) / SynodicMonth % 1.0;

        if (phase < 0)
        {
            phase += 1;
        }

        var illumination = (1 - Math.Cos(2 * Math.PI * phase)) / 2;
        var bucket = (int)Math.Round(phase * 8, MidpointRounding.AwayFromZero) % 8;

        return new MoonInfo(phase, illumination, (MoonPhaseName)bucket);
    }

    /// <summary>
    /// The point the moon is directly over, from its instantaneous right ascension and declination
    /// against sidereal time. [S-08]
    /// <para>
    /// Meeus's truncated lunar series: good to a fraction of a degree, which is finer than the disc
    /// the widget draws. Crucially it is a function of the instant, not of a rounded day index —
    /// the moon has to be seen to move, and the day index used for sunrise would freeze it for
    /// twenty-four hours at a time.
    /// </para>
    /// </summary>
    public static GroundPoint SublunarPoint(DateTimeOffset moment)
    {
        var days = AstroMath.DaysSinceJ2000(moment);
        var obliquity = AstroMath.ObliquityDegrees * AstroMath.Deg;

        var meanLongitude = (218.316 + (13.176396 * days)) * AstroMath.Deg;
        var meanAnomaly = (134.963 + (13.064993 * days)) * AstroMath.Deg;
        var argumentOfLatitude = (93.272 + (13.229350 * days)) * AstroMath.Deg;

        var eclipticLongitude = meanLongitude + (6.289 * AstroMath.Deg * Math.Sin(meanAnomaly));
        var eclipticLatitude = 5.128 * AstroMath.Deg * Math.Sin(argumentOfLatitude);

        var rightAscension = Math.Atan2(
            (Math.Sin(eclipticLongitude) * Math.Cos(obliquity)) - (Math.Tan(eclipticLatitude) * Math.Sin(obliquity)),
            Math.Cos(eclipticLongitude)) / AstroMath.Deg;

        var declination = Math.Asin(
            (Math.Sin(eclipticLatitude) * Math.Cos(obliquity)) +
            (Math.Cos(eclipticLatitude) * Math.Sin(obliquity) * Math.Sin(eclipticLongitude))) / AstroMath.Deg;

        return AstroMath.GroundPointOf(rightAscension, declination, days);
    }

    /// <summary>The display label for a phase bucket. Written out rather than derived from the enum name.</summary>
    public static string Label(MoonPhaseName name) => name switch
    {
        MoonPhaseName.NewMoon => "New Moon",
        MoonPhaseName.WaxingCrescent => "Waxing Crescent",
        MoonPhaseName.FirstQuarter => "First Quarter",
        MoonPhaseName.WaxingGibbous => "Waxing Gibbous",
        MoonPhaseName.FullMoon => "Full Moon",
        MoonPhaseName.WaningGibbous => "Waning Gibbous",
        MoonPhaseName.LastQuarter => "Last Quarter",
        MoonPhaseName.WaningCrescent => "Waning Crescent",
        _ => "Moon",
    };
}
