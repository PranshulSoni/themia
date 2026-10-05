using Themia.Core.Geometry;

namespace Themia.Core.Motion;

/// <summary>One frame of the hyperspace screen shake: a translation in DIPs and a roll in degrees.</summary>
public readonly record struct ShakeKey(double At, double X, double Y, double Degrees);

/// <summary>One sample of the warp: its level (0 dots .. 1 full streaks) and how far it has carried the stars.</summary>
public readonly record struct WarpSample(double Ms, double Level, double Travel);

/// <summary>
/// One star of the field, fixed at birth. It never needs a per-frame update: where it is follows in
/// closed form from the warp's travel (<see cref="Hyperspace.Distance"/>), which is what lets the
/// compositor move it with an expression instead of a managed timer redrawing a canvas.
/// </summary>
/// <param name="Angle">Its first ray from the core, radians.</param>
/// <param name="Log0">ln(distance + core offset) at the start.</param>
/// <param name="Rate">Growth of that log per unit of travel: the star's own speed over the field's radius.</param>
/// <param name="Speed">The original's per-star speed, 0.4..1.3; also scales its streak.</param>
/// <param name="Phase">Twinkle offset, so static stars do not pulse in step.</param>
/// <param name="Hue">Its hue in degrees, 200..240 (ice blue).</param>
public readonly record struct Star(double Angle, double Log0, double Rate, double Speed, double Phase, double Hue);

/// <summary>
/// [S-1005] The hyperspace show: a violent, slow-burn-then-snap warp. A long static hold, a tease
/// where the streaks barely creep, a fast smoothstep snap to full warp, a cruise, the jump (where the
/// widgets warp in) and a deceleration back to dots while the black veil lifts.
/// <para>
/// Pure timing and geometry, the original's <c>hyperspace.ts</c>; the surface turns it into
/// compositor animations, scaled as one piece to the length the user set ([S-1760]).
/// </para>
/// </summary>
public static class Hyperspace
{
    public const double StaticMs = 900;
    public const double AccelMs = 1500;
    public const double SnapMs = 280;
    public const double TeasePeak = 0.06;
    public const double CruiseMs = 900;
    public const double DecelMs = 900;

    /// <summary>static + accel + cruise: where the widgets warp in.</summary>
    public const double JumpMs = StaticMs + AccelMs + CruiseMs;

    /// <summary>accel + cruise + decel: the one continuous shake, after the static hold.</summary>
    public const double ShakeMs = AccelMs + CruiseMs + DecelMs;

    /// <summary>The overlay's fade-out after the last widget has landed.</summary>
    public const double FadeMs = 300;

    public const double AccelPow = 1.6;
    public const int StarCount = 100;

    /// <summary>Star speed at full warp, DIPs per 60-Hz frame (times the star's speed and depth).</summary>
    public const double SpeedFactor = 260;

    /// <summary>Streak length at full warp, DIPs (times the star's speed and depth).</summary>
    public const double TrailFactor = 1500;

    /// <summary>The warp core's height as a fraction of the screen: toward the bottom.</summary>
    public const double CentreYFraction = 0.68;

    /// <summary>
    /// The core offset, as a fraction of the field radius: a star's speed grows with
    /// <c>offset + depth</c>, so its log distance from <c>-offset</c> grows linearly with the travel.
    /// </summary>
    public const double CoreOffset = 0.15;

    /// <summary>A star leaves the field (and is reborn at the core on its next ray) past this fraction of the radius.</summary>
    public const double EdgeFraction = 1.05;

    /// <summary>The turn between one life's ray and the next: the golden angle, so reborn stars never line up.</summary>
    public const double RebirthTurn = 2.399963229728653;

    /// <summary>The original's frame, which its per-frame speeds are in.</summary>
    public const double FrameMs = 1000.0 / 60;

    /// <summary>
    /// [S-1005] The warp level at <paramref name="ms"/> into the show: 0 static dots .. 1 full streaks.
    /// Static, then the tease (capped at <see cref="TeasePeak"/>), the smoothstep snap, cruise at 1,
    /// and a sharp cubic drop to 0 over the deceleration.
    /// </summary>
    public static double Warp(double ms)
    {
        if (!double.IsFinite(ms) || ms <= StaticMs)
        {
            return 0;
        }

        var accelEnd = StaticMs + AccelMs;
        if (ms < accelEnd)
        {
            var t = ms - StaticMs;
            var teaseMs = AccelMs - SnapMs;
            if (t < teaseMs)
            {
                return Math.Pow(t / teaseMs, AccelPow) * TeasePeak;
            }

            var f = (t - teaseMs) / SnapMs;
            var s = f * f * (3 - (2 * f));
            return TeasePeak + ((1 - TeasePeak) * s);
        }

        if (ms <= JumpMs)
        {
            return 1;
        }

        var d = (ms - JumpMs) / DecelMs;
        return d >= 1 ? 0 : Math.Pow(1 - d, 3);
    }

