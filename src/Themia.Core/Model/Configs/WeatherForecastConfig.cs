using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Placeholder: the real WeatherForecast settings replace this file wholesale.</summary>
public sealed class WeatherForecastConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.WeatherForecast;

    public override WidgetConfig Clone() => new WeatherForecastConfig();
}
