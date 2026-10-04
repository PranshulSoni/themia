using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Processor load. Its own options (cores, frequency, top processes…) belong here.</summary>
public sealed class CpuConfig : GaugeConfig
{
    /// <summary>Load is only honest at a one-second cadence; a minute of history.</summary>
    public CpuConfig()
        : base(intervalSeconds: 1, graphSpanSeconds: 60)
    {
    }

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Cpu;

    public override WidgetConfig Clone()
    {
        var copy = new CpuConfig();
        CopyTo(copy);
        return copy;
    }
}
