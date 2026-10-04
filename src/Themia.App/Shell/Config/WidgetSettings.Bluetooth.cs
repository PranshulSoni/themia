using Microsoft.UI.Xaml;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

/// <summary>
/// Settings rows for the Bluetooth tile. Each control writes straight to the live config and saves,
/// so the widget behind the window follows along.
/// </summary>
internal static partial class WidgetSettings
{
    public static void Bluetooth(List<FrameworkElement> rows, BluetoothConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Devices"));

        rows.Add(FluentLayout.Row(Glyphs.Link, "Connected only",
            "Hide everything that is paired but not connected",
            FluentLayout.Toggle(config.ConnectedOnly, value => { config.ConnectedOnly = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Most devices",
            "A ceiling: the tile shows as many as fit and scrolls to the rest",
            FluentLayout.Slider(config.EffectiveMaxCount, 1, 30, 1, string.Empty,
                value => { config.MaxCount = (int)Math.Round(value); save(); })));

        // [S-1257] The original's per-device metrics. The name is always shown.
        rows.Add(FluentLayout.Header("Show"));

        rows.Add(FluentLayout.Row(Glyphs.Bluetooth, "Device icon",
            "What kind of device it is",
            FluentLayout.Toggle(config.ShowIcon, value => { config.ShowIcon = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Link, "Connection",
            "The dot, and Connected or Paired where there is room",
            FluentLayout.Toggle(config.ShowConnection, value => { config.ShowConnection = value; save(); })));

        // Said out loud, because it is the setting that decides how long a refresh takes: the GATT
        // read opens a connection to each device and is by far the slowest part of the pass.
        rows.Add(FluentLayout.Row(Glyphs.Battery, "Battery",
            "Opens a connection to each connected device; slows the refresh",
            FluentLayout.Toggle(config.ShowBattery, value => { config.ShowBattery = value; save(); })));

        // [S-1256] Also said out loud: it costs a short radio scan on every refresh.
        rows.Add(FluentLayout.Row(Glyphs.Signal, "Signal strength",
            "Listens for Bluetooth LE devices for 3 seconds each refresh; most other devices never report it",
            FluentLayout.Toggle(config.ShowSignal, value => { config.ShowSignal = value; save(); })));

        FluentLayout.RefreshSeconds(rows, "the devices are enumerated", config.IntervalSeconds, 5, 300, 5,
            value => { config.IntervalSeconds = value; save(); });
    }
}
