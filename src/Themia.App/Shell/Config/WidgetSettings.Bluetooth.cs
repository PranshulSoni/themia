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

        rows.Add(FluentLayout.Header("Bluetooth"));

        rows.Add(FluentLayout.Row(Glyphs.Link, "Connected only",
            "Hide everything that is paired but not connected",
            FluentLayout.Toggle(config.ConnectedOnly, value => { config.ConnectedOnly = value; save(); })));

        // Said out loud, because it is the setting that decides how long a refresh takes: the GATT
        // read opens a connection to each device and is by far the slowest part of the pass.
        rows.Add(FluentLayout.Row(Glyphs.Battery, "Read device battery",
            "Opens a connection to each device; slows the refresh",
            FluentLayout.Toggle(config.ShowBattery, value => { config.ShowBattery = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Devices",
            "How many the list holds; a short widget shows fewer",
            FluentLayout.Slider(config.EffectiveMaxCount, 1, 20, 1, string.Empty,
                value => { config.MaxCount = (int)Math.Round(value); save(); })));

        FluentLayout.RefreshSeconds(rows, "the devices are enumerated", config.IntervalSeconds, 5, 300, 5,
            value => { config.IntervalSeconds = value; save(); });
    }
}
