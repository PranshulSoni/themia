using Microsoft.UI.Xaml;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

/// <summary>
/// Settings rows for the power tile. Each control writes straight to the live config and saves, so
/// the widget behind the window follows along.
/// </summary>
internal static partial class WidgetSettings
{
    public static void Power(List<FrameworkElement> rows, PowerConfig config, Action save)
    {
        rows.Add(FluentLayout.Header("Power"));

        rows.Add(FluentLayout.Row(Glyphs.Pomodoro, "Show the power plan",
            "The friendly name of the active scheme",
            FluentLayout.Toggle(config.ShowPlan, value => { config.ShowPlan = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Battery, "Show battery saver",
            "Hidden on a PC with no battery either way",
            FluentLayout.Toggle(config.ShowBatterySaver, value => { config.ShowBatterySaver = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Timer, "Show time remaining",
            "Windows only estimates this while on battery",
            FluentLayout.Toggle(config.ShowTimeRemaining, value => { config.ShowTimeRemaining = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Motion, "Every",
            "Seconds between reads",
            FluentLayout.Slider(config.IntervalSeconds, 5, 300, 5, "s",
                value => { config.IntervalSeconds = (int)Math.Round(value); save(); })));
    }
}
