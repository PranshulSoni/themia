using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Placeholder: the real HabitTracker settings replace this file wholesale.</summary>
public sealed class HabitTrackerConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.HabitTracker;

    public override WidgetConfig Clone() => new HabitTrackerConfig();
}
