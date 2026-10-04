using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Throughput across the active adapters.</summary>
public sealed class NetworkConfig : GaugeConfig
{
    public NetworkConfig()
        : base(intervalSeconds: 1, graphSpanSeconds: 60)
    {
    }

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Network;

    public override WidgetConfig Clone()
    {
        var copy = new NetworkConfig();
        CopyTo(copy);
        return copy;
    }
}
