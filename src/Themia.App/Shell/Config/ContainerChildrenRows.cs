using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Widgets;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

/// <summary>
/// [S-866]/[S-870] The settings rows that list what a container holds, shared by group, stack and
/// tabs: one card per member with its settings, move up/down (stack and tabs — a group's members
/// have no order, only places) and take-out, then "Add widget". Every change goes through the
/// host's single write path; the list redraws itself when the state changes, so a widget dragged in
/// on the desktop while the page is open shows up here too.
/// </summary>
internal static class ContainerChildrenRows
{
    public static void Add(List<FrameworkElement> rows, AppHost host, ViewState view, WidgetInstance container)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(container);

        var kind = WidgetTree.ContainerOf(container.Kind);
        var ordered = kind != ContainerKind.Group;
        var takeOut = kind == ContainerKind.Group ? "Remove from group" : "Take out";

        rows.Add(FluentLayout.Header("Widgets inside"));

        var list = new StackPanel { Spacing = Fluent.Space1 };
        rows.Add(list);

        void Rebuild()
        {
            list.Children.Clear();
            var children = view.ChildrenOf(container.Id);

            if (children.Count == 0)
            {
                list.Children.Add(FluentLayout.Note(kind == ContainerKind.Group
                    ? "Nothing in this group yet. Add a widget below, or drag one onto it in edit mode."
                    : "Nothing in here yet. Add a widget below, or drag one onto it in edit mode."));
                return;
            }

            foreach (var child in children)
            {
                var id = child.Id;
                var definition = WidgetCatalog.Definition(child.Kind);
                var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Fluent.Space1 };

                if (ordered)
                {
                    var up = FluentLayout.IconCommand(Glyphs.MoveUp, "Move up");
                    up.IsEnabled = view.CanMoveChild(id, -1);
                    up.Click += (_, _) => Change(() => view.MoveChild(id, -1));

                    var down = FluentLayout.IconCommand(Glyphs.MoveDown, "Move down");
                    down.IsEnabled = view.CanMoveChild(id, 1);
                    down.Click += (_, _) => Change(() => view.MoveChild(id, 1));

                    commands.Children.Add(up);
                    commands.Children.Add(down);
                }

                var settings = FluentLayout.IconCommand(Glyphs.Settings, "Its settings");
                settings.Click += (_, _) => host.ShowConfig(new ConfigTarget.Widget(id));

                // The widget is not deleted, only let go of: back on the desktop at its own rect.
                var remove = FluentLayout.IconCommand(Glyphs.TakeOut, takeOut);
                remove.Click += (_, _) => Change(() => view.Reparent(id, null));

                commands.Children.Add(settings);
                commands.Children.Add(remove);

                var name = string.IsNullOrWhiteSpace(child.Label) ? definition.Label : child.Label!;
                list.Children.Add(FluentLayout.Row(definition.Glyph, name, string.IsNullOrWhiteSpace(child.Label) ? null : definition.Label, commands));
            }
        }

        void Change(Func<bool> change)
        {
            host.Mutate(_ => change());
            Rebuild();
        }

        void OnState() => Rebuild();

        list.Loaded += (_, _) => host.StateChanged += OnState;
        list.Unloaded += (_, _) => host.StateChanged -= OnState;
        Rebuild();

        // [S-870] The picker on the desktop, with this container chosen; only while its view is shown.
        var add = new Button { Content = "Add widget" };
        add.Click += (_, _) => add.IsEnabled = host.AddInside(container.Id);
        rows.Add(FluentLayout.Row(Glyphs.Add, "Add a widget inside", "Opens the widget picker on the desktop, with this one chosen", add));
    }
}
