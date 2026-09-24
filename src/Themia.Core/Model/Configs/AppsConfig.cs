using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Placeholder: the real Apps settings replace this file wholesale.</summary>
public sealed class AppsConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Apps;

    public override WidgetConfig Clone() => new AppsConfig();
}