    /// <summary>
    /// The warp level and the travel it has accumulated — the integral of the level, in full-warp
    /// frames — sampled every <paramref name="stepMs"/> from 0 to <paramref name="untilMs"/>. The
    /// compositor interpolates between samples; travel is smooth, so linear steps are exact enough.
    /// </summary>
    public static IReadOnlyList<WarpSample> Samples(double stepMs, double untilMs)
    {
        var step = double.IsFinite(stepMs) && stepMs > 0 ? stepMs : 20;
        var until = double.IsFinite(untilMs) && untilMs > 0 ? untilMs : 0;
        var samples = new List<WarpSample> { new(0, 0, 0) };
        const double Fine = 2;
        var travel = 0.0;
        var at = 0.0;

        while (at < until)
        {
            var next = Math.Min(until, at + step);
            for (var t = at; t < next; t += Fine)
            {
                var dt = Math.Min(Fine, next - t);
                travel += (Warp(t) + Warp(t + dt)) / 2 * (dt / FrameMs);
            }

            at = next;
            samples.Add(new WarpSample(at, Warp(at), travel));
        }

        return samples;
    }

    /// <summary>The field's radius: from the core to the farthest corner, so streaks always reach every edge.</summary>
    public static double Radius(double width, double height)
    {
        var cx = width / 2;
        var cy = height * CentreYFraction;
        return Math.Max(1, Math.Sqrt(Square(Math.Max(cx, width - cx)) + Square(Math.Max(cy, height - cy))));
    }

    /// <summary>
    /// The field, deterministically: the same seed and radius give the same stars, so a render and a
    /// test see what the user sees. Spread as the original's <c>pow(random, 0.6) · 0.9R</c>.
    /// </summary>
    public static Star[] Stars(int count, int seed, double radius)
    {
        var random = new Random(seed);
        var stars = new Star[Math.Max(0, count)];
        var r = Math.Max(1, radius);

        for (var i = 0; i < stars.Length; i++)
        {
            var distance = Math.Pow(random.NextDouble(), 0.6) * r * 0.9;
            var speed = 0.4 + (random.NextDouble() * 0.9);
            stars[i] = new Star(
                Angle: random.NextDouble() * Math.PI * 2,
                Log0: Math.Log(distance + (CoreOffset * r)),
                Rate: speed * SpeedFactor / r,
                Speed: speed,
                Phase: random.NextDouble() * Math.PI * 2,
                Hue: 200 + (random.NextDouble() * 40));
        }

        return stars;
    }

    /// <summary>The log-distance window a star lives in, from the core to just past the edge.</summary>
    public static (double Low, double Span) LogWindow(double radius)
    {
        var r = Math.Max(1, radius);
        var low = Math.Log(CoreOffset * r);
        return (low, Math.Log((EdgeFraction + CoreOffset) * r) - low);
    }

    /// <summary>How many times the star has crossed the field after <paramref name="travel"/>.</summary>
    public static double Lives(Star star, double travel, double radius)
    {
        var (low, span) = LogWindow(radius);
        return Math.Floor((star.Log0 - low + (star.Rate * travel)) / span);
    }

    /// <summary>
    /// A star's distance from the core after <paramref name="travel"/>, in DIPs: exponential growth (speed
    /// rises with depth, as the original's per-frame step did), wrapped back to the core once past the edge.
    /// </summary>
    public static double Distance(Star star, double travel, double radius)
    {
        var (low, span) = LogWindow(radius);
        var x = star.Log0 - low + (star.Rate * travel);
        var wrapped = x - (Math.Floor(x / span) * span);
        return Math.Exp(low + wrapped) - (CoreOffset * Math.Max(1, radius));
    }

    /// <summary>The ray a star is on after <paramref name="travel"/>: its first ray, turned once per rebirth.</summary>
    public static double Angle(Star star, double travel, double radius)
        => star.Angle + (Lives(star, travel, radius) * RebirthTurn);

    /// <summary>A streak's length in DIPs at warp <paramref name="level"/>; a static star stays a dot.</summary>
    public static double Trail(Star star, double level, double depth)
        => Math.Max(0.7, star.Speed * level * TrailFactor * (0.1 + depth));

