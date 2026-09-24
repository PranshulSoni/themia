using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Placeholder: the real Stack settings replace this file wholesale.</summary>
public sealed class StackConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Stack;

    public override WidgetConfig Clone() => new StackConfig();
}
