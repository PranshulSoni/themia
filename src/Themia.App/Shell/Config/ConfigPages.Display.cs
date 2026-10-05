using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Storage;

namespace Themia.App.Shell.Config;

/// <summary>
/// [S-853] One display's page: whether Themia is shown there, which view it shows, how much it
/// magnifies widgets, and what Windows reports about it.
/// </summary>
internal static partial class ConfigPages
{
    private static FrameworkElement BuildDisplay(AppHost host, string monitorId)
    {
        if (host.FindDisplay(monitorId) is not { } display)
        {
            return Page(Str.App_MonitorLabelUnknown.Text(), [Note(Str.Shell_Display_Gone.Text())]);
        }

        var state = host.State;
        var enabled = !state.DisabledMonitors.Contains(monitorId);
        var connected = host.Displays.Select(d => d.Info).ToList();

        // [S-845] The last display Themia is on cannot be switched off: with no surface there is no
        // edit mode and no way back here.
        var canToggle = !enabled || DisplayPlan.CanDisable(connected, state.DisabledMonitors, monitorId);
        ToggleSwitch? show = null;
        var reverting = false;
        show = FluentLayout.Toggle(enabled, on =>
        {
            // Refused (logged by the host): put the switch back rather than show a state that isn't so.
            if (!reverting && !host.SetDisplayEnabled(monitorId, on) && show is not null)
            {
                reverting = true;
                show.IsOn = !on;
                reverting = false;
            }
        });
        show.IsEnabled = canToggle;

        var rows = new List<FrameworkElement>
        {
            FluentLayout.Row(Glyphs.Display, Str.Shell_Display_Show.Text(),
                canToggle
                    ? Str.Shell_Display_ShowDesc.Text()
                    : Str.Shell_Display_ShowLast.Text(),
                show, "enable", "disable", "hide", "monitor"),

            FluentLayout.Row(Glyphs.View, Str.Shell_Display_Shows.Text(), Str.Shell_Display_ShowsDesc.Text(), ViewChoice(host, monitorId), "view"),

            FluentLayout.Row(Glyphs.Scale, Str.Display_WidgetScale_Label.Text(),
                Str.Shell_Display_ScaleDesc.Text(),
                FluentLayout.Slider(
                    (state.MonitorScales.TryGetValue(monitorId, out var scale) ? scale : 1.0) * 100,
                    AppStateStore.MinMonitorScale * 100,
                    AppStateStore.MaxMonitorScale * 100,
                    5,
                    "%",
                    value => host.SetDisplayScale(monitorId, value / 100)),
                "zoom", "size", "text size", "magnify"),

            FluentLayout.Row(Glyphs.Display, Str.Shell_Display_Resolution.Text(),
                Str.Shell_Display_ResolutionDesc.Format(("width", LocaleFormat.Integer(display.WorkArea.Width)), ("height", LocaleFormat.Integer(display.WorkArea.Height))),
                new TextBlock
                {
                    Text = Str.Shell_Display_Size.Format(("width", LocaleFormat.Integer(display.Bounds.Width)), ("height", LocaleFormat.Integer(display.Bounds.Height))),
                    VerticalAlignment = VerticalAlignment.Center,
                },
                "work area", "taskbar"),
        };

        rows.AddRange(ViewBarRows(host, monitorId));
        return Page(display.ShortName, rows, display.Describe());
    }

