using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>An arithmetic scratchpad. [S-09] History is in-memory only.</summary>
public sealed class CalculatorConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Calculator;

    /// <summary>[S-05] Digits after the point, 0 to 8.</summary>
    public int DecimalPlaces { get; set; } = 2;

    [JsonIgnore]
    public int EffectiveDecimalPlaces => Math.Clamp(DecimalPlaces, 0, 8);

    public override WidgetConfig Clone() => new CalculatorConfig { DecimalPlaces = DecimalPlaces };
}
