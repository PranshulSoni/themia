using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>A focus/break timer. [S-01] Run state is deliberately not persisted.</summary>
public sealed class PomodoroConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Pomodoro;

    /// <summary>Length of a focus phase, 1 to 180 minutes.</summary>
    public int WorkMinutes { get; set; } = 25;

    /// <summary>Length of a break phase, 1 to 180 minutes.</summary>
    public int BreakMinutes { get; set; } = 5;

    /// <summary>Sound a cue when the phase flips. [S-06]</summary>
    public bool Alarm { get; set; } = true;

    [JsonIgnore]
    public int EffectiveWorkMinutes => Math.Clamp(WorkMinutes, 1, 180);

    [JsonIgnore]
    public int EffectiveBreakMinutes => Math.Clamp(BreakMinutes, 1, 180);

    public override WidgetConfig Clone() => new PomodoroConfig
    {
        WorkMinutes = WorkMinutes,
        BreakMinutes = BreakMinutes,
        Alarm = Alarm,
    };
}
