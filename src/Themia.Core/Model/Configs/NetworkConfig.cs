using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>
/// [S-1195] Throughput across the active adapters, and what the original offered beside it. Each
/// figure is a toggle saying whether it <i>may</i> appear; the tile decides whether it fits. On by
/// default, except the public address, which asks a third-party service and so is opt-in.
/// </summary>
public sealed class NetworkConfig : GaugeConfig
{
    /// <summary>The most adapters the list may hold; the tile decides how many fit.</summary>
    public const int MaxInterfaces = 8;

    public NetworkConfig()
        : base(intervalSeconds: 1, graphSpanSeconds: 60)
    {
    }

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Network;

    /// <summary>[S-1193] The busiest adapter, its Wi-Fi network and signal, and its link speed.</summary>
    public bool ShowConnection { get; set; } = true;

    /// <summary>[S-1193] The busiest adapter's local IPv4 (else IPv6) address.</summary>
    public bool ShowAddress { get; set; } = true;

    /// <summary>[S-1194] The machine's public address, from api.ipify.org every ten minutes. Off by default.</summary>
    public bool ShowPublicAddress { get; set; }

    /// <summary>[S-1190] Downloaded and uploaded since Themia started.</summary>
    public bool ShowSession { get; set; } = true;

    /// <summary>[S-1190] One row per adapter, busiest first.</summary>
    public bool ShowInterfaces { get; set; } = true;

    public override WidgetConfig Clone()
    {
        var copy = new NetworkConfig
        {
            ShowConnection = ShowConnection,
            ShowAddress = ShowAddress,
            ShowPublicAddress = ShowPublicAddress,
            ShowSession = ShowSession,
            ShowInterfaces = ShowInterfaces,
        };

        CopyTo(copy);
        return copy;
    }
}
