using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Space used on a drive.</summary>
public sealed class DiskConfig : GaugeConfig
{
    /// <summary>
    /// Every five seconds over three minutes, the original's defaults. The graph is off by default,
    /// unlike the live meters: free space barely moves, and the bar says the same thing at once.
    /// </summary>
    public DiskConfig()
        : base(intervalSeconds: 5, graphSpanSeconds: 180)
        => ShowGraph = false;

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Disk;

    /// <summary>Drive root to report, e.g. <c>C:\</c>. Empty means the system drive.</summary>
    public string Drive { get; set; } = string.Empty;

    public override WidgetConfig Clone()
    {
        var copy = new DiskConfig { Drive = Drive };
        CopyTo(copy);
        return copy;
    }
}
