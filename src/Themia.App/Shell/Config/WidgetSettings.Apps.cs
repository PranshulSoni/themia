using Microsoft.UI.Xaml;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

/// <summary>
/// The Apps widget's settings rows.
/// <para>
/// There is nothing here that names an application. The list is read off the Start menu each time,
/// so an app installed or removed while the widget is on the desktop appears or goes without
/// anybody touching this page; these rows only decide how the list is shown and how it is opened.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    public static void Apps(List<FrameworkElement> rows, AppsConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Apps"));

        rows.Add(FluentLayout.Row(Glyphs.Search, "Filter box",
            "Hidden automatically when the widget is too short for it",
            FluentLayout.Toggle(config.ShowFilter, value => { config.ShowFilter = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Folder, "Show Start menu folder",
            "Hidden automatically when the widget is too narrow",
            FluentLayout.Toggle(config.ShowCategory, value => { config.ShowCategory = value; save(); })));

        // Off by default, and this says plainly what turning it on costs: the widget sits under
        // every other window, on the path the pointer takes across the desktop.
        rows.Add(FluentLayout.Row(Glyphs.Open, "Open on one click",
            "Off, an app opens on a double-click. A single click is easy to do by accident on the desktop",
            FluentLayout.Toggle(config.OpenOnSingleClick, value => { config.OpenOnSingleClick = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Apps", "How many the list holds",
            FluentLayout.Slider(config.EffectiveMaxCount, 10, 1000, 10, string.Empty,
                value => { config.MaxCount = (int)Math.Round(value); save(); })));
    }
}
