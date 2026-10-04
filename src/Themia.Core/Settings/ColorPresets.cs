namespace Themia.Core.Settings;

/// <summary>A preset in the colour picker: the stored <c>#RRGGBB</c> and the name a screen reader says.</summary>
public readonly record struct ColorPreset(string Hex, string Name);

/// <summary>
/// The swatches the shared colour picker offers before "Custom colour". [S-1549]
/// <para>
/// Colours are picked, never typed: a hex box is a field most people cannot fill in and the rest
/// get wrong. The default set is Windows' own accent palette — the 48 colours, in the order and
/// with the names Settings › Personalization › Colours shows them — so the picker reads as the
/// one users already know.
/// </para>
/// </summary>
public static class ColorPresets
{
    /// <summary>Swatches per row, as Windows lays the accent palette out.</summary>
    public const int Columns = 8;

    /// <summary>Windows' accent palette, row by row.</summary>
    public static IReadOnlyList<ColorPreset> Windows { get; } =
    [
        new("#FFB900", "Yellow gold"), new("#FF8C00", "Gold"), new("#F7630C", "Orange bright"), new("#CA5010", "Orange dark"),
        new("#DA3B01", "Rust"), new("#EF6950", "Pale rust"), new("#D13438", "Brick red"), new("#FF4343", "Mod red"),

        new("#E74856", "Pale red"), new("#E81123", "Red"), new("#EA005E", "Rose bright"), new("#C30052", "Rose"),
        new("#E3008C", "Plum light"), new("#BF0077", "Plum"), new("#C239B3", "Orchid light"), new("#9A0089", "Orchid"),

        new("#0078D4", "Default blue"), new("#0063B1", "Navy blue"), new("#8E8CD8", "Purple shadow"), new("#6B69D6", "Purple shadow dark"),
        new("#8764B8", "Iris pastel"), new("#744DA9", "Iris spring"), new("#B146C2", "Violet red light"), new("#881798", "Violet red"),

        new("#0099BC", "Cool blue bright"), new("#2D7D9A", "Cool blue"), new("#00B7C3", "Seafoam"), new("#038387", "Seafoam teal"),
        new("#00B294", "Mint light"), new("#018574", "Mint dark"), new("#00CC6A", "Turf green"), new("#10893E", "Sport green"),

        new("#7A7574", "Gray"), new("#5D5A58", "Gray brown"), new("#68768A", "Steel blue"), new("#515C6B", "Metal blue"),
        new("#567C73", "Pale moss"), new("#486860", "Moss"), new("#498205", "Meadow green"), new("#107C10", "Green"),

        new("#767676", "Overcast"), new("#4C4A48", "Storm"), new("#69797E", "Blue gray"), new("#4A5459", "Gray dark"),
        new("#647C64", "Liddy green"), new("#525E54", "Sage"), new("#847545", "Camouflage desert"), new("#7E735F", "Camouflage"),
    ];

    /// <summary>
    /// Surfaces: the accent palette is all saturated colour, and a card fill or a border usually
    /// wants black, white or a grey in between. One row, offered first where a surface is picked.
    /// </summary>
    public static IReadOnlyList<ColorPreset> Neutrals { get; } =
    [
        new("#000000", "Black"), new("#191919", "Charcoal"), new("#202020", "Dark gray"), new("#2C2C2C", "Graphite"),
        new("#454545", "Slate"), new("#E5E5E5", "Light gray"), new("#F3F3F3", "Mist"), new("#FFFFFF", "White"),
    ];

    /// <summary>The surface picker's swatches: the neutrals, then the accent palette.</summary>
    public static IReadOnlyList<ColorPreset> Surfaces { get; } = [.. Neutrals, .. Windows];

    /// <summary>
    /// The preset a stored value is, or null when it is a custom colour (or empty). Compared on the
    /// normalised value, so <c>#ffb900</c> and <c>#FFB900</c> are the same swatch.
    /// </summary>
    public static ColorPreset? Find(IReadOnlyList<ColorPreset> presets, string? value)
    {
        ArgumentNullException.ThrowIfNull(presets);

        var normalised = Widgets.HexColor.Normalise(value);
        if (normalised.Length == 0)
        {
            return null;
        }

        foreach (var preset in presets)
        {
            if (string.Equals(Widgets.HexColor.Normalise(preset.Hex), normalised, StringComparison.Ordinal))
            {
                return preset;
            }
        }

        return null;
    }
}
