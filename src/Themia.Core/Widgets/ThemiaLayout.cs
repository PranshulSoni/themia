namespace Themia.Core.Widgets;

/// <summary>
/// [S-1533] The Themia widget's disclosure ladder. Pure, so every tier is pinned by a test.
/// <para>
/// The mark first, always; then the name, then the version, as the height allows (the view
/// decides those by line height). A strip lays the mark beside the words instead of shrinking it
/// into a centred stack. A large tile spends its room on things to do rather than on a bigger
/// logo: a tagline, the maker and two buttons (L), then links to the release notes, feedback and
/// the source (XL). None of it fetches anything.
/// </para>
/// </summary>
public static class ThemiaLayout
{
    /// <summary>The largest the mark is drawn; past this a large tile shows more, not a bigger logo.</summary>
    public const double MaxMark = 160;

    /// <summary>The largest the mark is drawn on the extra-large tile, where it sits beside a column of words.</summary>
    public const double MaxMarkHuge = 240;

    /// <summary>
    /// The mark beside the words: a strip (2:1 or wider) at least a Regular width wide, and the
    /// extra-large tile, whose words and buttons make a column of their own beside the mark rather
    /// than a tall centred stack with empty sides.
    /// </summary>
    public static bool Beside(WidgetArea area)
        => (area.Shape == AreaShape.Wide && area.WidthClass >= SizeClass.Regular) || Links(area);

    /// <summary>The cap on the mark for this tile.</summary>
    public static double Mark(WidgetArea area) => Links(area) ? MaxMarkHuge : MaxMark;

    /// <summary>The tagline and "Made by Lometics": from an Expanded height (Tall, L, XL); they wrap on a narrow column.</summary>
    public static bool About(WidgetArea area) => area.HeightClass >= SizeClass.Expanded;

    /// <summary>"Edit widgets" and "Settings": L and XL (two buttons side by side need the width).</summary>
    public static bool Actions(WidgetArea area) => area.AtLeast(SizeClass.Expanded, SizeClass.Expanded);

    /// <summary>Release notes, feedback and source: XL only.</summary>
    public static bool Links(WidgetArea area) => area.AtLeast(SizeClass.Huge, SizeClass.Huge);
}
