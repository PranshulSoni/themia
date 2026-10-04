namespace Themia.Core.Model;

/// <summary>
/// [S-862] Which header placements a widget may have. Top, bottom and none for every widget; the left
/// and right edges (a turned header) only for a group, whose header is a title bar for an area and
/// reads naturally along its side. Any other widget's header carries trailing content — a count, a
/// button, a busy ring — that was never laid out to run sideways.
/// </summary>
public static class HeaderRules
{
    /// <summary>Whether <paramref name="placement"/> is one <paramref name="kind"/> may use.</summary>
    public static bool Allows(WidgetKind kind, HeaderPlacement placement) => placement switch
    {
        HeaderPlacement.Top or HeaderPlacement.Bottom or HeaderPlacement.Hidden => true,
        HeaderPlacement.Left or HeaderPlacement.Right => kind == WidgetKind.Group,
        _ => false,
    };

    /// <summary>Whether the header runs along a side edge, turned.</summary>
    public static bool IsSide(HeaderPlacement placement) => placement is HeaderPlacement.Left or HeaderPlacement.Right;

    /// <summary>The placements the settings offer for <paramref name="kind"/>, in the order they are listed.</summary>
    public static IReadOnlyList<HeaderPlacement> Choices(WidgetKind kind) => kind == WidgetKind.Group
        ? [HeaderPlacement.Top, HeaderPlacement.Bottom, HeaderPlacement.Left, HeaderPlacement.Right, HeaderPlacement.Hidden]
        : [HeaderPlacement.Top, HeaderPlacement.Bottom, HeaderPlacement.Hidden];
}
