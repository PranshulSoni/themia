using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Interop;
using Themia.App.Services;
using Themia.Core.Diagnostics;
using Themia.Core.Localization;
using Themia.Core.Model;
using Windows.Graphics;

namespace Themia.App.Shell.Config;

/// <summary>The navigation tree: app pages, views with their widgets, displays, and About at the foot.</summary>
internal sealed partial class ConfigWindow
{
    private void RebuildNavigation()
    {
        // Rebuilding assigns SelectedItem, which raises SelectionChanged; without this guard that
        // would navigate away from the page the caller just asked for.
        _rebuilding = true;
        try
        {
            _nav.MenuItems.Clear();

            _nav.MenuItems.Add(Header("Themia"));
            AddAppPages(_nav.MenuItems, AppPageSlot.Main, _host.State);

            _nav.MenuItems.Add(new NavigationViewItemSeparator());
            _nav.MenuItems.Add(Header(Str.App_Views.Text()));

            foreach (var view in _host.State.Views)
            {
                // A view's widgets hang under it, so the tree says where each widget actually lives
                // rather than listing them all flat.
                // [S-903] A view with an icon of its own shows it, in the colour icons' 20-DIP box so its
                // name lines up with its neighbours'; one without keeps the shared colour icon.
                var node = string.IsNullOrEmpty(view.Icon)
                    ? Item(view.Name, NavIcons.Create(NavIcon.ContentView), new ConfigTarget.View(view.Id))
                    : Item(view.Name, new FontIcon { Glyph = view.Icon, FontSize = NavIcons.Size - 2 }, new ConfigTarget.View(view.Id), wideBox: true);

                foreach (var widget in view.Widgets)
                {
                    var definition = Widgets.WidgetCatalog.Definition(widget.Kind);
                    node.MenuItems.Add(Item(
                        string.IsNullOrWhiteSpace(widget.Label) ? definition.Label : widget.Label!,
                        new FontIcon { Glyph = definition.Glyph },
                        new ConfigTarget.Widget(widget.Id)));
                }

                node.IsExpanded = _target is ConfigTarget.Widget w && view.Widgets.Any(x => x.Id == w.WidgetId);
                _nav.MenuItems.Add(node);
            }

            // [S-853] Every connected display, including one Themia is switched off on: its page is
            // where it is switched back on.
            if (_host.Displays.Count > 0)
            {
                _nav.MenuItems.Add(new NavigationViewItemSeparator());
                _nav.MenuItems.Add(Header(Str.App_Displays.Text()));

                foreach (var display in _host.Displays)
                {
                    _nav.MenuItems.Add(Item(
                        display.ShortName,
                        NavIcons.Create(NavIcon.Laptop),
                        new ConfigTarget.Display(display.Id)));
                }
            }

            _nav.FooterMenuItems.Clear();

            // [S-493] About is the very last entry, pinned to the foot of the menu under everything
            // else, as in karpo and Windows Settings. Anything else that wants the footer goes
            // ABOVE it in ConfigPages.AppPages, never after it.
            AddAppPages(_nav.FooterMenuItems, AppPageSlot.Footer, _host.State);

            SelectForTarget();
        }
        finally
        {
            _rebuilding = false;
        }
    }

    /// <summary>The registered app pages of one slot that are available now, in their registered order.</summary>
    private static void AddAppPages(IList<object> items, AppPageSlot slot, AppState state)
    {
        foreach (var page in ConfigPages.AppPages)
        {
            if (page.Slot == slot && page.IsAvailable(state))
            {
                items.Add(Item(page.Title, NavIcons.Create(page.Icon), new ConfigTarget.App(page.Page)));
            }
        }
    }

    private static NavigationViewItemHeader Header(string text) => new() { Content = text };

    /// <summary>
    /// [S-1602] A menu entry. Pages, views and displays carry full-colour icons, as the categories
    /// of Windows Settings do; a widget nested under its view keeps its monochrome kind glyph, the
    /// same one its card header and the picker show, so it reads as part of the view above it.
    /// </summary>
    private static NavigationViewItem Item(string text, IconElement icon, ConfigTarget target, bool wideBox = false)
    {
        var item = new NavigationViewItem { Content = text, Icon = icon, Tag = target };
        NavIcons.SizeIconBox(item, coloured: wideBox || icon is not FontIcon);
        return item;
    }

    private void SelectForTarget()
    {
        // A widget's page opens its view in the pane too, so the selection pill is visible on the
        // widget itself rather than hidden inside a folded view.
        foreach (var item in _nav.MenuItems)
        {
            if (item is NavigationViewItem node && node.MenuItems.OfType<NavigationViewItem>().Any(c => c.Tag is ConfigTarget t && t == _target))
            {
                node.IsExpanded = true;
            }
        }

        foreach (var candidate in Walk(_nav.MenuItems).Concat(Walk(_nav.FooterMenuItems)))
        {
            if (candidate.Tag is ConfigTarget tag && tag == _target)
            {
                _nav.SelectedItem = candidate;
                return;
            }
        }
    }

    private static IEnumerable<NavigationViewItem> Walk(IList<object> items)
    {
        foreach (var item in items)
        {
            if (item is not NavigationViewItem node)
            {
                continue;
            }

            yield return node;

            foreach (var child in Walk(node.MenuItems))
            {
                yield return child;
            }
        }
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // Also ignored when it is the page already showing: selecting the item for a page that was
        // opened some other way (back, search, a link) raises this too, and must not build it twice.
        if (_rebuilding || args.SelectedItem is not NavigationViewItem { Tag: ConfigTarget target } || target == _target)
        {
            return;
        }

        Show(target, record: true);
    }
}
