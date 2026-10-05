using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Widgets;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>
/// [S-1700] One view's page: its name and icon, its widgets, and, under Danger, copying and deleting
/// it. Which display shows it is chosen on the display's page or from the side menu, and its place
/// in the order by dragging it in the side menu ([S-1703]), so neither is repeated here.
/// </summary>
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
        name.LostFocus += (_, _) => RenameView(host, viewId, name.Text);

        var duplicate = new Button { Content = Str.Common_Duplicate.Text() };

        // [S-900] The copy shows on the display Settings acts on ([S-1702]).
        duplicate.Click += (_, _) => host.DuplicateView(viewId, host.SettingsSurface()?.Monitor.Id);

        var delete = new Button { Content = Str.App_DeleteView.Text(), IsEnabled = host.State.Views.Count > 1 };
        delete.Click += (_, _) =>
        {
            host.DeleteView(viewId);
            navigate(new ConfigTarget.App());
        };

        var rows = new List<FrameworkElement>
        {
            FluentLayout.Row(Glyphs.Rename, Str.View_Name_Label.Text(), null, name),
            FluentLayout.Row(Glyphs.View, Str.View_Icon_Label.Text(), Str.Shell_View_IconDesc.Text(), ViewIconPicker(host, view), "glyph", "symbol"),
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
        // [S-1701] Edit layout puts this view on the screen first: edit mode alone would arrange
        // whatever that screen happened to show.
        var edit = new Button { Content = Str.Shell_Card_EditLayout.Text() };
        edit.Click += (_, _) => host.EditView(viewId);
        rows.Add(FluentLayout.Row(Glyphs.Add, Str.Shell_View_AddWidget.Text(), Str.Shell_View_AddWidgetDesc.Format(("keys", Str.Shell_Key_CtrlPlus.Format(("key", "Alt+W")))), edit));

        rows.Add(FluentLayout.Header(Str.Shell_Settings_Danger.Text()));
        rows.Add(FluentLayout.Row(Glyphs.Copy, Str.Shell_View_DuplicateThis.Text(), Str.Shell_View_DuplicateDesc.Text(), duplicate, "copy", "clone"));
        rows.Add(FluentLayout.Row(Glyphs.Delete, Str.Shell_View_DeleteThis.Text(),
            host.State.Views.Count > 1 ? Str.Shell_View_DeleteDesc.Text() : Str.Display_CantDeleteLast.Text(),
            delete));

        return Page(view.Name, rows);
    }

    /// <summary>[S-1707] Rename a view, from its page or the side menu: one rule, <see cref="ViewOps.Rename"/>.</summary>
    internal static void RenameView(AppHost host, string viewId, string? name)
        => host.Mutate(s => ViewOps.Rename(s, viewId, name));

    /// <summary>
    /// [S-903] One glyph from the shared icon grid, or the name's initials — which is what a view
    /// without an icon shows in the view bar, so the "default" cell shows exactly that. Used by the
    /// view page and the side menu's Change icon.
    /// </summary>
    internal static DropDownButton ViewIconPicker(AppHost host, ViewState view, Action? changed = null)
    {
        var viewId = view.Id;
        return IconChoice.Create(
            view.Icon,
            Glyphs.View,
            glyph =>
            {
                host.Mutate(s =>
                {
                    if (s.FindView(viewId) is { } target)
                    {
                        target.Icon = glyph;
                    }
                });
                changed?.Invoke();
            },
            defaultText: ViewSwitcherRules.Initials(view.Name),
            defaultName: Str.Shell_View_Initials.Text());
    }

    /// <summary>
    /// [S-1706] The view a display shows: its own assignment, or the active view when it has none
    /// yet ([S-18]). Read only, unlike <see cref="AppState.ViewForMonitor"/>, which repairs.
    /// </summary>
    internal static string ShownViewId(AppState state, string monitorId)
        => state.MonitorViews.TryGetValue(monitorId, out var assigned) && state.FindView(assigned) is not null
            ? assigned
            : state.ActiveView().Id;
}