    /// <summary>[S-916] This display's view bar: shown or not, where, how big, names, and which views it offers.</summary>
    private static List<FrameworkElement> ViewBarRows(AppHost host, string monitorId)
    {
        var bar = ViewBarRules.For(host.State, monitorId);
        void Change(Action<ViewBarSettings> change) => host.Mutate(s => ViewBarRules.Update(s, monitorId, change));

        var rows = new List<FrameworkElement>
        {
            FluentLayout.Header(Str.Shell_ViewBar_Header.Text()),

            FluentLayout.Row(Glyphs.View, Str.Shell_ViewBar_Show.Text(),
                Str.Shell_ViewBar_ShowDesc.Text(),
                FluentLayout.Toggle(!bar.Hidden, on => Change(b => b.Hidden = !on)),
                "views", "switcher", "chips"),

            FluentLayout.Row(Glyphs.Display, Str.ViewBar_Position_Label.Text(),
                Str.Shell_ViewBar_PositionDesc.Text(),
                FluentLayout.Choice<ViewBarAnchor>(
                [
                    (ViewBarAnchor.TopLeft, Str.Shell_ViewBar_TopLeft.Text()),
                    (ViewBarAnchor.TopCenter, Str.Shell_ViewBar_TopCentre.Text()),
                    (ViewBarAnchor.TopRight, Str.Shell_ViewBar_TopRight.Text()),
                    (ViewBarAnchor.LeftCenter, Str.Shell_ViewBar_LeftVertical.Text()),
                    (ViewBarAnchor.RightCenter, Str.Shell_ViewBar_RightVertical.Text()),
                    (ViewBarAnchor.BottomLeft, Str.Shell_ViewBar_BottomLeft.Text()),
                    (ViewBarAnchor.BottomCenter, Str.Shell_ViewBar_BottomCentre.Text()),
                    (ViewBarAnchor.BottomRight, Str.Shell_ViewBar_BottomRight.Text()),
                ], bar.Anchor, value => Change(b => b.Anchor = value)),
                "anchor", "corner", "vertical"),

            FluentLayout.Row(Glyphs.Scale, Str.ViewBar_Size_Label.Text(), null,
                FluentLayout.Choice<ViewBarSize>(
                [
                    (ViewBarSize.Medium, Str.Shell_Size_Medium.Text()),
                    (ViewBarSize.Large, Str.Shell_Size_Large.Text()),
                    (ViewBarSize.ExtraLarge, Str.Shell_Size_ExtraLarge.Text()),
                ], bar.Size, value => Change(b => b.Size = value)),
                "bigger", "smaller"),

            FluentLayout.Row(Glyphs.Rename, Str.Shell_ViewBar_ShowNames.Text(),
                Str.Shell_ViewBar_ShowNamesDesc.Text(),
                FluentLayout.Toggle(!bar.IconOnly, on => Change(b => b.IconOnly = !on)),
                "labels", "icons only", "initials"),
        };

        // [S-908] One switch per view. A change can be refused (the last view cannot be switched
        // off), so after every change all the switches are set back from what was stored.
        var toggles = new List<(string Id, ToggleSwitch Toggle)>();
        var syncing = false;

        void Sync()
        {
            var settings = ViewBarRules.For(host.State, monitorId);
            var offered = ViewBarRules.VisibleViews(host.State.Views, settings).Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
            syncing = true;
            try
            {
                foreach (var (id, toggle) in toggles)
                {
                    toggle.IsOn = offered.Contains(id);
                }
            }
            finally
            {
                syncing = false;
            }
        }

        var viewRows = new List<Border>();
        foreach (var view in host.State.Views)
        {
            var id = view.Id;
            var toggle = FluentLayout.Toggle(false, on =>
            {
                if (syncing)
                {
                    return;
                }

                host.Mutate(s => ViewBarRules.Update(s, monitorId, b => b.VisibleViews = ViewBarRules.ToggleVisible(s.Views, b, id, on)));
                Sync();
            });

            toggles.Add((id, toggle));
            viewRows.Add(FluentLayout.Row(string.IsNullOrEmpty(view.Icon) ? Glyphs.View : view.Icon, view.Name, null, toggle));
        }

        Sync();

        rows.Add(FluentLayout.Expander(Glyphs.View, Str.Shell_ViewBar_Views.Text(),
            Str.Shell_ViewBar_ViewsDesc.Text(),
            null, viewRows));

        return rows;
    }

    /// <summary>
    /// [S-1706] The view drop-down: one of the existing views, nothing else. A display is never
    /// given a view of its own ([S-1705]), and there is no "follow the active view": every display
    /// holds an assignment, so that choice would only last until the next display pass.
    /// </summary>
    private static ComboBox ViewChoice(AppHost host, string monitorId)
    {
        var views = host.State.Views.Select(v => (v.Id, v.Name)).ToList();
        var shown = ShownViewId(host.State, monitorId);

        var combo = new ComboBox { MinWidth = 220 };
        foreach (var (id, name) in views)
        {
            combo.Items.Add(new ComboBoxItem { Content = name, Tag = id });
        }

        combo.SelectedIndex = Math.Max(0, views.FindIndex(v => v.Id == shown));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: string id })
            {
                // [S-19] The one way a display is given a view.
                host.ShowView(monitorId, id);
            }
        };

        return combo;
    }
}
