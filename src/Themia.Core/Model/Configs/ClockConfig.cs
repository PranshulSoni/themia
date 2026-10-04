using System.Text.Json.Serialization;

namespace Themia.Core.Model;

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
