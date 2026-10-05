using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Themia.Core.Diagnostics;
using Themia.Core.Geometry;
using Themia.Core.Model;
using Themia.Core.Motion;

namespace Themia.App.Shell.Motion;

/// <summary>One widget taking part in a show: its card and its logical geometry.</summary>
internal readonly record struct MotionActor(WidgetCard Card, FracRect Rect);

/// <summary>
/// Everything a show needs from its surface: the cards in cascade order, the layer they sit on, and
/// the surface's size in DIPs.
/// </summary>
internal sealed record MotionStage(
    UIElement Layer,
    double Width,
    double Height,
    IReadOnlyList<MotionActor> Actors,
    double GapPx,
    double CornerRadius);

/// <summary>
/// [S-1000..S-1007] One intro, played once on one surface: every card's keyframes, the hyperspace
/// overlay and shake or the hacker tiles, all on the compositor.
/// <para>
/// Purely cosmetic ([S-1013]): it moves the cards' composition visuals, never their layout, so the
/// hit region, the drag geometry and the saved rects never see a half-animated box, and every widget
/// is live and clickable from the first frame. Its end is event-driven: the finite animations join one
/// scoped batch, whose completion takes the overlay and the tiles down. No timer decides it.
/// </para>
/// </summary>
internal sealed class MotionShow
{
    private const string Scope = "motion";

    private readonly List<WidgetCard> _covered = [];
    private readonly List<Visual> _tilted = [];
    private readonly Action<MotionShow> _ended;
    private CompositionScopedBatch? _batch;
    private bool _over;

    private MotionShow(IntroMode mode, IntroAnimation style, Action<MotionShow> ended)
    {
        Mode = mode;
        Style = style;
        _ended = ended;
    }

    public IntroMode Mode { get; }

    public IntroAnimation Style { get; }

    /// <summary>The full-screen overlay while it plays; the surface widens its hit region for it.</summary>
    public HyperspaceOverlay? Overlay { get; private set; }

    /// <summary>
    /// Start a show <paramref name="ms"/> long in all ([S-1760]): the overlay, the cascade and every
    /// widget's own animation fit inside it. <paramref name="mountOverlay"/> puts the overlay's element under the widgets
    /// before anything starts, so its first frame is already black. <paramref name="ended"/> runs once,
    /// on the UI thread, when the last finite animation has finished — or at once if nothing played.
    /// </summary>
    public static MotionShow Start(
        MotionStage stage,
        IntroMode mode,
        IntroAnimation style,
        double ms,
        Action<FrameworkElement> mountOverlay,
        Action<MotionShow> ended)
    {
        var show = new MotionShow(mode, style, ended);
        var compositor = ElementCompositionPreview.GetElementVisual(stage.Layer).Compositor;
        var kit = new MotionKit(compositor);
        var loops = new List<HackerTile.Built>();
        var schedule = IntroSchedule.For(style, mode, ms, stage.Actors.Count);

        if (schedule.Overlay)
        {
            show.Overlay = new HyperspaceOverlay(stage.Width, stage.Height, schedule);
            mountOverlay(show.Overlay.Element);
        }

        show._batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        show._batch.Completed += (_, _) => show.Finish();

        try
        {
            show.Overlay?.StartTimeline(kit);

            if (show.Overlay is not null)
            {
                Shake(kit, stage, schedule);
            }

            if (IntroMotion.UsesTiles(style))
            {
                loops.AddRange(show.CoverAll(kit, stage, schedule));
            }
            else if (IntroTracks.For(style) is { } tracks)
            {
                show.AnimateAll(kit, stage, tracks, style, schedule);
            }
        }
        finally
        {
            // Ended whatever happened above, so a failure part-way still completes and cleans up.
            show._batch.End();
        }

        // Loops and expressions never finish, so they stay out of the batch; they leave with the
        // elements that carry them.
        show.Overlay?.StartField(kit);
        foreach (var built in loops)
        {
            built.StartLoops(kit);
        }

        Log.Debug(Scope, $"intro {mode}/{style} {schedule.TotalMs:0} ms over {stage.Actors.Count} widget(s){(show.Overlay is null ? string.Empty : ", with the overlay")}");
        return show;
    }

    /// <summary>
    /// End the show now: a view change replaces it, or the surface closes. The cards keep whatever
    /// their animations reached and the next show (or none) takes them from there; tiles and the
    /// overlay go at once.
    /// </summary>
    public void Stop() => Finish();

    private void AnimateAll(MotionKit kit, MotionStage stage, IntroTracks tracks, IntroAnimation style, IntroSchedule schedule)
    {
        SheetOffsets? sheets = IntroSheets.AxisFor(style) is { } axis
            ? IntroSheets.Offsets(stage.Actors.Select(a => a.Rect), axis, axis == SheetAxis.X ? stage.Width : stage.Height)
            : null;

        for (var i = 0; i < stage.Actors.Count; i++)
        {
            var (card, rect) = stage.Actors[i];
            var start = style switch
            {
                IntroAnimation.Hyperspace => Hyperspace.FromOffset(rect, stage.Width, stage.Height),
                _ when sheets is { } s && IntroSheets.AxisFor(style) is { } a => IntroSheets.StartFor(rect, a, s),
                _ => (0.0, 0.0),
            };

            Animate(kit, card, tracks, start, schedule.WidgetMs, schedule.DelayMs(i), stage.GapPx);
        }
    }

