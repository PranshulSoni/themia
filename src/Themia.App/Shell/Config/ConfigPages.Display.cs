using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.Core.Model;
using Themia.Core.Storage;

namespace Themia.App.Shell.Config;

/// <summary>
/// [S-853] One display's page: whether Themia is shown there, which view it shows, how much it
/// magnifies widgets, and what Windows reports about it.
/// </summary>
internal static partial class ConfigPages
{
    private static FrameworkElement BuildDisplay(AppHost host, string monitorId)
    {
        if (host.FindDisplay(monitorId) is not { } display)
        {
            return Page("Display", [Note("This display is no longer connected.")]);
        }

        var state = host.State;
        var enabled = !state.DisabledMonitors.Contains(monitorId);
        var connected = host.Displays.Select(d => d.Info).ToList();

        // [S-845] The last display Themia is on cannot be switched off: with no surface there is no
        // edit mode and no way back here.
        var canToggle = !enabled || DisplayPlan.CanDisable(connected, state.DisabledMonitors, monitorId);
        ToggleSwitch? show = null;
        var reverting = false;
        show = FluentLayout.Toggle(enabled, on =>
        {
            // Refused (logged by the host): put the switch back rather than show a state that isn't so.
            if (!reverting && !host.SetDisplayEnabled(monitorId, on) && show is not null)
            {
                reverting = true;
                show.IsOn = !on;
                reverting = false;
            }
        });
        show.IsEnabled = canToggle;

        var rows = new List<FrameworkElement>
        {
            FluentLayout.Row(Glyphs.Display, "Show Themia on this display",
                canToggle
                    ? "Off hides every widget here and lets the whole screen through to the desktop"
                    : "Themia has to stay on at least one display",
                show, "enable", "disable", "hide", "monitor"),

            FluentLayout.Row(Glyphs.View, "Shows", "Which view this display renders", ViewChoice(host, monitorId), "view"),

            FluentLayout.Row(Glyphs.Scale, "Widget scale",
                "Makes everything inside the widgets on this display bigger or smaller; where they sit and their size stay the same",
                FluentLayout.Slider(
                    (state.MonitorScales.TryGetValue(monitorId, out var scale) ? scale : 1.0) * 100,
                    AppStateStore.MinMonitorScale * 100,
                    AppStateStore.MaxMonitorScale * 100,
                    5,
                    "%",
                    value => host.SetDisplayScale(monitorId, value / 100)),
                "zoom", "size", "text size", "magnify"),

            FluentLayout.Row(Glyphs.Display, "Resolution",
                $"Widgets are laid out over the whole screen; the taskbar leaves {display.WorkArea.Width} × {display.WorkArea.Height} for placing new ones",
                new TextBlock
                {
                    Text = $"{display.Bounds.Width} × {display.Bounds.Height}",
                    VerticalAlignment = VerticalAlignment.Center,
                },
                "work area", "taskbar"),
        };

        return Page(display.ShortName, rows, display.Describe());
    }

    /// <summary>The view drop-down: an explicit view, or the active view.</summary>
    private static ComboBox ViewChoice(AppHost host, string monitorId)
    {
        var options = new List<(string Value, string Label)> { (string.Empty, "Follow the active view") };
        options.AddRange(host.State.Views.Select(v => (v.Id, v.Name)));

        var selected = host.State.MonitorViews.TryGetValue(monitorId, out var assigned) ? assigned : string.Empty;

        var combo = new ComboBox { MinWidth = 220 };
        foreach (var (value, label) in options)
        {
            combo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        }

        combo.SelectedIndex = Math.Max(0, options.FindIndex(o => o.Value == selected));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not ComboBoxItem { Tag: string id })
            {
                return;
            }

            host.Mutate(s =>
            {
                if (string.IsNullOrEmpty(id))
                {
                    s.MonitorViews.Remove(monitorId);
                }
                else
                {
                    s.MonitorViews[monitorId] = id;
                }
            });
        };

        return combo;
    }
}
