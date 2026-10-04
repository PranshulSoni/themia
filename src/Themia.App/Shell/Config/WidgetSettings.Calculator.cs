using Microsoft.UI.Xaml;
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

        rows.Add(FluentLayout.Header("Calculator"));

        // [S-05] Applied to the live result and to every row already in the history.
        rows.Add(FluentLayout.Row(Glyphs.Calculator, "Decimal places", "Digits shown after the point",
            FluentLayout.Slider(config.EffectiveDecimalPlaces, 0, ExpressionResult.MaxDecimalPlaces, 1, string.Empty,
                value => { config.DecimalPlaces = (int)Math.Round(value); save(); })));
    }
}
