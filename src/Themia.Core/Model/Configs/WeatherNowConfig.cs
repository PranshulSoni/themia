using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Placeholder: the real WeatherNow settings replace this file wholesale.</summary>
public sealed class WeatherNowConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.WeatherNow;

    public override WidgetConfig Clone() => new WeatherNowConfig();
}
