using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>
/// Mains status, the active power plan and battery saver.
/// <para>
/// Not a gauge: a desktop has no charge to draw a history of, and the three facts the widget
/// carries are states rather than samples.
/// </para>
/// </summary>
public sealed class PowerConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Power;

    /// <summary>Show the active power plan's friendly name.</summary>
    public bool ShowPlan { get; set; } = true;

    /// <summary>Show whether battery saver is on.</summary>
    public bool ShowBatterySaver { get; set; } = true;

    /// <summary>Show Windows' estimate of time left. It only estimates while discharging.</summary>
    public bool ShowTimeRemaining { get; set; } = true;

    /// <summary>
    /// Seconds between reads, 5 to 300. Slower than a meter by default: a power plan changes when
    /// somebody changes it, and the charge moves by a percent every few minutes at most.
    /// </summary>
    public int IntervalSeconds { get; set; } = 15;

    /// <summary>Clamped on read, so a persisted zero cannot turn the shared ticker into a spin.</summary>
    [JsonIgnore]
    public TimeSpan TickInterval => TimeSpan.FromSeconds(Math.Clamp(IntervalSeconds, 5, 300));

    public override WidgetConfig Clone() => new PowerConfig
    {
        ShowPlan = ShowPlan,
        ShowBatterySaver = ShowBatterySaver,
        ShowTimeRemaining = ShowTimeRemaining,
        IntervalSeconds = IntervalSeconds,
    };
}
