using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>
/// The busiest processes. Not a gauge: there is no single fraction to draw, so it does not inherit
/// the meter's graph setting.
/// </summary>
public sealed class ProcessesConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Processes;

    /// <summary>Which column the list is ranked on.</summary>
    public ProcessSort Sort { get; set; } = ProcessSort.Cpu;

    /// <summary>How many rows to show, 1 to 20.</summary>
    public int MaxCount { get; set; } = 6;

    /// <summary>Show the memory column beside the processor column.</summary>
    public bool ShowMemory { get; set; } = true;

    /// <summary>
    /// Sampling a few hundred processes is the most expensive thing on the tick, and process use
    /// is noisy enough that a faster cadence only adds jitter.
    /// </summary>
    [JsonIgnore]
    public TimeSpan TickInterval => TimeSpan.FromSeconds(2);

    /// <summary>Clamped on read: a persisted zero would draw an empty list.</summary>
    [JsonIgnore]
    public int EffectiveMaxCount => Math.Clamp(MaxCount, 1, 20);

    public override WidgetConfig Clone() => new ProcessesConfig
    {
        Sort = Sort,
        MaxCount = MaxCount,
        ShowMemory = ShowMemory,
    };
}

/// <summary>Throughput across the active adapters. A gauge, so it carries the history setting.</summary>
public sealed class NetworkConfig : GaugeConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Network;

    public override WidgetConfig Clone()
    {
        var copy = new NetworkConfig();
        CopyTo(copy);
        return copy;
    }
}

/// <summary>Round-trip time to a host.</summary>
public sealed class PingConfig : GaugeConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Ping;

    /// <summary>Host or address to probe. Empty falls back to <see cref="DefaultHost"/>.</summary>
    public string Host { get; set; } = DefaultHost;

    /// <summary>Seconds between probes, 1 to 300.</summary>
    public int IntervalSeconds { get; set; } = 10;

    /// <summary>A public resolver: reachable without configuration, and not a service this app talks to.</summary>
    public const string DefaultHost = "1.1.1.1";

    /// <summary>The host actually probed. Never empty, so the widget always has something to show.</summary>
    [JsonIgnore]
    public string EffectiveHost => string.IsNullOrWhiteSpace(Host) ? DefaultHost : Host.Trim();

    [JsonIgnore]
    public override TimeSpan TickInterval => TimeSpan.FromSeconds(Math.Clamp(IntervalSeconds, 1, 300));

    public override WidgetConfig Clone()
    {
        var copy = new PingConfig { Host = Host, IntervalSeconds = IntervalSeconds };
        CopyTo(copy);
        return copy;
    }
}
