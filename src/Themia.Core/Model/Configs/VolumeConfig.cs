using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Placeholder: the real Volume settings replace this file wholesale.</summary>
public sealed class VolumeConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Volume;

    public override WidgetConfig Clone() => new VolumeConfig();
}
