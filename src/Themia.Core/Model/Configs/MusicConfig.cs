using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Placeholder: the real Music settings replace this file wholesale.</summary>
public sealed class MusicConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Music;

    public override WidgetConfig Clone() => new MusicConfig();
}
