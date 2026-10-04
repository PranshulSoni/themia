using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>
/// The Apps widget's settings rows ([S-1524]): how the list looks, how it opens, then what is hidden
/// — the apps hidden one by one, then a switch per folder (original S-25's "Filter" section).
/// <para>
/// Nothing here names an application to show. The list is read off the Start menu each time, so an
/// app installed or removed while the widget is on the desktop appears or goes without anybody
/// touching this page. The one list of names on it is the reverse: what the user asked not to see.
/// The folder switches come from a scan of the Start menu made when the page opens, off the UI
/// thread, plus any hidden folder the scan no longer finds (so it can still be shown again).
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    private static readonly (AppsViewMode, string)[] AppsViews =
    [
        (AppsViewMode.Grid, "Tiles"),
        (AppsViewMode.List, "List"),
        (AppsViewMode.Compact, "Compact list"),
    ];

    public static void Apps(List<FrameworkElement> rows, AppsConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Display"));

        rows.Add(FluentLayout.Row(Glyphs.GridLayout, "View",
            "Tiles with the name under the icon, rows, or compact rows with names only",
            FluentLayout.Choice(AppsViews, config.View, value => { config.View = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Folder, "Group by Start menu folder",
            "Tiles and rows, under a heading per folder. Compact rows show the folder beside the name instead",
            FluentLayout.Toggle(config.GroupByFolder, value => { config.GroupByFolder = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Search, "Show the filter box",
            "Where the widget is tall enough for it",
            FluentLayout.Toggle(config.ShowFilter, value => { config.ShowFilter = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Apps", "How many the list holds",
            FluentLayout.Slider(config.EffectiveMaxCount, 10, 1000, 10, string.Empty,
                value => { config.MaxCount = (int)Math.Round(value); save(); })));

        rows.Add(FluentLayout.Header("Behaviour"));

        // Double by default, and this says plainly why: the widget sits under every other window,
        // on the path the pointer takes across the desktop.
        rows.Add(FluentLayout.Row(Glyphs.Open, "Open with",
            "A double click is harder to do by accident on the desktop",
            FluentLayout.Choice(ShortcutClicks, config.ClickMode, value => { config.ClickMode = value; save(); })));

        // The hidden apps first: a short list, above a folder list that can run to dozens of rows.
        rows.Add(FluentLayout.Header("Hidden apps"));

        var hidden = new StackPanel { Spacing = Fluent.Space1 };
        rows.Add(hidden);
        FillHiddenApps(hidden, config, save);

        rows.Add(FluentLayout.Header("Folders"));

        var folders = new StackPanel { Spacing = Fluent.Space1 };
        folders.Children.Add(FluentLayout.Note("Reading the Start menu…"));
        rows.Add(folders);

        _ = FillFoldersAsync(folders, config, save);
    }

    /// <summary>
    /// [S-1524] One switch per Start-menu folder, on when its apps are listed. The scan runs on the
    /// thread pool; the page is drawn at once and the switches arrive when it finishes.
    /// </summary>
    private static async Task FillFoldersAsync(StackPanel host, AppsConfig config, Action save)
    {
        IReadOnlyList<InstalledApp> apps;

        try
        {
            apps = await Task.Run(() => InstalledApps.Read(InstalledAppRules.MaxEntries, CancellationToken.None)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warn("apps-settings", "could not read the Start menu for the folder list", ex);
            host.Children.Clear();
            host.Children.Add(FluentLayout.Note("The Start menu could not be read."));
            return;
        }

        var categories = InstalledAppRules.Categories(apps);

        // A folder hidden earlier and gone from the menu since still gets its switch, so it can be
        // taken off the list.
        foreach (var gone in config.HiddenCategories)
        {
            if (!categories.Contains(gone, StringComparer.OrdinalIgnoreCase))
            {
                categories.Add(gone);
            }
        }

        host.Children.Clear();

        if (categories.Count == 0)
        {
            host.Children.Add(FluentLayout.Note("No folders found in your Start menu."));
            return;
        }

        foreach (var category in categories)
        {
            var count = apps.Count(app => app.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
            var shown = !config.HiddenCategories.Contains(category, StringComparer.OrdinalIgnoreCase);
            var name = category;

            host.Children.Add(FluentLayout.Row(Glyphs.Folder, InstalledAppRules.CategoryTitle(category),
                category.Length == 0
                    ? $"Apps at the top of the Start menu · {count}"
                    : count == 1 ? "1 app" : $"{count} apps",
                FluentLayout.Toggle(shown, on =>
                {
                    config.HiddenCategories.RemoveAll(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));

                    if (!on)
                    {
                        config.HiddenCategories.Add(name);
                    }

                    save();
                })));
        }
    }

    /// <summary>[S-1524] The apps hidden one by one, each with a button that shows it again.</summary>
    private static void FillHiddenApps(StackPanel host, AppsConfig config, Action save)
    {
        host.Children.Clear();

        if (config.HiddenApps.Count == 0)
        {
            host.Children.Add(FluentLayout.Note("None. Right-click an app in the widget and choose Hide from list."));
            return;
        }

        foreach (var path in config.HiddenApps.ToList())
        {
            var show = new Button { Content = "Show again" };
            show.Click += (_, _) =>
            {
                config.HiddenApps.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
                save();
                FillHiddenApps(host, config, save);
            };

            host.Children.Add(FluentLayout.Row(Glyphs.Hide, Path.GetFileNameWithoutExtension(path), path, show));
        }
    }
}