    private void Animate(MotionKit kit, WidgetCard card, IntroTracks tracks, (double X, double Y) start, double ms, double delayMs, double gapPx)
    {
        var visual = ElementCompositionPreview.GetElementVisual(card);
        var (w, h) = SizeOf(card);
        var width = (float)w;
        var height = (float)h;
        var inset = (float)(Math.Max(0, gapPx) / 2);

        if (tracks.Axis == IntroAxis.Tilt)
        {
            // Flip turns about the painted surface's top edge, toward the viewer.
            visual.CenterPoint = new Vector3(width / 2, inset, 0);
            visual.RotationAxis = Vector3.UnitX;
        }
        else
        {
            visual.CenterPoint = new Vector3(width / 2, height / 2, 0);
            visual.RotationAxis = Vector3.UnitZ;
        }

        if (tracks.Perspective)
        {
            // Perspective about the same pivot. Flat at rest — a z = 0 point is untouched by it — and
            // reset at the end anyway, so a card never keeps a transform it did not ask for.
            var pivot = new Vector3(width / 2, inset, 0);
            var perspective = Matrix4x4.Identity;
            perspective.M34 = -1f / (float)IntroTracks.PerspectiveDips;
            visual.TransformMatrix = Matrix4x4.CreateTranslation(-pivot) * perspective * Matrix4x4.CreateTranslation(pivot);
            _tilted.Add(visual);
        }

        if (tracks.Opacity.Length > 0)
        {
            visual.StartAnimation("Opacity", kit.Scalar(tracks.Opacity, ms, delayMs));
        }

        if (tracks.Scale.Length > 0)
        {
            visual.StartAnimation("Scale", kit.Scale(tracks.Scale, ms, delayMs));
        }

        if (tracks.Move.Length > 0)
        {
            ElementCompositionPreview.SetIsTranslationEnabled(card, true);
            visual.StartAnimation("Translation", kit.Move(tracks.Move, start, ms, delayMs));
        }

        if (tracks.Rotation.Length > 0)
        {
            visual.StartAnimation("RotationAngleInDegrees", kit.Scalar(tracks.Rotation, ms, delayMs));
        }
    }

    /// <summary>[S-1007] Every card gets its tile, opaque at once, fading out on its own timing.</summary>
    private List<HackerTile.Built> CoverAll(MotionKit kit, MotionStage stage, IntroSchedule schedule)
    {
        var built = new List<HackerTile.Built>(stage.Actors.Count);
        var inset = Math.Max(0, stage.GapPx);

        for (var i = 0; i < stage.Actors.Count; i++)
        {
            var card = stage.Actors[i].Card;
            var (width, height) = SizeOf(card);
            var tile = HackerTile.Build(i, width - inset, height - inset, stage.CornerRadius);
            card.SetMotionCover(tile.Tile);
            _covered.Add(card);

            var timing = schedule.Tile(i);
            ElementCompositionPreview.GetElementVisual(tile.Tile).StartAnimation("Opacity", kit.FadeOut(timing.RevealMs, timing.FadeMs));
            built.Add(tile);
        }

        return built;
    }

    /// <summary>
    /// A card's cell size. A card created by the same layout pass has its size set but not yet
    /// measured, so the set size stands in until the first arrange.
    /// </summary>
    private static (double Width, double Height) SizeOf(WidgetCard card)
    {
        static double Pick(double actual, double set) => actual > 0 ? actual : double.IsFinite(set) ? set : 0;
        return (Pick(card.ActualWidth, card.Width), Pick(card.ActualHeight, card.Height));
    }

    /// <summary>[S-1006] The widget layer takes the warp's hits, on the overlay's scaled clock; the stars stay steady.</summary>
    private static void Shake(MotionKit kit, MotionStage stage, IntroSchedule schedule)
    {
        var visual = ElementCompositionPreview.GetElementVisual(stage.Layer);
        visual.CenterPoint = new Vector3((float)(stage.Width / 2), (float)(stage.Height / 2), 0);
        visual.RotationAxis = Vector3.UnitZ;
        ElementCompositionPreview.SetIsTranslationEnabled(stage.Layer, true);

        var move = Hyperspace.Shake.Select(k => new MoveKey(k.At, 0, k.X, k.Y, MotionEase.Linear)).ToArray();
        var roll = Hyperspace.Shake.Select(k => new MotionKey(k.At, k.Degrees, MotionEase.Linear)).ToArray();
        var duration = schedule.Scaled(Hyperspace.ShakeMs);
        var delay = schedule.Scaled(Hyperspace.StaticMs);
        visual.StartAnimation("Translation", kit.Move(move, (0, 0), duration, delay));
        visual.StartAnimation("RotationAngleInDegrees", kit.Scalar(roll, duration, delay));
    }

    private void Finish()
    {
        if (_over)
        {
            return;
        }

        _over = true;

        try
        {
            foreach (var card in _covered)
            {
                card.SetMotionCover(null);
            }

            foreach (var visual in _tilted)
            {
                visual.TransformMatrix = Matrix4x4.Identity;
            }

            Overlay?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "taking an intro down failed", ex);
        }

        _covered.Clear();
        _tilted.Clear();
        _batch = null;

        try
        {
            _ended(this);
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "the surface failed to hear an intro end", ex);
        }
    }
}
