using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Widgets;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>One view's page: its name and icon, its place in the order, its widgets, copying and deleting it.</summary>
internal static partial class ConfigPages
{
    private static FrameworkElement BuildView(AppHost host, string viewId, Action<ConfigTarget> navigate)
    {
        var view = host.State.FindView(viewId);
        if (view is null)
        {
            return Page("View", [Note("This view no longer exists.")]);
        }

        var name = new TextBox { Text = view.Name, MinWidth = 220 };
        name.LostFocus += (_, _) => host.Mutate(s =>
        {
            if (s.FindView(viewId) is { } target && !string.IsNullOrWhiteSpace(name.Text))
            {
                target.Name = name.Text.Trim();
            }
        });

        // [S-903] One glyph from the shared icon grid, or the name's initials — which is what a view
        // without an icon shows in the view bar, so the "default" cell shows exactly that.
        var icon = IconChoice.Create(
            view.Icon,
            Glyphs.View,
            glyph => host.Mutate(s =>
            {
                if (s.FindView(viewId) is { } target)
                {
                    target.Icon = glyph;
                }
            }),
            defaultText: ViewSwitcherRules.Initials(view.Name),
            defaultName: "Initials");

        // On the screen the button was pressed on: every display shows a view of its own, so "show
        // this view" has to say where.
        var activate = new Button { Content = "Show on this screen" };
        activate.Click += (_, _) =>
        {
            if ((host.SurfaceUnderCursor() ?? host.Surfaces.FirstOrDefault()) is { } surface)
            {
                host.ShowView(surface.Monitor.Id, viewId);
            }
        };

        var duplicate = new Button { Content = "Duplicate" };
        duplicate.Click += (_, _) =>
        {
            // [S-900] The copy shows where the pointer is, as the toolbar's and the bar's do.
            var surface = host.SurfaceUnderCursor() ?? host.Surfaces.FirstOrDefault();
            host.DuplicateView(viewId, surface?.Monitor.Id);
        };

        var delete = new Button { Content = "Delete view", IsEnabled = host.State.Views.Count > 1 };
        delete.Click += (_, _) =>
        {
            host.DeleteView(viewId);
            navigate(new ConfigTarget.App());
        };

        var rows = new List<FrameworkElement>
        {
            FluentLayout.Row(Glyphs.Rename, "Name", null, name),
            FluentLayout.Row(Glyphs.View, "Icon", "Shown in the view bar, the view switcher and this window; without one, the initials are", icon, "glyph", "symbol"),
            FluentLayout.Row(Glyphs.View, "Active view",
                host.State.ActiveView().Id == viewId ? "This view is showing now" : "Switch the desktop to it",
                activate),
            OrderRow(host, viewId),
            FluentLayout.Row(Glyphs.Copy, "Duplicate this view", "A copy with every widget in it, shown on this screen", duplicate, "copy", "clone"),
            FluentLayout.Header("Widgets"),
        };

        if (view.Widgets.Count == 0)
        {
            rows.Add(Note("No widgets in this view yet."));
        }

        foreach (var widget in view.Widgets)
        {
            var definition = WidgetCatalog.Definition(widget.Kind);
            var id = widget.Id;

            // [S-1540] A card that drills into the widget's own page, with Settings' chevron.
            rows.Add(FluentLayout.NavRow(
                definition.Glyph,
                string.IsNullOrWhiteSpace(widget.Label) ? definition.Label : widget.Label!,

                // The kind under a renamed widget; the kind's own line under one that is not.
                string.IsNullOrWhiteSpace(widget.Label) ? definition.Description : definition.Label,
                () => navigate(new ConfigTarget.Widget(id))));
        }

        // [S-420] Widgets are added on the desktop, from the edit toolbar, where the click that places
        // one can land exactly where it is wanted. A second way in here would be a second path.
        var edit = new Button { Content = "Edit layout" };
        edit.Click += (_, _) => host.SetEditMode(true);
        rows.Add(FluentLayout.Row(Glyphs.Add, "Add a widget", "From Add widget on the edit toolbar (Ctrl+Alt+W)", edit));

        rows.Add(FluentLayout.Header("Danger"));
        rows.Add(FluentLayout.Row(Glyphs.Delete, "Delete this view",
            host.State.Views.Count > 1 ? "Its widgets go with it" : "The last view cannot be deleted",
            delete));

        return Page(view.Name, rows);
    }

    /// <summary>
    /// [S-901] The view's place in the order — the order of the view bar, the switchers, this
    /// window and Ctrl+1..9 in edit mode. The buttons are set again after each move, since the page
    /// is not rebuilt for a change it made itself.
    /// </summary>
    private static Border OrderRow(AppHost host, string viewId)
    {
        var up = FluentLayout.IconCommand(Glyphs.MoveUp, "Move up");
        var down = FluentLayout.IconCommand(Glyphs.MoveDown, "Move down");
        var where = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, Fluent.Space2, 0) };

        void Sync()
        {
            var index = host.State.Views.FindIndex(v => v.Id == viewId);
            up.IsEnabled = index > 0;
            down.IsEnabled = index >= 0 && index < host.State.Views.Count - 1;
            where.Text = index >= 0 ? $"{index + 1} of {host.State.Views.Count}" : string.Empty;
        }

        up.Click += (_, _) =>
        {
            host.MoveView(viewId, -1);
            Sync();
        };

        down.Click += (_, _) =>
        {
            host.MoveView(viewId, +1);
            Sync();
        };

        Sync();

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space1,
            Children = { where, up, down },
        };

        return FluentLayout.Row(Glyphs.MoveUp, "Order",
            "Where it comes in the view bar and the view switcher; in edit mode, Ctrl+1 to Ctrl+9 show the first nine",
            controls, "reorder", "move", "position", "shortcut");
    }
}
