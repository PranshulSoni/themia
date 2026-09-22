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
