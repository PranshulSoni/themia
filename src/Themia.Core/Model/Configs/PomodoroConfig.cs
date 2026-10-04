using System.Text.Json.Serialization;
using Themia.Core.Office;

namespace Themia.Core.Model;

/// <summary>
/// [S-1460] The phase-flip cue: off, or one of the original's twelve ringtones. Read leniently, so
/// a hand-edited or unknown name is the default (<see cref="Chime"/>, the zero value) rather than a
/// state file that refuses to load.
/// </summary>
[JsonConverter(typeof(LenientEnumConverter<AlarmSound>))]
public enum AlarmSound
{
    Chime = 0,
    Marimba = 1,
    Crystal = 2,
    Pluck = 3,
    Droplet = 4,
    Birdsong = 5,
    Zen = 6,
    Bell = 7,
    Gong = 8,
    Arcade = 9,
    Digital = 10,
    Buzzer = 11,
    Off = 12,
}

/// <summary>A focus/break timer. [S-01] Run state is deliberately not persisted.</summary>
public sealed class PomodoroConfig : WidgetConfig
{
    /// <summary>[S-1460] The original's default level.</summary>
    public const int DefaultVolume = 70;

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Pomodoro;

    /// <summary>Length of a focus phase, 1 to 180 minutes.</summary>
    public int WorkMinutes { get; set; } = 25;

    /// <summary>Length of a break phase, 1 to 180 minutes.</summary>
    public int BreakMinutes { get; set; } = 5;

    /// <summary>[S-1460] The cue a phase flip sounds; <see cref="AlarmSound.Off"/> for none.</summary>
    public AlarmSound Sound { get; set; } = AlarmSound.Chime;

    /// <summary>[S-1460] The cue's level, 0 to 100.</summary>
    public int Volume { get; set; } = DefaultVolume;

    [JsonIgnore]
    public int EffectiveWorkMinutes => Math.Clamp(WorkMinutes, 1, 180);

    [JsonIgnore]
    public int EffectiveBreakMinutes => Math.Clamp(BreakMinutes, 1, 180);

    [JsonIgnore]
    public int EffectiveVolume => Math.Clamp(Volume, 0, 100);

    /// <summary>Bring the volume and the sound back inside their sets.</summary>
    public override IReadOnlyList<string> Repair()
    {
        var repairs = new List<string>();

        if (Volume != EffectiveVolume)
        {
            repairs.Add($"Pomodoro volume {Volume} was out of range");
            Volume = EffectiveVolume;
        }

        if (!Enum.IsDefined(Sound))
        {
            repairs.Add("Pomodoro widget had an unknown sound");
            Sound = AlarmSound.Chime;
        }

        return repairs;
    }

    public override WidgetConfig Clone() => new PomodoroConfig
    {
        WorkMinutes = WorkMinutes,
        BreakMinutes = BreakMinutes,
        Sound = Sound,
        Volume = Volume,
    };
}
