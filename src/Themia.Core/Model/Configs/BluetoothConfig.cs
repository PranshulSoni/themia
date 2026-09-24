using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>
/// Paired Bluetooth devices, their connection state, and their battery where they report one.
/// </summary>
public sealed class BluetoothConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Bluetooth;

    /// <summary>
    /// Read each connected device's battery over GATT.
    /// <para>
    /// Off-by-choice is a real option: the read opens a BLE connection per device and is by far the
    /// slowest part of a refresh, so a desktop that only wants to know what is connected should not
    /// have to pay for it.
    /// </para>
    /// </summary>
    public bool ShowBattery { get; set; } = true;

    /// <summary>List only what is connected, rather than everything ever paired.</summary>
    public bool ConnectedOnly { get; set; }

    /// <summary>How many rows to show, 1 to 20.</summary>
    public int MaxCount { get; set; } = 6;

    /// <summary>
    /// Seconds between enumerations, 5 to 300. Slow by default: enumerating the radios and reading
    /// GATT batteries is seconds of work, not microseconds, and a headset does not connect twice a
    /// minute.
    /// </summary>
    public int IntervalSeconds { get; set; } = 30;

    /// <summary>Clamped on read: a persisted zero would draw an empty list.</summary>
    [JsonIgnore]
    public int EffectiveMaxCount => Math.Clamp(MaxCount, 1, 20);

    /// <summary>Clamped on read, so a persisted zero cannot turn the shared ticker into a spin.</summary>
    [JsonIgnore]
    public TimeSpan TickInterval => TimeSpan.FromSeconds(Math.Clamp(IntervalSeconds, 5, 300));

    public override WidgetConfig Clone() => new BluetoothConfig
    {
        ShowBattery = ShowBattery,
        ConnectedOnly = ConnectedOnly,
        MaxCount = MaxCount,
        IntervalSeconds = IntervalSeconds,
    };
}
