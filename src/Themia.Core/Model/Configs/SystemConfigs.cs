using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>
/// The busiest processes. Not a gauge: there is no single fraction to draw, so it does not inherit
/// the meter's graph setting.
/// </summary>
public sealed class ProcessesConfig : WidgetConfig
{
    /// <summary>The most rows any tile may show.</summary>
    public const int MaxRows = 30;

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Processes;

    /// <summary>Which column the list is ranked on.</summary>
    public ProcessSort Sort { get; set; } = ProcessSort.Cpu;

    /// <summary>[S-1151] A ceiling, 1 to <see cref="MaxRows"/>: the tile shows as many whole rows as fit, up to this.</summary>
    public int MaxCount { get; set; } = 20;

    /// <summary>Show the memory column beside the processor column.</summary>
    public bool ShowMemory { get; set; } = true;

    /// <summary>[S-1152] A thread-count column, where the row is wide enough.</summary>
    public bool ShowThreads { get; set; } = true;

    /// <summary>[S-1152] A process-id column, where the row is wide enough.</summary>
    public bool ShowPid { get; set; } = true;

    /// <summary>[S-1153] The "312 processes · 4,210 threads" footer, on a tall tile.</summary>
    public bool ShowTotals { get; set; } = true;

    /// <summary>
    /// Sampling a few hundred processes is the most expensive thing on the tick, and process use
    /// is noisy enough that a faster cadence only adds jitter.
    /// </summary>
    [JsonIgnore]
    public TimeSpan TickInterval => TimeSpan.FromSeconds(2);

    /// <summary>Clamped on read: a persisted zero would draw an empty list.</summary>
    [JsonIgnore]
    public int EffectiveMaxCount => Math.Clamp(MaxCount, 1, MaxRows);

    public override WidgetConfig Clone() => new ProcessesConfig
    {
        Sort = Sort,
        MaxCount = MaxCount,
        ShowMemory = ShowMemory,
        ShowThreads = ShowThreads,
        ShowPid = ShowPid,
        ShowTotals = ShowTotals,
    };

    public override IReadOnlyList<string> Repair()
    {
        if (EffectiveMaxCount == MaxCount)
        {
            return [];
        }

        var note = $"{Kind}: rows {MaxCount} is outside 1..{MaxRows}; using {EffectiveMaxCount}";
        MaxCount = EffectiveMaxCount;
        return [note];
    }
}
