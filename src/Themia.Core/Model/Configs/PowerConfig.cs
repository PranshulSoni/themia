using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Placeholder: the real Power settings replace this file wholesale.</summary>
public sealed class PowerConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Power;

    public override WidgetConfig Clone() => new PowerConfig();
}
