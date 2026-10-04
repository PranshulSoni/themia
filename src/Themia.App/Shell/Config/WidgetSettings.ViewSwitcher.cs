using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>
/// Settings rows for the view switcher: which views get a button, and whether the buttons carry
/// names. Each control writes straight to the live config and saves.
/// </summary>
internal static partial class WidgetSettings
{
    public static void ViewSwitcher(
        List<FrameworkElement> rows,
        ViewSwitcherConfig config,
        IReadOnlyList<ViewSummary> views,
        Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(views);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header(Str.ViewSwitcher_ViewsTab.Text()));
        rows.Add(FluentLayout.Note(Str.WidgetsC_ViewSwitcher_Settings_Note.Text()));

        var toggles = new List<(string Id, ToggleSwitch Toggle)>(views.Count);
        var syncing = false;

        // [S-490] One switch per view. A change can be refused — the last view cannot be switched
        // off — so after every change all the switches are set back from the selection as it stands,
        // rather than trusting the one that was flipped.
        void Sync()
        {
            var effective = ViewSwitcherRules.EffectiveVisible(views, config.VisibleViews);
            syncing = true;
            try
            {
                foreach (var (id, toggle) in toggles)
                {
                    toggle.IsOn = effective.Contains(id);
                }
            }
            finally
            {
                syncing = false;
            }
        }

        foreach (var view in views)
        {
            var id = view.Id;
            var toggle = FluentLayout.Toggle(false, on =>
            {
                if (syncing)
                {
                    return;
                }

                config.VisibleViews = ViewSwitcherRules.Toggle(views, config.VisibleViews, id, on);
                save();
                Sync();
            });

            toggles.Add((id, toggle));
            rows.Add(FluentLayout.Row(Glyphs.View, view.Name, null, toggle));
        }

        Sync();

        rows.Add(FluentLayout.Header(Str.WidgetsC_ViewSwitcher_Settings_Buttons.Text()));

        rows.Add(FluentLayout.Row(Glyphs.Hidden, Str.WidgetsC_ViewSwitcher_InitialsOnly.Text(),
            Str.WidgetsC_ViewSwitcher_Settings_InitialsOnly_Desc.Text(),
            FluentLayout.Toggle(config.IconOnly, value => { config.IconOnly = value; save(); })));
    }
}
