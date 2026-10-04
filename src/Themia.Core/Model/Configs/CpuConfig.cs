using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>
/// [S-1149] Processor load, and what the original's metric picker offered beside it. Each metric
/// is a toggle saying whether it <i>may</i> appear; the size of the tile decides whether it fits.
/// The ones that always have data are on by default, since they only show where there is room;
/// temperature and fan need a sensor most machines do not expose, so they start off and, once on,
/// say "N/A" rather than vanish (the original's [S-20]/[S-23]).
/// </summary>
public sealed class CpuConfig : GaugeConfig
{
    /// <summary>
    /// The most top-process rows the list may hold; the tile decides how many fit. Above the
    /// original's 10 so the list panel of an extra-large tile is filled rather than half empty.
    /// </summary>
    public const int MaxTopProcesses = 20;

    /// <summary>Load is only honest at a one-second cadence; a minute of history.</summary>
    public CpuConfig()
        : base(intervalSeconds: 1, graphSpanSeconds: 60)
    {
    }

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Cpu;

    /// <summary>[S-1141] The clock beside the reading ("4.72 GHz").</summary>
    public bool ShowFrequency { get; set; } = true;

    /// <summary>[S-1142] A bar per logical processor.</summary>
    public bool ShowCores { get; set; } = true;

    /// <summary>[S-1143] Time since boot.</summary>
    public bool ShowUptime { get; set; } = true;

    /// <summary>[S-1144] How many processes and threads are running, and the handles they hold.</summary>
    public bool ShowProcessCount { get; set; } = true;

    /// <summary>[S-1145] The processes using the most processor time.</summary>
    public bool ShowTopProcesses { get; set; } = true;

    /// <summary>[S-1145] A ceiling, 1 to <see cref="MaxTopProcesses"/>: a short tile shows fewer.</summary>
    public int TopProcessCount { get; set; } = MaxTopProcesses;

    /// <summary>[S-1146] The processor temperature, from the ACPI thermal zone or a hardware monitor.</summary>
    public bool ShowTemperature { get; set; }

    /// <summary>[S-1147] The processor fan, from a hardware monitor only.</summary>
    public bool ShowFan { get; set; }

    [JsonIgnore]
    public int EffectiveTopProcessCount => Math.Clamp(TopProcessCount, 1, MaxTopProcesses);

    public override WidgetConfig Clone()
    {
        var copy = new CpuConfig
        {
            ShowFrequency = ShowFrequency,
            ShowCores = ShowCores,
            ShowUptime = ShowUptime,
            ShowProcessCount = ShowProcessCount,
            ShowTopProcesses = ShowTopProcesses,
            TopProcessCount = TopProcessCount,
            ShowTemperature = ShowTemperature,
            ShowFan = ShowFan,
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
