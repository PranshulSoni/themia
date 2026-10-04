using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Widgets;
using Themia.Core.Model;
using Themia.Core.Motion;

namespace Themia.App.Shell.Config;

/// <summary>
/// [S-1014] The Motion section of the Appearance page: the startup, unlock and view-change intros,
/// each with the same controls — a style, a length and a ▶ replay — and the startup sound under
/// Startup. Any change replays that moment on the desktop, so the user sees what they picked.
/// Deliberately not part of a design preset: motion is the user's accessibility choice.
/// </summary>
internal static partial class ConfigPages
{
    /// <summary>The ten styles, in the original's order, for all three moments.</summary>
    private static readonly (IntroAnimation Value, string Label)[] IntroStyles =
    [
        (IntroAnimation.None, "None"),
        (IntroAnimation.Fade, "Fade"),
        (IntroAnimation.Scale, "Scale"),
        (IntroAnimation.Slide, "Slide"),
        (IntroAnimation.Sides, "Sides"),
        (IntroAnimation.TopBottom, "Top and bottom"),
        (IntroAnimation.Flip, "Flip"),
        (IntroAnimation.Blur, "Blur"),
        (IntroAnimation.Hyperspace, "Hyperspace"),
        (IntroAnimation.Hacker, "Hacker"),
    ];

    private static IEnumerable<FrameworkElement> MotionRows(AppHost host)
    {
        yield return FluentLayout.Header("Motion");

        yield return MotionMoment(host, IntroMode.Startup, Glyphs.Startup, "Startup animation",
            "Plays when Themia starts", withSound: true);

        yield return MotionMoment(host, IntroMode.Unlock, Glyphs.Lock, "Unlock animation",
            "Plays when you unlock the PC. None turns it off", withSound: false);

        yield return MotionMoment(host, IntroMode.Transition, Glyphs.View, "View change animation",
            "Plays when a display switches views", withSound: false);

        // [S-1010] Said, not hidden: with Windows' animation effects off nothing here plays.
        if (!Motion.SystemMotion.AnimationsEnabled)
        {
            yield return Note("Animation effects are off in Windows (Accessibility › Visual effects), so these animations and the startup sound do not play.");
        }
    }

    private static Border MotionMoment(AppHost host, IntroMode mode, string glyph, string title, string description, bool withSound)
    {
        var design = host.State.Design;

        void Set(Action<DesignState> change)
        {
            host.Mutate(s => change(s.Design));
            host.ReplayIntro(mode);
        }

        var style = FluentLayout.Choice(IntroStyles, IntroMotion.StyleFor(design, mode), value => Set(d =>
        {
            switch (mode)
            {
                case IntroMode.Transition: d.TransitionAnimation = value; break;
                case IntroMode.Unlock: d.UnlockAnimation = value; break;
                default: d.IntroAnimation = value; break;
            }
        }));

        var replay = FluentLayout.IconCommand(Glyphs.Play, "Play it now");
        replay.Click += (_, _) => host.ReplayIntro(mode);

        var length = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space2,
            Children =
            {
                FluentLayout.Slider(IntroMotion.MsFor(design, mode), IntroMotion.MinMs, IntroMotion.MaxMs, 100, " ms", value => Set(d =>
                {
                    switch (mode)
                    {
                        case IntroMode.Transition: d.TransitionAnimationMs = value; break;
                        case IntroMode.Unlock: d.UnlockAnimationMs = value; break;
                        default: d.IntroAnimationMs = value; break;
                    }
                })),
                replay,
            },
        };

        var rows = new List<Border>
        {
            FluentLayout.Row(Glyphs.Timer, "Animation length", "How long each widget takes to arrive", length),
        };

        if (withSound)
        {
            rows.Add(FluentLayout.Row(Glyphs.Sound, "Startup sound", "A short sound that matches the animation", FluentLayout.Toggle(design.StartupSound, on =>
            {
                host.Mutate(s => s.Design.StartupSound = on);

                // Turning it on plays it, with the animation it belongs to.
                if (on)
                {
                    host.ReplayIntro(IntroMode.Startup);
                }
            })));
        }

        return FluentLayout.Expander(glyph, title, description, style, rows);
    }
}
