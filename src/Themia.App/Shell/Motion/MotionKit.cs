using System.Numerics;
using Microsoft.UI.Composition;
using Themia.Core.Motion;

namespace Themia.App.Shell.Motion;

/// <summary>
/// Turns Core's keyframe data into compositor animations. Every intro runs on the compositor
/// thread — the UI thread only starts it — so the motion costs no managed timer, no layout pass and
/// no redraw of the widget, and a busy UI thread cannot make it stutter ([S-1015]).
/// </summary>
internal sealed class MotionKit
{
    private readonly Compositor _compositor;
    private readonly CompositionEasingFunction _linear;
    private readonly CompositionEasingFunction _settle;
    private readonly CompositionEasingFunction _crash;

    public MotionKit(Compositor compositor)
    {
        _compositor = compositor;
        _linear = compositor.CreateLinearEasingFunction();
        _settle = Bezier(compositor, MotionEase.Settle);
        _crash = Bezier(compositor, MotionEase.Crash);
    }

    public Compositor Compositor => _compositor;

    public CompositionEasingFunction Ease(MotionEase ease) => ease switch
    {
        MotionEase.Settle => _settle,
        MotionEase.Crash => _crash,
        _ => _linear,
    };

    private static CompositionEasingFunction Bezier(Compositor compositor, MotionEase ease)
    {
        var (x1, y1, x2, y2) = IntroTracks.Bezier(ease) ?? (0, 0, 1, 1);
        return compositor.CreateCubicBezierEasingFunction(new Vector2((float)x1, (float)y1), new Vector2((float)x2, (float)y2));
    }

    /// <summary>
    /// A scalar track. The first key's value is held through the delay, so a widget waiting for its
    /// turn in the cascade is already in its start pose rather than flashing in at rest first.
    /// </summary>
    public ScalarKeyFrameAnimation Scalar(IReadOnlyList<MotionKey> keys, double durationMs, double delayMs)
    {
        var animation = _compositor.CreateScalarKeyFrameAnimation();
        foreach (var key in keys)
        {
            animation.InsertKeyFrame((float)key.At, (float)key.Value, Ease(key.Ease));
        }

        return Timed(animation, durationMs, delayMs);
    }

    /// <summary>A uniform scale track, as the compositor's three-axis scale.</summary>
    public Vector3KeyFrameAnimation Scale(IReadOnlyList<MotionKey> keys, double durationMs, double delayMs)
    {
        var animation = _compositor.CreateVector3KeyFrameAnimation();
        foreach (var key in keys)
        {
            animation.InsertKeyFrame((float)key.At, new Vector3((float)key.Value, (float)key.Value, 1), Ease(key.Ease));
        }

        return Timed(animation, durationMs, delayMs);
    }

    /// <summary>The translation track: each key's share of the widget's own start offset plus its fixed nudge.</summary>
    public Vector3KeyFrameAnimation Move(IReadOnlyList<MoveKey> keys, (double X, double Y) start, double durationMs, double delayMs)
    {
        var animation = _compositor.CreateVector3KeyFrameAnimation();
        foreach (var key in keys)
        {
            var at = new Vector3((float)((key.From * start.X) + key.X), (float)((key.From * start.Y) + key.Y), 0);
            animation.InsertKeyFrame((float)key.At, at, Ease(key.Ease));
        }

        return Timed(animation, durationMs, delayMs);
    }

    /// <summary>A linear scalar ramp through sampled values, for the warp's level and travel.</summary>
    public ScalarKeyFrameAnimation Sampled(IReadOnlyList<(double Ms, double Value)> samples, double totalMs)
    {
        var animation = _compositor.CreateScalarKeyFrameAnimation();
        var total = Math.Max(1, totalMs);
        foreach (var (ms, value) in samples)
        {
            animation.InsertKeyFrame((float)Math.Clamp(ms / total, 0, 1), (float)value, _linear);
        }

        return Timed(animation, total, 0);
    }

    /// <summary>A one-way hold-then-fade on opacity: 1 until <paramref name="holdMs"/>, 0 at <paramref name="holdMs"/> + <paramref name="fadeMs"/>.</summary>
    public ScalarKeyFrameAnimation FadeOut(double holdMs, double fadeMs)
    {
        var total = Math.Max(1, holdMs + fadeMs);
        var animation = _compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, 1, _linear);
        animation.InsertKeyFrame((float)(Math.Max(0, holdMs) / total), 1, _linear);
        animation.InsertKeyFrame(1, 0, _settle);
        return Timed(animation, total, 0);
    }

    /// <summary>An endless linear loop of a scalar from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public ScalarKeyFrameAnimation Loop(double from, double to, double periodMs)
    {
        var animation = _compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, (float)from, _linear);
        animation.InsertKeyFrame(1, (float)to, _linear);
        animation.Duration = TimeSpan.FromMilliseconds(Math.Max(1, periodMs));
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        return animation;
    }

    /// <summary>An endless loop through 2-D points (a reticle tracking, a scan line), linear between them.</summary>
    public Vector3KeyFrameAnimation Path(IReadOnlyList<Vector2> points, double periodMs, bool linear = false)
    {
        var animation = _compositor.CreateVector3KeyFrameAnimation();
        for (var i = 0; i < points.Count; i++)
        {
            animation.InsertKeyFrame(points.Count == 1 ? 0 : (float)i / (points.Count - 1), new Vector3(points[i], 0), linear ? _linear : _settle);
        }

        animation.Duration = TimeSpan.FromMilliseconds(Math.Max(1, periodMs));
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        return animation;
    }

    private static T Timed<T>(T animation, double durationMs, double delayMs)
        where T : KeyFrameAnimation
    {
        animation.Duration = TimeSpan.FromMilliseconds(Math.Max(1, durationMs));
        animation.DelayTime = TimeSpan.FromMilliseconds(Math.Max(0, delayMs));
        animation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        animation.IterationBehavior = AnimationIterationBehavior.Count;
        animation.IterationCount = 1;
        return animation;
    }
}
