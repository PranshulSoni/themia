namespace Themia.Core.Widgets;

/// <summary>[S-1531] How the Help widget arranges its pages for the room it has.</summary>
public enum HelpArrangement
{
    /// <summary>No tabs: the open page scrolls on its own (four icons no longer fit).</summary>
    PageOnly,

    /// <summary>A strip of icon tabs above the page; each tab names itself in its tooltip.</summary>
    IconTabs,

    /// <summary>A list of labelled pages on the left and the open page beside it, as Windows Settings does.</summary>
    TwoPane,
}

/// <summary>
/// [S-1531] The Help widget's disclosure ladder. Pure, so every tier is pinned by a test.
/// <para>
/// The prose always comes first. Width buys navigation: icon tabs from a Regular width, a
/// labelled page list beside the page from an Expanded one — which also keeps the line length
/// readable on a wide tile, where one column of prose would run 500 DIPs a line. Height and width
/// together buy the extras: buttons into edit mode and Settings under the page list (L), and a
/// card of the keys beside the page (XL).
/// </para>
/// </summary>
public static class HelpLayout
{
    /// <summary>The page list's width in the two-pane arrangement.</summary>
    public const double PaneWidth = 148;

    /// <summary>The keys card's width at XL.</summary>
    public const double KeysWidth = 232;

    /// <summary>Below this page width the prose drops from Body to Caption, so a line still holds a phrase.</summary>
    public const double BodyProseFrom = 240;

    public static HelpArrangement Arrangement(WidgetArea area) => area.WidthClass switch
    {
        SizeClass.Compact => HelpArrangement.PageOnly,
        SizeClass.Regular => HelpArrangement.IconTabs,
        _ => HelpArrangement.TwoPane,
    };

    /// <summary>[S-1531] "Edit widgets" and "Settings" under the page list: L and XL.</summary>
    public static bool Actions(WidgetArea area)
        => Arrangement(area) == HelpArrangement.TwoPane && area.HeightClass >= SizeClass.Expanded;

    /// <summary>[S-1531] The keys card beside the page: XL only.</summary>
    public static bool Keys(WidgetArea area) => area.AtLeast(SizeClass.Huge, SizeClass.Huge);

    /// <summary>The width the prose actually gets, once the page list and the keys card have taken theirs.</summary>
    public static double PageWidth(WidgetArea area)
    {
        var width = area.Width;

        if (Arrangement(area) == HelpArrangement.TwoPane)
        {
            width -= PaneWidth;
        }

        if (Keys(area))
        {
            width -= KeysWidth;
        }

        return Math.Max(0, width);
    }

    /// <summary>Whether the prose is set in Body (else Caption).</summary>
    public static bool BodyProse(WidgetArea area) => PageWidth(area) >= BodyProseFrom;
}
