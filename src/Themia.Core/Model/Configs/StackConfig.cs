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
    /// [S-864] Which child is open, by id, or null for none — every child starts closed, and a click
    /// on the open child's row closes it. An id that no longer names a child is "nothing open" and is
    /// left as it is (see <see cref="ContainerRules.OpenIndex"/>).
    /// </summary>
    public string? OpenChildId { get; set; }

    public override WidgetConfig Clone() => new StackConfig { OpenChildId = OpenChildId };
}
