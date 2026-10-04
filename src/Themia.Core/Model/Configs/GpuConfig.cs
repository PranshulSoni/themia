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
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Gpu;

    /// <summary>
    /// [S-433] Which adapters to show, as positions in the system's adapter list, in the order they
    /// are drawn. Untrusted on read: <see cref="GpuRules.ResolveAdapters"/> is the boundary.
    /// </summary>
    public List<int> Adapters { get; set; } = [0];

    /// <summary>[S-436] Seconds between samples, 1 to 60.</summary>
    public int IntervalSeconds { get; set; } = 2;

    /// <summary>[S-438] The dedicated video memory row.</summary>
    public bool ShowDedicated { get; set; } = true;

    /// <summary>[S-438] The shared system memory row.</summary>
    public bool ShowShared { get; set; } = true;

    /// <summary>[S-439] Driver-reported rows, which only some cards answer. Off by default.</summary>
    public bool ShowTemperature { get; set; }

    public bool ShowPower { get; set; }

    public bool ShowFan { get; set; }

    public bool ShowClocks { get; set; }

    public bool ShowVideoEngines { get; set; }

    [JsonIgnore]
    public override TimeSpan TickInterval => TimeSpan.FromSeconds(Math.Clamp(IntervalSeconds, 1, 60));

    public override WidgetConfig Clone()
    {
        var copy = new GpuConfig
        {
            // A new list: two widgets sharing one would follow each other's picker.
            Adapters = [.. Adapters ?? []],
            IntervalSeconds = IntervalSeconds,
            ShowDedicated = ShowDedicated,
            ShowShared = ShowShared,
            ShowTemperature = ShowTemperature,
            ShowPower = ShowPower,
            ShowFan = ShowFan,
            ShowClocks = ShowClocks,
            ShowVideoEngines = ShowVideoEngines,
        };

        CopyTo(copy);
        return copy;
    }
}
