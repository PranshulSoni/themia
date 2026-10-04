using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>
/// The getting-started guide. [S-480] Nothing to configure: the guide is the same for everyone, and
/// the tab it is open on is something you look at, not a setting worth keeping.
/// </summary>
public sealed class HelpConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Help;

    public override WidgetConfig Clone() => new HelpConfig();
}

/// <summary>The Themia mark with the name and version. [S-484] Nothing to configure.</summary>
public sealed class ThemiaConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Themia;

    public override WidgetConfig Clone() => new ThemiaConfig();
}

/// <summary>
/// A row of buttons, one per view, that switches the display the widget sits on. [S-487]
/// </summary>
public sealed class ViewSwitcherConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.ViewSwitcher;

    /// <summary>
    /// The views to offer, by id. <b>Empty means every view</b>, so a view created later shows up
    /// without anyone having to come back here and tick it. An id whose view has gone is ignored
    /// on read rather than repaired on load: it costs nothing and the view may only be renamed.
    /// </summary>
    public List<string> VisibleViews { get; set; } = [];

    /// <summary>Show only each view's initials, for a narrow strip.</summary>
    public bool IconOnly { get; set; }

    public override WidgetConfig Clone() => new ViewSwitcherConfig
    {
        VisibleViews = [.. VisibleViews ?? []],
        IconOnly = IconOnly,
    };
}
