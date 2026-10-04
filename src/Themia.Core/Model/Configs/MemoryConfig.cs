using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>
/// [S-1130] Physical memory in use, and what the original's metric picker offered beside it. Each
/// metric is a toggle saying whether it <i>may</i> appear; the size of the tile decides whether it
/// fits. All on by default: they show only where there is room, which is what a bigger tile is for.
/// </summary>
public sealed class MemoryConfig : GaugeConfig
{
    /// <summary>The most top-process rows the list may hold; the tile decides how many fit.</summary>
    public const int MaxTopProcesses = 10;

    public MemoryConfig()
        : base(intervalSeconds: 1, graphSpanSeconds: 60)
    {
    }

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Memory;

    /// <summary>[S-1131] The usage bar under the reading beside the chart; in-use / cached / free on a large tile.</summary>
    public bool ShowBar { get; set; } = true;

    /// <summary>[S-1132] "Available 5.9 GB".</summary>
    public bool ShowAvailable { get; set; } = true;

    /// <summary>[S-1132] The page files: used of total and a share. Hidden on a machine without one.</summary>
    public bool ShowSwap { get; set; } = true;

    /// <summary>[S-1132] The commit charge: committed of the commit limit.</summary>
    public bool ShowCommitted { get; set; } = true;

    /// <summary>[S-1133] The processes holding the most memory.</summary>
    public bool ShowTopProcesses { get; set; } = true;

    /// <summary>[S-1133] A ceiling, 1 to <see cref="MaxTopProcesses"/>: a short tile shows fewer.</summary>
    public int TopProcessCount { get; set; } = MaxTopProcesses;

    [JsonIgnore]
    public int EffectiveTopProcessCount => Math.Clamp(TopProcessCount, 1, MaxTopProcesses);

    public override WidgetConfig Clone()
    {
        var copy = new MemoryConfig
        {
            ShowBar = ShowBar,
            ShowAvailable = ShowAvailable,
            ShowSwap = ShowSwap,
            ShowCommitted = ShowCommitted,
            ShowTopProcesses = ShowTopProcesses,
            TopProcessCount = TopProcessCount,
        };

        CopyTo(copy);
        return copy;
    }

    public override IReadOnlyList<string> Repair()
    {
        var notes = new List<string>(base.Repair());

        if (EffectiveTopProcessCount != TopProcessCount)
        {
            notes.Add($"{Kind}: top processes {TopProcessCount} is outside 1..{MaxTopProcesses}; using {EffectiveTopProcessCount}");
            TopProcessCount = EffectiveTopProcessCount;
        }

        return notes;
    }
}
