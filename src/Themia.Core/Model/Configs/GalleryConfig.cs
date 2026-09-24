using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Placeholder: the real Gallery settings replace this file wholesale.</summary>
public sealed class GalleryConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Gallery;

    public override WidgetConfig Clone() => new GalleryConfig();
}
