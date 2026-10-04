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

/// <summary>One view's page: its name, its widgets, and the displays showing it.</summary>
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

        // On the screen the button was pressed on: every display shows a view of its own, so "show
        // this view" has to say where.
        var activate = new Button { Content = "Show on this screen" };
        activate.Click += (_, _) =>
        {
            if ((host.SurfaceUnderCursor() ?? host.Surfaces.FirstOrDefault()) is { } surface)
            {
                host.ShowView(surface.Monitor.DeviceName, viewId);
            }
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
            FluentLayout.Row(Glyphs.View, "Active view",
                host.State.ActiveView().Id == viewId ? "This view is showing now" : "Switch the desktop to it",
                activate),
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
}
