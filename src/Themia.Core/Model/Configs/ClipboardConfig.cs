using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Placeholder: the real Clipboard settings replace this file wholesale.</summary>
public sealed class ClipboardConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Clipboard;

    public override WidgetConfig Clone() => new ClipboardConfig();
}
