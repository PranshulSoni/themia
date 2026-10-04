using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Round-trip time to a host, and the statistics behind it.</summary>
public sealed class PingConfig : GaugeConfig
{
    /// <summary>A public resolver: reachable without configuration, and not a service this app talks to.</summary>
    public const string DefaultHost = "1.1.1.1";

    /// <summary>The longest name a host may be given; the header has room for less.</summary>
    public const int MaxLabelLength = 64;

    /// <summary>Every ten seconds over twenty minutes: a probe is a packet to someone else's host.</summary>
    public PingConfig()
        : base(intervalSeconds: 10, graphSpanSeconds: 1200)
    {
    }

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Ping;

    /// <summary>Host or address to probe. Empty falls back to <see cref="DefaultHost"/>.</summary>
    public string Host { get; set; } = DefaultHost;

    /// <summary>[S-1173] A name for the host ("Router", "Game server"): the header title, with the address as a detail line.</summary>
    public string HostLabel { get; set; } = string.Empty;

    /// <summary>[S-1171] How much consecutive answers vary.</summary>
    public bool ShowJitter { get; set; } = true;

    /// <summary>[S-1171] The share of probes that went unanswered.</summary>
    public bool ShowLoss { get; set; } = true;

    /// <summary>[S-1171] Fastest, mean and slowest answer.</summary>
    public bool ShowMinAvgMax { get; set; } = true;

    /// <summary>A probe goes over the network, so it may be slower than a local read: up to five minutes.</summary>
    [JsonIgnore]
    public override int MaxIntervalSeconds => 300;

    /// <summary>The host actually probed. Never empty, so the widget always has something to show.</summary>
    [JsonIgnore]
    public string EffectiveHost => string.IsNullOrWhiteSpace(Host) ? DefaultHost : Host.Trim();

    /// <summary>[S-1173] The label, trimmed; null when there is none.</summary>
    [JsonIgnore]
    public string? EffectiveLabel => string.IsNullOrWhiteSpace(HostLabel) ? null : HostLabel.Trim();

    public override WidgetConfig Clone()
    {
        var copy = new PingConfig
        {
            Host = Host,
            HostLabel = HostLabel,
            ShowJitter = ShowJitter,
            ShowLoss = ShowLoss,
            ShowMinAvgMax = ShowMinAvgMax,
        };
        CopyTo(copy);
        return copy;
    }

    /// <summary>[S-1173] A label too long for any header is cut to <see cref="MaxLabelLength"/>, loudly.</summary>
    public override IReadOnlyList<string> Repair()
    {
        var notes = new List<string>(base.Repair());

        HostLabel ??= string.Empty;
        Host ??= DefaultHost;

        if (HostLabel.Length > MaxLabelLength)
        {
            notes.Add($"{Kind}: host label is {HostLabel.Length} characters; cut to {MaxLabelLength}");
            HostLabel = HostLabel[..MaxLabelLength];
        }

        return notes;
    }
}
