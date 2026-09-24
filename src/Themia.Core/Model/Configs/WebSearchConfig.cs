using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Placeholder: the real WebSearch settings replace this file wholesale.</summary>
public sealed class WebSearchConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.WebSearch;

    public override WidgetConfig Clone() => new WebSearchConfig();
}
