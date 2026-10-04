namespace Themia.Core.Widgets;

/// <summary>
/// [S-1529] The Search widget's disclosure ladder. Pure, so every tier is pinned by a test.
/// <para>
/// The box first, always, at its stock height. Then, by class: the engine chips from an Expanded
/// width (a row of the engines to switch between — on anything narrower they would wrap or crowd
/// the box); the larger box type on the largest tile. The hint line and the recent searches are
/// budget rungs the view takes with <see cref="Disclosure"/>: the hint when a line is left, the
/// recent searches as whole rows of what remains.
/// </para>
/// </summary>
public static class WebSearchLayout
{
    /// <summary>One engine chip's height.</summary>
    public const double ChipHeight = 28;

    /// <summary>One recent-search row.</summary>
    public const double RecentRow = 32;

    /// <summary>The engine chips: Wide, L, XL.</summary>
    public static bool Chips(WidgetArea area) => area.WidthClass >= SizeClass.Expanded;

    /// <summary>The box set one step up the type ramp: XL.</summary>
    public static bool LargeBox(WidgetArea area) => area.AtLeast(SizeClass.Huge, SizeClass.Huge);

    /// <summary>How many recent searches the ladder offers at most, by height: 5, 8 from Expanded (Tall, L), 10 from Huge.</summary>
    public static int RecentWanted(WidgetArea area) => area.HeightClass switch
    {
        SizeClass.Huge => RecentSearches.Capacity,
        SizeClass.Expanded => 8,
        _ => 5,
    };
}
