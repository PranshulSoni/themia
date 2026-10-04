using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Charge level.</summary>
public sealed class BatteryConfig : GaugeConfig
{
    /// <summary>Charge moves slowly: every fifteen seconds, five minutes of history.</summary>
    public BatteryConfig()
        : base(intervalSeconds: 15, graphSpanSeconds: 300)
    {
    }

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Battery;

    /// <summary>Show the estimated time remaining under the percentage.</summary>
    public bool ShowTimeRemaining { get; set; } = true;

    public override WidgetConfig Clone()
    {
        var copy = new BatteryConfig { ShowTimeRemaining = ShowTimeRemaining };
        CopyTo(copy);
        return copy;
    }
}