    /// <summary>
    /// The streak actually drawn from a star <paramref name="distance"/> out: its trail, cut at the core
    /// (a streak never reaches through to the other side), and never thinner than a dot.
    /// </summary>
    public static double Streak(Star star, double level, double distance, double radius)
        => Math.Max(0.7, Math.Min(Trail(star, level, distance / Math.Max(1, radius)), distance));

    /// <summary>A streak's thickness in DIPs.</summary>
    public static double Thickness(double level, double depth) => (0.5 + (depth * 1.1)) * (1 + (level * 0.6));

    /// <summary>A star's alpha: deeper and faster is brighter; the twinkle reads strongest while static.</summary>
    public static double Alpha(Star star, double level, double depth, double ms)
    {
        var twinkle = 0.75 + (0.25 * Math.Sin((ms * 0.004 * star.Speed) + star.Phase));
        return Math.Clamp((0.30 + (depth * 0.55)) * (0.5 + (level * 0.5)) * twinkle, 0, 1);
    }

    /// <summary>
    /// A star's colour, fixed at birth: its hue at 80% saturation, lighter the deeper it starts. The
    /// warp's own palette, deliberately not the app accent, like the original's.
    /// </summary>
    public static (byte R, byte G, byte B) Colour(Star star, double radius)
    {
        var depth = Math.Clamp((Math.Exp(star.Log0) - (CoreOffset * Math.Max(1, radius))) / Math.Max(1, radius), 0, 1);
        return Hsl(star.Hue, 0.8, (78 + (depth * 16)) / 100);
    }

    /// <summary>
    /// [S-1006] Where a widget warps in from: the offset from its centre to the core, in DIPs, so it
    /// flies out of the lowered core rather than from the front.
    /// </summary>
    public static (double X, double Y) FromOffset(FracRect rect, double width, double height)
    {
        var cx = (rect.X + (rect.W / 2)) * width;
        var cy = (rect.Y + (rect.H / 2)) * height;
        return (Math.Round((width / 2) - cx), Math.Round((height * CentreYFraction) - cy));
    }

    /// <summary>
    /// [S-1760] The overlay's natural timeline, end to end: the jump, the deceleration and the fade.
    /// A show of any length plays this timeline scaled to fit (<see cref="IntroSchedule.TimeScale"/>).
    /// </summary>
    public const double NaturalMs = JumpMs + DecelMs + FadeMs;

    /// <summary>
    /// [S-1006] The screen shake the widget layer takes over <see cref="ShakeMs"/>, after the static
    /// hold: near-still through the tease, a bang at the snap, a judder through cruise, a spike at the
    /// jump, decaying landing hits, then still. The stars never shake. Identity at both ends.
    /// </summary>
    public static IReadOnlyList<ShakeKey> Shake { get; } =
    [
        new(0.00, 0, 0, 0),
        new(0.12, 0.2, -0.1, 0),
        new(0.22, -0.3, 0.2, 0),
        new(0.30, 0.4, -0.3, 0.02),
        new(0.36, -0.6, 0.4, -0.03),
        new(0.41, 0.8, -0.6, 0.04),
        new(0.43, -10, 7, -0.5),
        new(0.45, 8, -6, 0.4),
        new(0.48, -6, 4.5, -0.3),
        new(0.51, 5.5, -4, 0.28),
        new(0.54, -5, 3.5, -0.26),
        new(0.58, 5, -3.5, 0.24),
        new(0.61, -5, 3.5, -0.24),
        new(0.64, 5, -3.5, 0.24),
        new(0.67, -5, 3.5, -0.24),
        new(0.70, 5, -3.5, 0.24),
        new(0.73, -14, 9, -0.55),
        new(0.75, 11, -7, 0.45),
        new(0.78, -9, 6, -0.4),
        new(0.81, 8, -5, 0.35),
        new(0.84, -7, 4.5, -0.3),
        new(0.87, 6, -4, 0.25),
        new(0.90, -4, 2.5, -0.18),
        new(0.93, 2.5, -1.5, 0.1),
        new(0.96, -1.2, 0.8, -0.05),
        new(1.00, 0, 0, 0),
    ];

    private static double Square(double x) => x * x;

    private static (byte R, byte G, byte B) Hsl(double hue, double saturation, double lightness)
    {
        var c = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
        var h = (((hue % 360) + 360) % 360) / 60;
        var x = c * (1 - Math.Abs((h % 2) - 1));
        var (r, g, b) = h switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        var m = lightness - (c / 2);
        return (ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    private static byte ToByte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
}
