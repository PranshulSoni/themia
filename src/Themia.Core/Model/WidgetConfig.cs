using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>
/// Per-instance widget settings. A discriminated union rather than a property bag: the
/// discriminator is written into the JSON so a config can never be silently read as the wrong
/// widget's shape.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(ClockConfig), typeDiscriminator: "clock")]
public abstract class WidgetConfig
{
    /// <summary>The kind this config belongs to. Used to detect a config/kind mismatch on load.</summary>
    [JsonIgnore]
    public abstract WidgetKind Kind { get; }

    /// <summary>Default config for a kind. Never returns null, so a widget always has settings.</summary>
    public static WidgetConfig CreateDefault(WidgetKind kind) => kind switch
    {
        WidgetKind.Clock => new ClockConfig(),
        _ => new ClockConfig(),
    };

    public abstract WidgetConfig Clone();
}

public sealed class ClockConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Clock;

    public ClockFace Face { get; set; } = ClockFace.Digital;

    public bool ShowSeconds { get; set; }

    public bool Use24Hour { get; set; } = true;

    public bool ShowDate { get; set; } = true;

    /// <summary>
    /// IANA/Windows time-zone id to display, or null for the machine's local zone. Validated on
    /// read: an id that no longer resolves falls back to local rather than throwing at render time.
    /// </summary>
    public string? TimeZoneId { get; set; }

    public override WidgetConfig Clone() => new ClockConfig
    {
        Face = Face,
        ShowSeconds = ShowSeconds,
        Use24Hour = Use24Hour,
        ShowDate = ShowDate,
        TimeZoneId = TimeZoneId,
    };

    /// <summary>
    /// Resolve <see cref="TimeZoneId"/> to a real zone. An unknown or malformed id degrades to the
    /// local zone; it never throws, because this runs on every tick.
    /// </summary>
    public TimeZoneInfo ResolveTimeZone()
    {
        if (string.IsNullOrWhiteSpace(TimeZoneId))
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    /// <summary>The tick cadence this config actually needs. A clock without seconds wakes once a minute.</summary>
    [JsonIgnore]
    public TimeSpan TickInterval => ShowSeconds || Face == ClockFace.Analog
        ? TimeSpan.FromSeconds(1)
        : TimeSpan.FromMinutes(1);
}
