using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>
/// The widget catalogue. A <b>closed set</b>, so it is an enum rather than a string: an unknown
/// value coming out of persisted JSON is a corrupt-data signal, not a plugin.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WidgetKind>))]
public enum WidgetKind
{
    Clock = 0,

    /// <summary>Processor load across all cores.</summary>
    Cpu = 1,

    /// <summary>Physical memory in use.</summary>
    Memory = 2,

    /// <summary>Space used on one drive.</summary>
    Disk = 3,

    /// <summary>Charge level and whether it is on mains.</summary>
    Battery = 4,

    /// <summary>Recent mail, read from the hearth hub.</summary>
    Email = 5,

    /// <summary>The busiest running processes, ranked.</summary>
    Processes = 6,

    /// <summary>Bytes per second across the active adapters.</summary>
    Network = 7,

    /// <summary>Round-trip time to a host.</summary>
    Ping = 8,

    /// <summary>People, read from the hearth hub.</summary>
    Contacts = 9,

    /// <summary>Upcoming events, read from the hearth hub.</summary>
    Calendar = 10,

    /// <summary>Open tasks, read from the hearth hub.</summary>
    Tasks = 11,

    /// <summary>A free-text note kept on this machine.</summary>
    Notes = 12,

    /// <summary>Time left until one or more dated targets.</summary>
    Countdown = 13,

    /// <summary>A focus/break timer.</summary>
    Pomodoro = 14,

    /// <summary>An arithmetic scratchpad with a short history.</summary>
    Calculator = 15,

    /// <summary>Sunrise, sunset and the moon phase, computed offline.</summary>
    SunMoon = 16,
}

/// <summary>Which column a process list is ranked on.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProcessSort>))]
public enum ProcessSort
{
    Cpu = 0,
    Memory = 1,
}

/// <summary>How a countdown renders the time it has left. [C-11]</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CountdownFormat>))]
public enum CountdownFormat
{
    /// <summary>Pick the coarsest unit that still says something useful.</summary>
    Auto = 0,

    /// <summary>Whole days only.</summary>
    Days = 1,

    /// <summary>Days, hours and minutes.</summary>
    DaysHoursMinutes = 2,

    /// <summary>Days, hours, minutes and seconds — the only format that needs a one-second tick.</summary>
    DaysHoursMinutesSeconds = 3,
}

/// <summary>How much of the sun's path the Sun &amp; Moon widget draws. [S-10]</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SunPathMode>))]
public enum SunPathMode
{
    /// <summary>The curve with its time labels.</summary>
    Numbers = 0,

    /// <summary>The curve alone.</summary>
    Plain = 1,

    /// <summary>No curve; the event rows carry everything.</summary>
    Hidden = 2,
}

/// <summary>How the clock renders its face.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ClockFace>))]
public enum ClockFace
{
    Digital = 0,
    Analog = 1,
}

/// <summary>Light/dark selection. <c>System</c> follows the Windows app theme.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AppTheme>))]
public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>Surface a widget card paints behind its content.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WidgetSurface>))]
public enum WidgetSurface
{
    /// <summary>Fluent card fill — reads over any wallpaper.</summary>
    Card = 0,

    /// <summary>
    /// A tinted, partly see-through card: the wallpaper shows through, unblurred.
    /// <para>
    /// Not acrylic. Real acrylic samples and blurs what is behind the window, and a backdrop is a
    /// property of a <i>window</i>, not of an element — so per-widget acrylic would mean one HWND,
    /// composition target and render thread per widget. That is precisely the cost this port avoids,
    /// so the whole view shares one transparent window and this surface is an honest tint instead.
    /// </para>
    /// </summary>
    Translucent = 1,

    /// <summary>[S-32] No fill at all: content sits directly on the wallpaper.</summary>
    Transparent = 2,
}

/// <summary>How a widget card's border is drawn.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WidgetBorderStyle>))]
public enum WidgetBorderStyle
{
    None = 0,
    Solid = 1,
    Dashed = 2,
    Dotted = 3,
}

/// <summary>
/// The one-shot animation widgets play when a view appears. A closed set, because each value picks
/// a different piece of code rather than a parameter.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<IntroAnimation>))]
public enum IntroAnimation
{
    None = 0,
    Fade = 1,
    Scale = 2,
    Slide = 3,
}
