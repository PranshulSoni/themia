using Microsoft.UI.Xaml;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

internal static partial class WidgetSettings
{
    public static void Battery(List<FrameworkElement> rows, BatteryConfig battery, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(battery);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Battery"));

        rows.Add(FluentLayout.Row(Glyphs.Timer, "Show time remaining",
            "Windows only estimates this while on battery",
            FluentLayout.Toggle(battery.ShowTimeRemaining, value => { battery.ShowTimeRemaining = value; save(); })));

        MeterRows(rows, battery, save, "the charge is read");
    }
}
