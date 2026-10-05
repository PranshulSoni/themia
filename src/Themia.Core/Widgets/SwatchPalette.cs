using Themia.Core.Localization;

namespace Themia.Core.Widgets;

/// <summary>One colour in a palette, as stored: the text the user typed and the name they gave it.</summary>
/// <param name="Value">The colour in any notation <see cref="ColorFormats.TryParse"/> accepts.</param>
/// <param name="Name">What to call it. May be empty, in which case the value is the label.</param>
public readonly record struct SwatchSeed(string Value, string Name);

/// <summary>One colour in a palette, as shown: parsed, named, and ready to copy.</summary>
/// <param name="Color">The parsed colour.</param>
/// <param name="Name">The display name, never empty — it falls back to the formatted value.</param>
public readonly record struct Swatch(RgbColor Color, string Name);

/// <summary>
/// The palette a colour widget shows, and the rules for editing it.
/// <para>
/// Kept here rather than in the view because reordering, dropping a malformed entry and choosing a
/// label are decisions, not drawing — and the test project can see this assembly while it cannot
/// see a <c>Border</c>.
/// </para>
/// </summary>
public static class SwatchPalette
{
    /// <summary>
    /// A palette that is worth having on the desktop unmodified: one step of a calm ramp per hue,
    /// all at a comparable lightness, so they read as a set rather than as ten unrelated colours.
    /// The names are written into the new widget's own list in the language it was created in, and
    /// from then on they are the user's to rename.
    /// </summary>
    public static IReadOnlyList<SwatchSeed> Default => DefaultIn(Strings.Current);

    /// <summary>The default palette, named in <paramref name="locale"/>.</summary>
    public static IReadOnlyList<SwatchSeed> DefaultIn(Locale locale) =>
    [
        new("#0F172A", Strings.Get(locale, Str.WidgetsA_ColorSwatch_Default_Ink)),
        new("#64748B", Strings.Get(locale, Str.WidgetsA_ColorSwatch_Default_Slate)),
        new("#38BDF8", Strings.Get(locale, Str.WidgetsA_ColorSwatch_Default_Sky)),
        new("#2563EB", Strings.Get(locale, Str.WidgetsA_ColorSwatch_Default_Blue)),
        new("#7C3AED", Strings.Get(locale, Str.WidgetsA_ColorSwatch_Default_Violet)),
        new("#F43F5E", Strings.Get(locale, Str.WidgetsA_ColorSwatch_Default_Rose)),
        new("#F59E0B", Strings.Get(locale, Str.WidgetsA_ColorSwatch_Default_Amber)),
        new("#84CC16", Strings.Get(locale, Str.WidgetsA_ColorSwatch_Default_Lime)),
        new("#10B981", Strings.Get(locale, Str.WidgetsA_ColorSwatch_Default_Emerald)),
        new("#E7E5E4", Strings.Get(locale, Str.WidgetsA_ColorSwatch_Default_Stone)),
    ];

    /// <summary>A palette this size is already unwieldy in a widget, and it bounds the state file.</summary>
    public const int MaxSwatches = 64;

    /// <summary>
    /// Turn stored entries into shown ones, dropping the ones that do not name a colour.
    /// <para>
    /// Dropping rather than substituting: a swatch whose value is <c>"not a colour"</c> has no
    /// colour to show, and painting it black would be inventing one. The entry stays in settings,
    /// where it can be fixed.
    /// </para>
    /// </summary>
    public static List<Swatch> Read(IReadOnlyList<SwatchSeed>? entries, ColorNotation notation)
    {
        var swatches = new List<Swatch>();

        if (entries is null)
        {
            return swatches;
        }

        foreach (var entry in entries)
        {
            if (!ColorFormats.TryParse(entry.Value, out var color))
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(entry.Name)
                ? ColorFormats.Format(color, notation)
                : entry.Name.Trim();

            swatches.Add(new Swatch(color, name));
        }

        return swatches;
    }
}
