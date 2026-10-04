using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Interop;
using Themia.App.Services;
using Themia.Core.Diagnostics;
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
            AddAppPages(_nav.MenuItems, AppPageSlot.Main);

            _nav.MenuItems.Add(new NavigationViewItemSeparator());
            _nav.MenuItems.Add(Header("Views"));

            foreach (var view in _host.State.Views)
            {
                // A view's widgets hang under it, so the tree says where each widget actually lives
                // rather than listing them all flat.
                var node = Item(view.Name, Glyphs.View, new ConfigTarget.View(view.Id));

                foreach (var widget in view.Widgets)
                {
                    var definition = Widgets.WidgetCatalog.Definition(widget.Kind);
                    node.MenuItems.Add(Item(
                        string.IsNullOrWhiteSpace(widget.Label) ? definition.Label : widget.Label!,
                        definition.Glyph,
                        new ConfigTarget.Widget(widget.Id)));
                }

                node.IsExpanded = _target is ConfigTarget.Widget w && view.Widgets.Any(x => x.Id == w.WidgetId);
                _nav.MenuItems.Add(node);
            }

            if (_host.Surfaces.Count > 0)
            {
                _nav.MenuItems.Add(new NavigationViewItemSeparator());
                _nav.MenuItems.Add(Header("Displays"));

                foreach (var surface in _host.Surfaces)
                {
                    _nav.MenuItems.Add(Item(
                        surface.Monitor.ShortName,
                        Glyphs.Display,
                        new ConfigTarget.Display(surface.Monitor.DeviceName)));
                }
            }

            _nav.FooterMenuItems.Clear();

            // [S-493] About is the very last entry, pinned to the foot of the menu under everything
            // else, as in karpo and Windows Settings. Anything else that wants the footer goes
            // ABOVE it in ConfigPages.AppPages, never after it.
            AddAppPages(_nav.FooterMenuItems, AppPageSlot.Footer);

            SelectForTarget();
        }
        finally
        {
            _rebuilding = false;
        }
    }

    /// <summary>The registered app pages of one slot, in their registered order.</summary>
    private static void AddAppPages(IList<object> items, AppPageSlot slot)
    {
        foreach (var page in ConfigPages.AppPages)
        {
            if (page.Slot == slot)
            {
                items.Add(Item(page.Title, page.Glyph, new ConfigTarget.App(page.Page)));
            }
        }
    }

    private static NavigationViewItemHeader Header(string text) => new() { Content = text };

    private static NavigationViewItem Item(string text, string glyph, ConfigTarget target) => new()
    {
        Content = text,
        Icon = new FontIcon { Glyph = glyph },
        Tag = target,
    };

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
