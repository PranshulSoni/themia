using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Placeholder: the real Group settings replace this file wholesale.</summary>
public sealed class GroupConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Group;

    public override WidgetConfig Clone() => new GroupConfig();
}
