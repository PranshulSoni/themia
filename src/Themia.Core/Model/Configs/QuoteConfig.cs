using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Placeholder: the real Quote settings replace this file wholesale.</summary>
public sealed class QuoteConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Quote;

    public override WidgetConfig Clone() => new QuoteConfig();
}
