using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>
/// An accordion: the children are rolled up to a row each, and one is open at a time.
/// <para>
/// A stack owns its children's geometry — their stored rect is ignored while they are in it, and
/// restored to meaning the moment they leave.
/// </para>
/// </summary>
public sealed class StackConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Stack;

    /// <summary>
    /// Which child is open, by id. An id that no longer names a child opens the first one instead of
    /// showing nothing, because a stack with everything rolled up looks broken rather than tidy.
    /// </summary>
    public string? OpenChildId { get; set; }

    public override WidgetConfig Clone() => new StackConfig { OpenChildId = OpenChildId };
}
