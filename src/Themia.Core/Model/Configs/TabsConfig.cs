using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Placeholder: the real Tabs settings replace this file wholesale.</summary>
public sealed class TabsConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Tabs;

    public override WidgetConfig Clone() => new TabsConfig();
}
