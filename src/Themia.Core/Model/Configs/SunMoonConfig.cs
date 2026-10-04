using System.Text.Json.Serialization;

namespace Themia.Core.Model;

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
