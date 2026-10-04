using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Widgets;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>
/// Settings rows for the power tile. Each control writes straight to the live config and saves, so
/// the widget behind the window follows along.
/// </summary>
internal static partial class WidgetSettings
{
    public static void Power(List<FrameworkElement> rows, PowerConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        // [S-1242] One row per action: a switch to show it, and arrows to move a shown one. The
        // chosen ones come first, in the order the tile draws them.
        rows.Add(FluentLayout.Header(Str.Power_ActionsLabel.Text()));

        var host = new StackPanel { Spacing = Fluent.Space1 };
        rows.Add(host);

        void Rebuild()
        {
            host.Children.Clear();
            config.Actions = [.. PowerActionRules.Visible(config.Actions)];

            foreach (var action in PowerActionRules.SettingsOrder(config.Actions))
            {
                host.Children.Add(ActionRow(action));
            }
        }

        FrameworkElement ActionRow(PowerAction action)
        {
            var index = config.Actions.IndexOf(action);
            var shown = index >= 0;

            var up = FluentLayout.IconCommand(Glyphs.MoveUp, Str.WidgetsC_Tasks_MoveUp.Text());
            var down = FluentLayout.IconCommand(Glyphs.MoveDown, Str.WidgetsC_Tasks_MoveDown.Text());
            up.IsEnabled = shown && index > 0;
            down.IsEnabled = shown && index < config.Actions.Count - 1;
            up.Click += (_, _) => Move(index, -1);
            down.Click += (_, _) => Move(index, 1);

            var toggle = FluentLayout.Toggle(shown, on =>
            {
                if (PowerActionRules.Toggle(config.Actions, action, on))
                {
                    save();
                    Rebuild();
                }
            });

            var controls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Fluent.Space1,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { up, down, toggle },
            };

            var description = PowerActionRules.EndsSession(action)
                ? Str.WidgetsC_Power_Settings_AsksToConfirm.Format(("description", PowerActionRules.Description(action)))
                : PowerActionRules.Description(action);
            return FluentLayout.Row(PowerGlyphs.For(action), PowerActionRules.Label(action), description, controls);
        }

        void Move(int index, int delta)
        {
            if (DeskListOrder.Move(config.Actions, index, delta))
            {
                save();
                Rebuild();
            }
        }

        Rebuild();

        rows.Add(FluentLayout.Header(Str.WidgetsC_Power_Settings_Buttons.Text()));

        rows.Add(FluentLayout.Row(Glyphs.Tag, Str.Power_LabelsLabel.Text(),
            Str.WidgetsC_Power_Settings_LabelsDesc.Text(),
            FluentLayout.Toggle(config.ShowLabels, value => { config.ShowLabels = value; save(); })));

        // [S-1245] Said plainly: this is the one setting that decides whether a single click can
        // end the session.
        rows.Add(FluentLayout.Row(Glyphs.Warning, Str.WidgetsC_Power_Settings_Confirm_Label.Text(),
            Str.WidgetsC_Power_Settings_Confirm_Desc.Text(),
            FluentLayout.Toggle(config.Confirm, value => { config.Confirm = value; save(); })));

        rows.Add(FluentLayout.Header(Str.WidgetsC_Power_Settings_Status.Text()));

        rows.Add(FluentLayout.Row(Glyphs.Lightning, Str.WidgetsC_Power_Settings_ShowPlan_Label.Text(),
            Str.WidgetsC_Power_Settings_ShowPlan_Desc.Text(),
            FluentLayout.Toggle(config.ShowPlan, value => { config.ShowPlan = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.BatterySaver, Str.WidgetsC_Power_Settings_ShowSaver_Label.Text(),
            Str.WidgetsC_Power_Settings_ShowSaver_Desc.Text(),
            FluentLayout.Toggle(config.ShowBatterySaver, value => { config.ShowBatterySaver = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Timer, Str.WidgetsC_Power_Settings_ShowRemaining_Label.Text(),
            Str.WidgetsC_Power_Settings_ShowRemaining_Desc.Text(),
            FluentLayout.Toggle(config.ShowTimeRemaining, value => { config.ShowTimeRemaining = value; save(); })));

        FluentLayout.RefreshSeconds(rows, Str.WidgetsC_Power_Settings_Refresh_Desc.Text(), config.IntervalSeconds, 5, 300, 5,
            value => { config.IntervalSeconds = value; save(); });
    }
}
