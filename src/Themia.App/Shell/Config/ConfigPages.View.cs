using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Widgets;
using Themia.Core.Localization;
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
            return Page(Str.CtxMenu_View.Text(), [Note(Str.Shell_View_Gone.Text())]);
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
            defaultName: Str.Shell_View_Initials.Text());

        // On the screen the button was pressed on: every display shows a view of its own, so "show
        // this view" has to say where.
        var activate = new Button { Content = Str.Shell_View_ShowHere.Text() };
        activate.Click += (_, _) =>
        {
            if ((host.SurfaceUnderCursor() ?? host.Surfaces.FirstOrDefault()) is { } surface)
            {
                host.ShowView(surface.Monitor.Id, viewId);
            }
        };

        var duplicate = new Button { Content = Str.Common_Duplicate.Text() };
        duplicate.Click += (_, _) =>
        {
            // [S-900] The copy shows where the pointer is, as the toolbar's and the bar's do.
            var surface = host.SurfaceUnderCursor() ?? host.Surfaces.FirstOrDefault();
            host.DuplicateView(viewId, surface?.Monitor.Id);
        };

        var delete = new Button { Content = Str.App_DeleteView.Text(), IsEnabled = host.State.Views.Count > 1 };
        delete.Click += (_, _) =>
        {
            host.DeleteView(viewId);
            navigate(new ConfigTarget.App());
        };

        var rows = new List<FrameworkElement>
        {
            FluentLayout.Row(Glyphs.Rename, Str.View_Name_Label.Text(), null, name),
            FluentLayout.Row(Glyphs.View, Str.View_Icon_Label.Text(), Str.Shell_View_IconDesc.Text(), icon, "glyph", "symbol"),
            FluentLayout.Row(Glyphs.View, Str.Shell_View_Active.Text(),
                host.State.ActiveView().Id == viewId ? Str.Shell_View_ActiveNow.Text() : Str.Shell_View_ActiveSwitch.Text(),
                activate),
            OrderRow(host, viewId),
            FluentLayout.Row(Glyphs.Copy, Str.Shell_View_DuplicateThis.Text(), Str.Shell_View_DuplicateDesc.Text(), duplicate, "copy", "clone"),
            FluentLayout.Header(Str.Settings_License_QuotaWidgets.Text()),
        };

        if (view.Widgets.Count == 0)
        {
            rows.Add(Note(Str.Shell_View_NoWidgets.Text()));
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
        var edit = new Button { Content = Str.Shell_Card_EditLayout.Text() };
        edit.Click += (_, _) => host.SetEditMode(true);
        rows.Add(FluentLayout.Row(Glyphs.Add, Str.Shell_View_AddWidget.Text(), Str.Shell_View_AddWidgetDesc.Format(("keys", Str.Shell_Key_CtrlPlus.Format(("key", "Alt+W")))), edit));

        rows.Add(FluentLayout.Header(Str.Shell_Settings_Danger.Text()));
        rows.Add(FluentLayout.Row(Glyphs.Delete, Str.Shell_View_DeleteThis.Text(),
            host.State.Views.Count > 1 ? Str.Shell_View_DeleteDesc.Text() : Str.Display_CantDeleteLast.Text(),
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
        var up = FluentLayout.IconCommand(Glyphs.MoveUp, Str.ChildList_MoveUp.Text());
        var down = FluentLayout.IconCommand(Glyphs.MoveDown, Str.ChildList_MoveDown.Text());
        var where = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, Fluent.Space2, 0) };

        void Sync()
        {
            var index = host.State.Views.FindIndex(v => v.Id == viewId);
            up.IsEnabled = index > 0;
            down.IsEnabled = index >= 0 && index < host.State.Views.Count - 1;
            where.Text = index >= 0 ? Str.Shell_View_Position.Format(("n", index + 1), ("total", host.State.Views.Count)) : string.Empty;
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

        return FluentLayout.Row(Glyphs.MoveUp, Str.Shell_View_Order.Text(),
            Str.Shell_View_OrderDesc.Format(("first", Str.Shell_Key_CtrlPlus.Format(("key", "1"))), ("last", Str.Shell_Key_CtrlPlus.Format(("key", "9")))),
            controls, "reorder", "move", "position", "shortcut");
    }
}
