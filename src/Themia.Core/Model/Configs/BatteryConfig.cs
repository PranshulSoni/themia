using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>
/// [S-1183] Charge level, and what the original's metric picker offered beside it. Each figure is a
/// toggle saying whether it <i>may</i> appear; the size of the tile decides whether it fits, and a
/// figure the battery does not report hides on its own. On by default except temperature, which
/// most batteries have no sensor for.
/// </summary>
public sealed class BatteryConfig : GaugeConfig
{
    /// <summary>Charge moves slowly: every fifteen seconds, five minutes of history.</summary>
    public BatteryConfig()
        : base(intervalSeconds: 15, graphSpanSeconds: 300)
    {
    }

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Battery;

    /// <summary>[S-1180] Windows' estimate under the percentage while discharging; with it, time to full or empty.</summary>
    public bool ShowTimeRemaining { get; set; } = true;

    /// <summary>[S-1182] Full-charge capacity against design capacity: how worn the battery is.</summary>
    public bool ShowHealth { get; set; } = true;

    /// <summary>[S-1182] The charge or drain rate in watts.</summary>
    public bool ShowRate { get; set; } = true;

    /// <summary>[S-1182] Charge cycles, where the firmware counts them.</summary>
    public bool ShowCycles { get; set; } = true;

    /// <summary>[S-1182] Terminal voltage.</summary>
    public bool ShowVoltage { get; set; } = true;

    /// <summary>[S-1182] Pack temperature; "N/A" when there is no sensor. Off by default.</summary>
    public bool ShowTemperature { get; set; }

    /// <summary>[S-1183] Power source, power plan and battery saver.</summary>
    public bool ShowPower { get; set; } = true;

    public override WidgetConfig Clone()
    {
        var copy = new BatteryConfig
        {
            ShowTimeRemaining = ShowTimeRemaining,
            ShowHealth = ShowHealth,
            ShowRate = ShowRate,
            ShowCycles = ShowCycles,
            ShowVoltage = ShowVoltage,
            ShowTemperature = ShowTemperature,
            ShowPower = ShowPower,
        };

        CopyTo(copy);
        return copy;
    }
}
