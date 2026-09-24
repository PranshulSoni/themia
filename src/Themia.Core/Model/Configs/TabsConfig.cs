using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>
/// Several widgets in one rect, one shown at a time, switched from a tab strip.
/// <para>
/// Like a stack, a tab strip owns its children's geometry. Unlike a stack, exactly one child is ever
/// visible, so the hidden ones are collapsed rather than merely short — a widget nobody can see
/// should not be laying itself out.
/// </para>
/// </summary>
public sealed class TabsConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Tabs;

    /// <summary>The selected child by id; an unknown id falls back to the first.</summary>
    public string? SelectedChildId { get; set; }

    public override WidgetConfig Clone() => new TabsConfig { SelectedChildId = SelectedChildId };
}
