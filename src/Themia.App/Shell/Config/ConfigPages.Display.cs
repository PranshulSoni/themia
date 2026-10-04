using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>One display's page.</summary>
internal static partial class ConfigPages
{
    private static FrameworkElement BuildDisplay(AppHost host, string monitorId)
    {
        var surface = host.Surfaces.FirstOrDefault(s => s.Monitor.DeviceName == monitorId);
        if (surface is null)
        {
            return Page("Display", [Note("This display is no longer connected.")]);
        }

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

        var area = surface.Monitor.WorkArea;

        var rows = new List<FrameworkElement>
        {
            FluentLayout.Row(Glyphs.View, "Shows", "Which view this display renders", combo),
            FluentLayout.Row(Glyphs.Display, "Work area", $"{area.Width} x {area.Height} physical pixels",
                new TextBlock { Text = surface.Monitor.IsPrimary ? "Primary" : string.Empty }),
        };

        return Page(monitorId, rows);
    }
}
