using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>
/// A labelled area on the desktop that other widgets belong to.
/// <para>
/// A group does <b>not</b> take over its children's geometry: they keep the place on the grid the
/// user put them in, and the group draws behind them. That is the whole difference between a group
/// and a stack, and it is why dropping a widget into a group does not move it.
/// </para>
/// </summary>
public sealed class GroupConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Group;

    /// <summary>
    /// Collapsed hides the children rather than moving them, so expanding puts every one back
    /// exactly where it was. A group that rearranged its contents on collapse would lose the layout.
    /// </summary>
    public bool Collapsed { get; set; }

    public override WidgetConfig Clone() => new GroupConfig { Collapsed = Collapsed };
}
