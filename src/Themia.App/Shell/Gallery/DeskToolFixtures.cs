#if DEBUG
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-1516] The calculator's and the colour swatch's populated gallery pages. The calculator's
/// history is in memory, per widget, so its fixture is a seed the view reads on attach (as the
/// original's demo mode seeded a few past sums, its S-12); the swatch's populated page is its
/// palette mode, the one with a list to lay out (the default page is the solid block).
/// </summary>
internal static class DeskToolFixtures
{
    public static bool Has(WidgetKind kind) => kind is WidgetKind.Calculator or WidgetKind.ColorSwatch;

    /// <summary>Newest first, as the history holds them; null for none.</summary>
    public static IReadOnlyList<Calculation>? CalculatorHistory { get; private set; }

    public static void Reset() => CalculatorHistory = null;

    public static WidgetConfig? Create(WidgetKind kind)
    {
        switch (kind)
        {
            case WidgetKind.Calculator:
                CalculatorHistory =
                [
                    new Calculation("1250 * 1.077", "1346.25"),
                    new Calculation("(48 + 52) / 3", "33.3333"),
                    new Calculation("2^10", "1024"),
                    new Calculation("19.90 * 3", "59.7"),
                    new Calculation("1680 / 7", "240"),
                    new Calculation("15% * 240", "36"),
                    new Calculation("365 * 24", "8760"),
                    new Calculation("12 + 30", "42"),
                ];

                return new CalculatorConfig();

            case WidgetKind.ColorSwatch:
                return new ColorSwatchConfig { Mode = SwatchMode.Palette };

            default:
                return null;
        }
    }
}
#endif
