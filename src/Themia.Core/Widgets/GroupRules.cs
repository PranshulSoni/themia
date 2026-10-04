using Themia.Core.Model;

namespace Themia.Core.Widgets;

/// <summary>[S-863]/[S-872] How a group's area is painted, and so whether it takes clicks.</summary>
public static class GroupRules
{
    /// <summary>One visible glyph: a single UTF-16 unit, or one surrogate pair.</summary>
    public static bool IsGlyph(string? text)
        => text is not null
            && (text.Length == 1 ? !char.IsWhiteSpace(text[0]) && !char.IsControl(text[0])
                : text.Length == 2 && char.IsSurrogatePair(text[0], text[1]));

    /// <summary>
    /// [S-863] The area's own fill as a colour and opacity, or null for the theme's faint wash. Junk
    /// (not a colour) is the wash; an opacity outside 0..1 is the default.
    /// </summary>
    public static (byte R, byte G, byte B, double Opacity)? Fill(GroupConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!HexColor.TryParse(config.Fill, out var r, out var g, out var b))
        {
            return null;
        }

        var opacity = double.IsFinite(config.FillOpacity) && config.FillOpacity is >= 0 and <= 1
            ? config.FillOpacity
            : GroupConfig.DefaultFillOpacity;

        return (r, g, b, opacity);
    }

    /// <summary>
    /// [S-872] Whether the area paints anything. A colour of the user's own paints unless they slid
    /// its opacity to zero; otherwise the wash paints unless the widget's background is None. The
    /// hit region follows this, not which mode was picked: what matters is what the user sees.
    /// </summary>
    public static bool AreaPaints(GroupConfig config, WidgetSurface surface)
        => Fill(config) is { } fill ? fill.Opacity > 0 : surface != WidgetSurface.Transparent;

    /// <summary>
    /// [S-872] Whether only the title bar takes clicks: a see-through area with members in it. A
    /// collapsed group is already just its bar ([S-871]), and an empty one keeps its whole rect,
    /// because its "Add widget" placeholder is real paint and the way to fill it.
    /// </summary>
    public static bool BandOnly(GroupConfig config, WidgetSurface surface, int memberCount)
        => !config.Collapsed && memberCount > 0 && !AreaPaints(config, surface);
}
