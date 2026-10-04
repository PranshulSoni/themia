using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Round-trip time to a host.</summary>
public sealed class PingConfig : GaugeConfig
{
    /// <summary>A public resolver: reachable without configuration, and not a service this app talks to.</summary>
    public const string DefaultHost = "1.1.1.1";

    /// <summary>Every ten seconds over twenty minutes: a probe is a packet to someone else's host.</summary>
    public PingConfig()
        : base(intervalSeconds: 10, graphSpanSeconds: 1200)
    {
    }

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Ping;

    /// <summary>Host or address to probe. Empty falls back to <see cref="DefaultHost"/>.</summary>
    public string Host { get; set; } = DefaultHost;

    /// <summary>A probe goes over the network, so it may be slower than a local read: up to five minutes.</summary>
    [JsonIgnore]
    public override int MaxIntervalSeconds => 300;

    /// <summary>The host actually probed. Never empty, so the widget always has something to show.</summary>
    [JsonIgnore]
    public string EffectiveHost => string.IsNullOrWhiteSpace(Host) ? DefaultHost : Host.Trim();

    public override WidgetConfig Clone()
    {
        var copy = new PingConfig { Host = Host };
        CopyTo(copy);
        return copy;
    }
}
