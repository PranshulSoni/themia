using Themia.Core.Model;

namespace Themia.Core.Motion;

/// <summary>
/// [S-1760] When everything in one intro happens, fitted into the one length the user set. The length
/// is the show's whole wall time — the hyperspace overlay, the wait for its jump, the cascade across
/// every widget and the last widget's own animation — never a per-widget length that the cascade and
/// the overlay then stretch.
/// <para>
/// The cascade keeps the original's 45 ms step while the show has room for it, and is squeezed when
/// it does not: it may take at most <see cref="CascadeShare"/> of the time the widgets have, so a
/// dense view still gives every widget most of the length to arrive in. The overlay's own timeline
/// (static hold, acceleration, cruise, jump, deceleration, fade) is played faster or slower as one
/// piece (<see cref="TimeScale"/>), so its proportions survive any length.
/// </para>
/// </summary>
public sealed class IntroSchedule
{
    /// <summary>The most of the widgets' time the cascade may take.</summary>
    public const double CascadeShare = 0.4;

    private IntroSchedule(double totalMs, double baseDelayMs, double stepMs, double widgetMs, double timeScale, int count, bool overlay)
    {
        TotalMs = totalMs;
        BaseDelayMs = baseDelayMs;
        StepMs = stepMs;
        WidgetMs = widgetMs;
        TimeScale = timeScale;
        Count = count;
        Overlay = overlay;
    }

    /// <summary>The whole show, start to end: the configured length, clamped to the offered range.</summary>
    public double TotalMs { get; }

    /// <summary>How long every widget holds its start pose before the cascade begins: the overlay's jump, else 0.</summary>
    public double BaseDelayMs { get; }

    /// <summary>The delay between one widget's start and the next's.</summary>
    public double StepMs { get; }

    /// <summary>Each widget's own animation: the same for all, so the last one ends exactly at <see cref="TotalMs"/>.</summary>
    public double WidgetMs { get; }

    /// <summary>
    /// Real milliseconds per millisecond of the overlay's natural timeline (<see cref="Hyperspace"/>'s
    /// constants, <see cref="Hyperspace.NaturalMs"/> long); 1 when there is no overlay.
    /// </summary>
    public double TimeScale { get; }

    /// <summary>How many widgets take part.</summary>
    public int Count { get; }

    /// <summary>Whether the hyperspace overlay plays ([S-1005]).</summary>
    public bool Overlay { get; }

    /// <summary>
    /// The schedule for <paramref name="widgets"/> widgets playing <paramref name="style"/> at
    /// <paramref name="mode"/>, in <paramref name="totalMs"/> (clamped by <see cref="IntroMotion.ClampMs"/>).
    /// </summary>
    public static IntroSchedule For(IntroAnimation style, IntroMode mode, double totalMs, int widgets)
    {
        var total = IntroMotion.ClampMs(totalMs);
        var count = Math.Max(0, widgets);
        var overlay = IntroMotion.PlaysOverlay(style, mode);
        var scale = overlay ? total / Hyperspace.NaturalMs : 1;

        // Under the overlay the widgets wait for the jump and may use everything after it; the
        // overlay's own fade runs over the end of their landing.
        var baseDelay = overlay ? Hyperspace.JumpMs * scale : 0;
        var window = total - baseDelay;

        // [S-1004] A sheet moves as one block: no cascade.
        var cascading = count > 1 && IntroSheets.AxisFor(style) is null;
        var spread = cascading
            ? Math.Min(Math.Min((count - 1) * IntroMotion.StaggerStepMs, IntroMotion.StaggerCapMs), window * CascadeShare)
            : 0;

        return new IntroSchedule(total, baseDelay, cascading ? spread / (count - 1) : 0, window - spread, scale, count, overlay);
    }

    /// <summary>When widget <paramref name="index"/> (in cascade order) starts its own animation.</summary>
    public double DelayMs(int index) => BaseDelayMs + (StepMs * Math.Clamp(index, 0, Math.Max(0, Count - 1)));

    /// <summary>When widget <paramref name="index"/> comes to rest.</summary>
    public double EndMs(int index) => DelayMs(index) + WidgetMs;

    /// <summary>The overlay's natural <paramref name="naturalMs"/> in real milliseconds.</summary>
    public double Scaled(double naturalMs) => naturalMs * TimeScale;

    /// <summary>
    /// [S-1007] Widget <paramref name="index"/>'s hacker tile: it covers for 65% of the widget's own
    /// animation and fades over the rest, after its share of the cascade — so the last tile is gone at
    /// exactly <see cref="TotalMs"/>.
    /// </summary>
    public HackerTiming Tile(int index)
    {
        var cover = Math.Round(WidgetMs * HackerIntro.CoverFraction);
        return new HackerTiming(DelayMs(index) + cover, WidgetMs - cover);
    }

    /// <summary>The moment the last thing in the show stops moving.</summary>
    public double LastEndMs => Count == 0 ? (Overlay ? TotalMs : 0) : Math.Max(EndMs(Count - 1), Overlay ? TotalMs : 0);
}
