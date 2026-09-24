using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Placeholder: the real Scratchpad settings replace this file wholesale.</summary>
public sealed class ScratchpadConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Scratchpad;

    public override WidgetConfig Clone() => new ScratchpadConfig();
}
