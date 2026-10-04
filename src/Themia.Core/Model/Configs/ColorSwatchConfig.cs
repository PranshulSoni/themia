using System.Text.Json.Serialization;
using Themia.Core.Widgets;

namespace Themia.Core.Model;

/// <summary>One colour in the palette, as stored: what the user typed and what they call it.</summary>
public sealed class SwatchItem
{
    /// <summary>The colour, in any notation <see cref="ColorFormats.TryParse"/> accepts.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>What to call it. Empty means "label it with its own value".</summary>
    public string Name { get; set; } = string.Empty;

    public SwatchItem Clone() => new() { Value = Value, Name = Name };
}

/// <summary>
/// A palette you can copy colours out of.
/// <para>
/// The palette is the user's own list, seeded with a usable one so a freshly dropped widget is
/// worth looking at. An empty list is therefore a deliberate empty palette rather than "not
/// configured yet", and the widget says so instead of quietly putting the defaults back — the one
/// thing worse than an empty palette is one that refuses to stay empty.
/// </para>
/// </summary>
public sealed class ColorSwatchConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.ColorSwatch;

    public ColorSwatchConfig()
    {
        foreach (var seed in SwatchPalette.Default)
        {
            Swatches.Add(new SwatchItem { Value = seed.Value, Name = seed.Name });
        }
    }

    /// <summary>The palette, in the order it is shown.</summary>
    public List<SwatchItem> Swatches { get; set; } = [];

    /// <summary>Which notation a click puts on the clipboard.</summary>
    public ColorNotation CopyAs { get; set; } = ColorNotation.Hex;

    /// <summary>Show each colour's name beside its chip.</summary>
    public bool ShowNames { get; set; } = true;

    /// <summary>Show the value that would be copied, in the notation that would be copied.</summary>
    public bool ShowValues { get; set; } = true;

    /// <summary>
    /// The palette as colours, with entries that do not name one dropped. This is the read side of
    /// the trust boundary: the list comes off disk and out of a text box, and neither is trusted.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<Swatch> EffectiveSwatches
        => SwatchPalette.Read([.. Swatches.Select(s => new SwatchSeed(s.Value, s.Name))], CopyAs);

    public override WidgetConfig Clone()
    {
        // Not the seeding constructor: a clone is a copy of THIS palette, and starting from the
        // defaults and appending would double every swatch.
        var copy = new ColorSwatchConfig
        {
            CopyAs = CopyAs,
            ShowNames = ShowNames,
            ShowValues = ShowValues,
        };

        copy.Swatches.Clear();

        foreach (var swatch in Swatches)
        {
            copy.Swatches.Add(swatch.Clone());
        }

        return copy;
    }
}
