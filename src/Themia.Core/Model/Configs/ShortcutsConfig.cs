using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Placeholder: the real Shortcuts settings replace this file wholesale.</summary>
public sealed class ShortcutsConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Shortcuts;

    public override WidgetConfig Clone() => new ShortcutsConfig();
}
