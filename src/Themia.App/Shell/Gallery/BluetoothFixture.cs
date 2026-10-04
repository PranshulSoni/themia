#if DEBUG
using Themia.App.Services;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-1258] The Bluetooth list's gallery sample: a desk's worth of paired devices — some connected,
/// some reporting battery and signal — so the gallery shows the populated ladder, including the
/// long list a large tile must fill. Read only by a preview card; Debug builds only.
/// </summary>
internal static class BluetoothFixture
{
    public static BluetoothSnapshot? Snapshot { get; private set; }

    public static void Install(bool populated)
        => Snapshot = populated ? new BluetoothSnapshot(BluetoothStatus.Ok, Devices()) : null;

    public static BluetoothConfig Config() => new() { ShowSignal = true };

    private static List<BluetoothDeviceEntry> Devices() =>
    [
        Device("Surface Headphones 2", connected: true, battery: 80, signal: -58),
        Device("MX Master 3S Mouse", connected: true, battery: 45, signal: -64),
        Device("Keychron K2 Keyboard", connected: true, battery: 15, signal: -71),
        Device("Pixel 8", connected: false, battery: null, signal: null),
        Device("Xbox Wireless Controller", connected: false, battery: null, signal: null),
        Device("Galaxy Watch6", connected: false, battery: null, signal: -88),
        Device("Soundcore Motion Speaker", connected: false, battery: null, signal: null),
        Device("Surface Pen", connected: false, battery: null, signal: null),
        Device("AirPods Pro", connected: false, battery: null, signal: null),
        Device("Logitech Pebble Mouse", connected: false, battery: null, signal: null),
        Device("Car Audio", connected: false, battery: null, signal: null),
        Device("Fitbit Charge 6", connected: false, battery: null, signal: null),
        Device("Office Speakerphone", connected: false, battery: null, signal: null),
        Device("iPad Keyboard", connected: false, battery: null, signal: null),
    ];

    private static BluetoothDeviceEntry Device(string name, bool connected, int? battery, int? signal)
        => new(name, $"fixture:{name}", connected, BluetoothRules.Classify(name), battery, signal);
}
#endif
