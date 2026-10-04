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
