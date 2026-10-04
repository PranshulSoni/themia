using System.Text.Json.Serialization;
using Themia.Core.Widgets;

namespace Themia.Core.Model;

/// <summary>
/// A labelled area on the desktop that other widgets belong to.
/// <para>
/// A group does <b>not</b> take over its children's geometry: they keep the place on the grid the
/// user put them in, and the group draws behind them. That is the whole difference between a group
/// and a stack, and it is why dropping a widget into a group does not move it.
/// </para>
/// </summary>
public sealed class GroupConfig : WidgetConfig
{
    /// <summary>[S-863] The fill's opacity when none is set: the original's default.</summary>
    public const double DefaultFillOpacity = 0.12;

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Group;

    /// <summary>
    /// Collapsed hides the children rather than moving them, so expanding puts every one back
    /// exactly where it was. A group that rearranged its contents on collapse would lose the layout.
    /// [S-871] The card itself shrinks to its header bar.
    /// </summary>
    public bool Collapsed { get; set; }

    /// <summary>[S-863] A Segoe Fluent glyph for the title bar, or null for the group's own.</summary>
    public string? Icon { get; set; }

    /// <summary>
    /// [S-863] The area's fill as <c>#RRGGBB</c>, or empty for the theme's faint wash. Read through
    /// <see cref="GroupRules"/>; junk falls back to the wash.
    /// </summary>
    public string Fill { get; set; } = string.Empty;

    /// <summary>[S-863] The opacity of <see cref="Fill"/>, 0..1.</summary>
    public double FillOpacity { get; set; } = DefaultFillOpacity;

    public override WidgetConfig Clone() => new GroupConfig
    {
        Collapsed = Collapsed,
        Icon = Icon,
        Fill = Fill,
        FillOpacity = FillOpacity,
    };

    public override IReadOnlyList<string> Repair()
    {
        var notes = new List<string>(base.Repair());

        if (!double.IsFinite(FillOpacity) || FillOpacity < 0 || FillOpacity > 1)
        {
            notes.Add($"Group: fill opacity {FillOpacity} is outside 0..1; using {DefaultFillOpacity}");
            FillOpacity = DefaultFillOpacity;
        }

        if (Fill.Length > 0 && !HexColor.IsOverridden(Fill))
        {
            notes.Add("Group: the fill is not a colour; using the theme's wash");
            Fill = string.Empty;
        }

        if (Icon is { } icon && !GroupRules.IsGlyph(icon))
        {
            notes.Add("Group: the icon is not a single glyph; using the group's own");
            Icon = null;
        }

        return notes;
    }
}
