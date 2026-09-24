using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Placeholder: the real MoodTracker settings replace this file wholesale.</summary>
public sealed class MoodTrackerConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.MoodTracker;

    public override WidgetConfig Clone() => new MoodTrackerConfig();
}
