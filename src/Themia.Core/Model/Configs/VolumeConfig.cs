using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>
/// The master output level, and whether this tile is allowed to move it.
/// <para>
/// Reading is the widget's job; setting is opt-out rather than opt-in, because a volume tile you
/// cannot drag is a strange thing to put on a desktop — but a surface that sits under every other
/// window can be scrolled or clicked across by accident, so the switch exists.
/// </para>
/// </summary>
public sealed class VolumeConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Volume;

    /// <summary>Let the slider and the mute button write back to the system mixer.</summary>
    public bool AllowControl { get; set; } = true;

    /// <summary>Show the slider at all. With it off the tile is a readout, whatever <see cref="AllowControl"/> says.</summary>
    public bool ShowSlider { get; set; } = true;

    /// <summary>
    /// Seconds between reads of the endpoint, 1 to 30.
    /// <para>
    /// A poll rather than an endpoint callback: registering one means handing Core Audio a COM
    /// object of ours to call back into, and the read itself is four local COM calls, so the cost of
    /// asking is lower than the cost of being called.
    /// </para>
    /// </summary>
    public int IntervalSeconds { get; set; } = 2;

    /// <summary>Clamped on read: a persisted zero would rebuild the ticker subscription every frame.</summary>
    [JsonIgnore]
    public TimeSpan TickInterval => TimeSpan.FromSeconds(Math.Clamp(IntervalSeconds, 1, 30));

    public override WidgetConfig Clone() => new VolumeConfig
    {
        AllowControl = AllowControl,
        ShowSlider = ShowSlider,
        IntervalSeconds = IntervalSeconds,
    };
}
