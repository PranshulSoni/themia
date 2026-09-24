using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>A free-text note. The body lives in the config, so it travels with copy/paste and backup.</summary>
public sealed class NotesConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Notes;

    /// <summary>The note itself.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>[S-03] Base text size in device-independent pixels, before the global text scale.</summary>
    public double FontSizePx { get; set; } = 13;

    /// <summary>Wrap long lines rather than scrolling sideways.</summary>
    public bool WrapText { get; set; } = true;

    /// <summary>Clamped on read: a persisted 0 or a NaN would render nothing at all. [S-05]</summary>
    [JsonIgnore]
    public double EffectiveFontSizePx => double.IsFinite(FontSizePx) ? Math.Clamp(FontSizePx, 8, 48) : 13;

    public override WidgetConfig Clone() => new NotesConfig
    {
        Text = Text,
        FontSizePx = FontSizePx,
        WrapText = WrapText,
    };
}

/// <summary>One dated target in a <see cref="CountdownConfig"/>. [C-10]</summary>
public sealed class CountdownTimer
{
    /// <summary>Stable id, so reordering and editing cannot swap two timers' settings.</summary>
    public required string Id { get; set; }

    public string Label { get; set; } = string.Empty;

    /// <summary>The instant being counted to, always stored in UTC. [C-53]</summary>
    public DateTimeOffset Target { get; set; }

    public CountdownFormat Format { get; set; } = CountdownFormat.Auto;

    public static CountdownTimer Create(string label, DateTimeOffset target) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Label = label,
        Target = target,
    };

    public CountdownTimer Clone() => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Label = Label,
        Target = Target,
        Format = Format,
    };
}

/// <summary>An ordered list of countdowns.</summary>
public sealed class CountdownConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Countdown;

    public List<CountdownTimer> Timers { get; set; } = [];

    /// <summary>
    /// [C-20] One second only when a timer actually shows seconds; otherwise half a minute, which
    /// is enough for minute granularity and costs the process 120 fewer wakes a minute.
    /// </summary>
    [JsonIgnore]
    public TimeSpan TickInterval => Timers.Exists(t => t.Format == CountdownFormat.DaysHoursMinutesSeconds)
        ? TimeSpan.FromSeconds(1)
        : TimeSpan.FromSeconds(30);

    public override WidgetConfig Clone()
    {
        var copy = new CountdownConfig();
        foreach (var timer in Timers)
        {
            copy.Timers.Add(timer.Clone());
        }

        return copy;
    }
}

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

/// <summary>An arithmetic scratchpad. [S-09] History is in-memory only.</summary>
public sealed class CalculatorConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Calculator;

    /// <summary>[S-05] Digits after the point, 0 to 8.</summary>
    public int DecimalPlaces { get; set; } = 2;

    [JsonIgnore]
    public int EffectiveDecimalPlaces => Math.Clamp(DecimalPlaces, 0, 8);

    public override WidgetConfig Clone() => new CalculatorConfig { DecimalPlaces = DecimalPlaces };
}

/// <summary>Sunrise, sunset and the moon, computed offline from a fixed location. [S-01]</summary>
public sealed class SunMoonConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.SunMoon;

    /// <summary>Degrees north, -90 to 90. Defaults to Zurich, matching the original.</summary>
    public double Latitude { get; set; } = 47.37;

    /// <summary>Degrees east, -180 to 180.</summary>
    public double Longitude { get; set; } = 8.54;

    /// <summary>The place name shown in the header.</summary>
    public string Place { get; set; } = "Zurich";

    /// <summary>[S-10] How much of the sun's path is drawn.</summary>
    public SunPathMode PathMode { get; set; } = SunPathMode.Numbers;

    /// <summary>[S-10] Show the sunrise/sunset/moon rows.</summary>
    public bool ShowEvents { get; set; } = true;

    /// <summary>
    /// A sun position is only interesting at minute granularity; the rest of the readout changes
    /// twice a day.
    /// </summary>
    [JsonIgnore]
    public TimeSpan TickInterval => TimeSpan.FromMinutes(1);

    /// <summary>
    /// The coordinates actually used. Out-of-range values are clamped rather than rejected: the
    /// widget draws every tick and must never throw on a value that got onto disk.
    /// </summary>
    [JsonIgnore]
    public double EffectiveLatitude => double.IsFinite(Latitude) ? Math.Clamp(Latitude, -90, 90) : 47.37;

    [JsonIgnore]
    public double EffectiveLongitude => double.IsFinite(Longitude) ? Math.Clamp(Longitude, -180, 180) : 8.54;

    public override WidgetConfig Clone() => new SunMoonConfig
    {
        Latitude = Latitude,
        Longitude = Longitude,
        Place = Place,
        PathMode = PathMode,
        ShowEvents = ShowEvents,
    };
}
