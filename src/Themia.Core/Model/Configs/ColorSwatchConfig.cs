using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Placeholder: the real ColorSwatch settings replace this file wholesale.</summary>
public sealed class ColorSwatchConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.ColorSwatch;

    public override WidgetConfig Clone() => new ColorSwatchConfig();
}
