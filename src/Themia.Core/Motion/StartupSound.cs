using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.Core.Motion;

/// <summary>
/// [S-1008] The optional startup sound: a synthesised cue that matches the startup animation, one
/// plan per style, timed from the same constants the visuals use (the warp from
/// <see cref="Hyperspace"/>), so the sound cannot drift from the picture. No audio assets.
/// </summary>
public static class StartupSound
{
    /// <summary>The hacker churn's ladder: climbing, so it reads as "working" rather than random, and seedless.</summary>
    private static readonly double[] BlipHz = [440, 523, 587, 659, 784, 880, 988, 1175];

    /// <summary>
    /// The cue for <paramref name="style"/> at a per-widget length of <paramref name="widgetMs"/>.
    /// None is silent. The warp ignores the length (it only retimes the widgets' landing); the hacker
    /// churn and the simple swells follow it.
    /// <para>
    /// The original's voices summed past full scale and the browser clipped them; a plan that would
    /// clip is scaled down to full scale, and quieter plans keep their own level.
    /// </para>
    /// </summary>
    public static SoundPlan Plan(IntroAnimation style, double widgetMs)
    {
        var plan = style switch
        {
            IntroAnimation.None => SoundPlan.Empty,
            IntroAnimation.Hyperspace => Warp(),
            IntroAnimation.Hacker => Hacker(widgetMs),
            _ when Enum.IsDefined(style) => Swell(style, widgetMs),
            _ => SoundPlan.Empty,
        };

        return plan.PeakBound() > 1 ? plan.Normalised(1) : plan;
    }

    /// <summary>
    /// The warp: a sawtooth engine whine spooling up through the acceleration, a band-passed wind rush
    /// opening with it, a sub drone under the whole show, and a bass boom at the jump that decays
    /// through the deceleration while the widgets crash in.
    /// </summary>
    private static SoundPlan Warp()
    {
        const double Peak = 0.95;
        const double TopHz = 1500;
        const double BaseHz = 70;
        const double BoomHz = 58;
        var jump = Hyperspace.JumpMs;
        var decelEnd = jump + Hyperspace.DecelMs;
        var s = Hyperspace.StaticMs;
        var a = Hyperspace.AccelMs;

        return new SoundPlan(
        [
            new SoundVoice(VoiceShape.Sawtooth, s, jump + 120,
                [new(0, BaseHz), new(a, TopHz), new(jump - s, TopHz), new(jump - s + 120, BaseHz)],
                [new(0, 0), new(a, Peak * 0.7), new(jump - s, Peak), new(jump - s + 120, 0)]),

            new SoundVoice(VoiceShape.Noise, s, decelEnd,
                [new(0, 320), new(a, 2600), new(jump - s, 2600), new(decelEnd - s, 500)],
                [new(0, 0), new(a, 0.42), new(jump - s, 0.5), new(decelEnd - s, 0)]),

            new SoundVoice(VoiceShape.Sine, jump, decelEnd,
                [new(0, BoomHz), new(Hyperspace.DecelMs, BoomHz * 0.45)],
                [new(0, 0), new(40, 1), new(Hyperspace.DecelMs, 0)]),

            new SoundVoice(VoiceShape.Sine, s, decelEnd,
                [new(0, 46), new(decelEnd - s, 40)],
                [new(0, 0), new(a, 0.18), new(jump - s, 0.32), new(decelEnd - s, 0)]),
        ]);
    }

    /// <summary>A stream of square blips over the churn (more for a longer intro), then a two-note confirm.</summary>
    private static SoundPlan Hacker(double widgetMs)
    {
        var d = double.IsFinite(widgetMs) ? Math.Max(1, widgetMs) : IntroMotion.DefaultMs;
        const double StepMs = 110;
        var count = Math.Clamp((int)Math.Floor(d / StepMs), 5, 28);
        var voices = new List<SoundVoice>(count + 2);

        for (var i = 0; i < count; i++)
        {
            var at = Math.Round((double)i / count * (d - 80));
            voices.Add(new SoundVoice(VoiceShape.Square, at, at + 45,
                [new(0, BlipHz[i % BlipHz.Length])],
                [new(0, 0.16), new(38, 0.16), new(45, 0)]));
        }

        voices.Add(new SoundVoice(VoiceShape.Triangle, d, d + 160, [new(0, 784)], [new(0, 0), new(12, 0.3), new(160, 0)]));
        voices.Add(new SoundVoice(VoiceShape.Triangle, d + 90, d + 320, [new(0, 1175)], [new(0, 0), new(12, 0.34), new(230, 0)]));

        return new SoundPlan(voices);
    }

    /// <summary>The swell's base note per style: one shape, a colour of its own for each.</summary>
    public static double BaseHz(IntroAnimation style) => style switch
    {
        IntroAnimation.Fade => 392,
        IntroAnimation.Scale => 523,
        IntroAnimation.Slide => 440,
        IntroAnimation.Sides => 466,
        IntroAnimation.TopBottom => 494,
        IntroAnimation.Flip => 587,
        IntroAnimation.Blur => 330,
        _ => 440,
    };

    /// <summary>
    /// A soft two-partial power-on swell as long as the intro (clamped so it never drags), rising a
    /// fifth for the styles that move upward.
    /// </summary>
    private static SoundPlan Swell(IntroAnimation style, double widgetMs)
    {
        var baseHz = BaseHz(style);
        var len = Math.Clamp(double.IsFinite(widgetMs) ? widgetMs : IntroMotion.DefaultMs, 180, 1100);
        var rising = style is IntroAnimation.Slide or IntroAnimation.Scale;
        var endHz = rising ? baseHz * 1.5 : baseHz;

        return new SoundPlan(
        [
            new SoundVoice(VoiceShape.Sine, 0, len,
                [new(0, baseHz), new(len, endHz)],
                [new(0, 0), new(Math.Round(len * 0.35), 0.26), new(len, 0)]),
            new SoundVoice(VoiceShape.Triangle, 20, len,
                [new(0, baseHz * 2), new(len - 20, endHz * 2)],
                [new(0, 0), new(Math.Round(len * 0.3), 0.1), new(len - 20, 0)]),
        ]);
    }
}
