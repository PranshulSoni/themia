using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Placeholder: the real Rss settings replace this file wholesale.</summary>
public sealed class RssConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Rss;

    public override WidgetConfig Clone() => new RssConfig();
}
