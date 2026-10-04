using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>An arithmetic scratchpad. [S-09] History is in-memory only.</summary>
public sealed class CalculatorConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Calculator;

    /// <summary>[S-05] Digits after the point, 0 to 8. [S-1515] 4 by default, as the original.</summary>
    public int DecimalPlaces { get; set; } = 4;

    /// <summary>
    /// [S-1510] Show the keypad where the tile has room for it. On by default: on a desktop whose
    /// widgets do not take the keyboard until asked, the keys are how a sum is worked with the mouse.
    /// </summary>
    public bool ShowKeypad { get; set; } = true;

    [JsonIgnore]
    public int EffectiveDecimalPlaces => Math.Clamp(DecimalPlaces, 0, 8);

    public override WidgetConfig Clone() => new CalculatorConfig { DecimalPlaces = DecimalPlaces, ShowKeypad = ShowKeypad };
}
