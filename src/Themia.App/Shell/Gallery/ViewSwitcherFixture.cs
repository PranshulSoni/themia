#if DEBUG
using Themia.App.Widgets;
using Themia.Core.Geometry;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-1658] The gallery's pretend display, for the view switcher: three views with layouts behind
/// them, or — for <c>ViewSwitcher+fixture</c> — twelve, so the small sizes show the "+N" overflow
/// and the large ones a grid of view cards.
/// <para>
/// Switching only moves the highlight, and the menu's commands change nothing: the gallery has no
/// surface for a view to appear on and no state to write. Nothing here reaches the real views.
/// </para>
/// </summary>
internal sealed class GalleryViewsDisplay : IWidgetDisplay
{
    /// <summary>Set by the gallery's fixture for the view switcher; cleared before every page.</summary>
    public static bool Crowded { get; set; }

    private static readonly FracRect[] Desk =
    [
        new(0.02, 0.04, 0.22, 0.30),
        new(0.26, 0.04, 0.22, 0.30),
        new(0.02, 0.38, 0.46, 0.40),
        new(0.74, 0.04, 0.24, 0.56),
        new(0.52, 0.62, 0.20, 0.24),
        new(0.74, 0.64, 0.24, 0.22),
    ];

    private static readonly FracRect[] Home =
    [
        new(0.04, 0.06, 0.30, 0.40),
        new(0.04, 0.52, 0.30, 0.34),
        new(0.66, 0.06, 0.30, 0.80),
        new(0.38, 0.70, 0.24, 0.16),
    ];

    private static readonly FracRect[] Play =
    [
        new(0.70, 0.06, 0.26, 0.24),
        new(0.70, 0.34, 0.26, 0.24),
    ];

    private static readonly (ViewSummary View, FracRect[] Tiles)[] Few =
    [
        (new("gallery-work", "Work", null, Desk.Length), Desk),
        (new("gallery-home", "Home office", null, Home.Length), Home),
        // [S-903] One view with an icon of its own, so the gallery shows a glyph beside initials.
        (new("gallery-play", "Gaming", "", Play.Length), Play),
    ];

    private static readonly (ViewSummary View, FracRect[] Tiles)[] Many =
    [
        .. Few,
        (new("gallery-focus", "Focus", null, 1), [new(0.30, 0.20, 0.40, 0.50)]),
        (new("gallery-evening", "Evening", "", Home.Length), Home),
        (new("gallery-travel", "Travel", null, Play.Length), Play),
        (new("gallery-music", "Music", "", 3), [new(0.04, 0.70, 0.40, 0.20), new(0.70, 0.06, 0.26, 0.30), new(0.70, 0.40, 0.26, 0.30)]),
        (new("gallery-reading", "Reading list", null, Desk.Length), Desk),
        (new("gallery-weekend", "Weekend", null, 2), [new(0.10, 0.10, 0.30, 0.30), new(0.60, 0.50, 0.30, 0.30)]),
        (new("gallery-research", "Research", null, Desk.Length), Desk),
        (new("gallery-studio", "Studio", "", Home.Length), Home),
        (new("gallery-spare", "Spare", null, 0), []),
    ];

    private static (ViewSummary View, FracRect[] Tiles)[] Set => Crowded ? Many : Few;

    public IReadOnlyList<ViewSummary> Views => [.. Set.Select(v => v.View)];

    public string ShownViewId { get; private set; } = "gallery-work";

    public void ShowView(string viewId) => ShownViewId = viewId;

    public IReadOnlyList<FracRect> Tiles(string viewId)
        => Set.FirstOrDefault(v => v.View.Id == viewId).Tiles ?? [];

    public double Aspect => 16.0 / 9.0;

    public void DuplicateView(string viewId)
    {
        // ignore: the gallery has no views to copy
    }

    public void MoveView(string viewId, int delta)
    {
        // ignore: the gallery's views are a fixed fixture
    }

    public void DeleteView(string viewId)
    {
        // ignore: the gallery's views are a fixed fixture
    }

    public void OpenViewSettings(string viewId)
    {
        // ignore: the gallery's views have no settings page
    }
}
#endif
