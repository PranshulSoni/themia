using System.Text.Json.Serialization;
using Themia.Core.Widgets;

namespace Themia.Core.Model;

/// <summary>
/// [S-430] The graphics meter: load of the busiest engine, video memory, and — on cards whose driver
/// will say — temperature, power, fan, clocks and the video engines.
/// <para>
/// A gauge, so it carries the history setting the other meters do, and it draws the same sparkline.
/// </para>
/// </summary>
public sealed class GpuConfig : GaugeConfig
{
    /// <summary>[S-436] Every two seconds; a minute of history.</summary>
    public GpuConfig()
        : base(intervalSeconds: 2, graphSpanSeconds: 60)
    {
    }

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Gpu;

    /// <summary>
    /// [S-433] Which adapters to show, as positions in the system's adapter list, in the order they
    /// are drawn. Untrusted on read: <see cref="GpuRules.ResolveAdapters"/> is the boundary.
    /// </summary>
    public List<int> Adapters { get; set; } = [0];

    /// <summary>[S-438] The dedicated video memory row.</summary>
    public bool ShowDedicated { get; set; } = true;

    /// <summary>[S-438] The shared system memory row.</summary>
    public bool ShowShared { get; set; } = true;

    /// <summary>
    /// [S-439] [S-1208] Driver-reported lines, which only some cards answer. On by default: each one
    /// hides itself when the driver says nothing, and the ladder shows it only where there is room,
    /// so on a card that answers they fill a large tile and cost a small one nothing.
    /// </summary>
    public bool ShowTemperature { get; set; } = true;

    public bool ShowPower { get; set; } = true;

    public bool ShowFan { get; set; } = true;

    public bool ShowClocks { get; set; } = true;

    /// <summary>[S-1203] One bar per engine kind (3D, copy, compute, video decode and encode) on a tall tile.</summary>
    public bool ShowEngines { get; set; } = true;

    /// <summary>[S-1205] The processes using the adapter most, on a tile large both ways.</summary>
    public bool ShowProcesses { get; set; } = true;

    public override WidgetConfig Clone()
    {
        var copy = new GpuConfig
        {
            // A new list: two widgets sharing one would follow each other's picker.
            Adapters = [.. Adapters ?? []],
            ShowDedicated = ShowDedicated,
            ShowShared = ShowShared,
            ShowTemperature = ShowTemperature,
            ShowPower = ShowPower,
            ShowFan = ShowFan,
            ShowClocks = ShowClocks,
            ShowEngines = ShowEngines,
            ShowProcesses = ShowProcesses,
        };

        CopyTo(copy);
        return copy;
    }
}
