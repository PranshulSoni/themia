using Microsoft.UI.Xaml;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>The Calculator's settings rows. Each writes straight to the live config and saves.</summary>
internal static partial class WidgetSettings
{
    public static void Calculator(List<FrameworkElement> rows, CalculatorConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header(Str.Widget_Calculator_Label.Text()));

        // [S-05] Applied to the live result and to every row already in the history.
        rows.Add(FluentLayout.Row(Glyphs.Calculator, Str.WidgetsA_Calculator_Settings_Decimals_Label.Text(), Str.WidgetsA_Calculator_Settings_Decimals_Desc.Text(),
            FluentLayout.Slider(config.EffectiveDecimalPlaces, 0, ExpressionResult.MaxDecimalPlaces, 1, string.Empty,
                value => { config.DecimalPlaces = (int)Math.Round(value); save(); })));

        // [S-1510] Shown only where the tile has room for it: under the line on a tall tile, beside
        // everything else on a large one.
        rows.Add(FluentLayout.Row(Glyphs.Keyboard, Str.WidgetsA_Calculator_Settings_Keypad_Label.Text(), Str.WidgetsA_Calculator_Settings_Keypad_Desc.Text(),
            FluentLayout.Toggle(config.ShowKeypad, value => { config.ShowKeypad = value; save(); })));
    }
}
