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

/// <summary>[S-1500] What a colour swatch is: a block of one colour, a gradient, or a palette.</summary>
public enum SwatchMode
{
    /// <summary>The original widget: the whole tile in one colour, at an opacity. The default.</summary>
    Solid,

    /// <summary>The whole tile as a two-stop linear gradient at an angle.</summary>
    Gradient,

    /// <summary>A list of named colours to copy from.</summary>
    Palette,
}

/// <summary>
/// [S-1500] A colour swatch. By default the original's decorative block — the whole tile in one
/// colour, used as a backdrop or a coloured panel — or that block as a gradient, or a palette you can
/// copy colours out of.
/// <para>
/// Colours are stored <c>#RRGGBB</c> as the shared colour picker writes them; empty means "the
/// accent", the original's default being the app's own accent. The palette is the user's own list,
/// seeded with a usable one so switching to it shows something. An empty list is therefore a
/// deliberate empty palette rather than "not configured yet", and the widget says so instead of
/// quietly putting the defaults back.
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

    /// <summary>[S-1500] Solid by default, as the original.</summary>
    public SwatchMode Mode { get; set; } = SwatchMode.Solid;

    /// <summary>[S-1501] The solid colour; empty = the accent.</summary>
    public string Color { get; set; } = string.Empty;

    /// <summary>[S-1501] The solid colour's opacity, 0..1.</summary>
    public double Alpha { get; set; } = 1;

    /// <summary>[S-1501] The gradient's first stop; empty = the accent.</summary>
    public string StartColor { get; set; } = string.Empty;

    public double StartAlpha { get; set; } = 1;

    /// <summary>[S-1501] The gradient's last stop.</summary>
    public string EndColor { get; set; } = SwatchFill.DefaultEndColor;

    public double EndAlpha { get; set; } = 1;

    /// <summary>[S-1503] CSS-style degrees: 0 up, 90 right, clockwise. 135 = top-left to bottom-right.</summary>
    public double Angle { get; set; } = GradientAxis.DefaultAngle;

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

    /// <summary>
    /// [S-1502] Bring a hand-edited file back inside the swatch's invariants: an unknown mode is the
    /// default block, a colour that is not <c>#RRGGBB</c> is the default for its slot, opacities are
    /// 0..1 and the angle is 0..360.
    /// </summary>
    public override IReadOnlyList<string> Repair()
    {
        var notes = new List<string>();

        if (!Enum.IsDefined(Mode))
        {
            notes.Add($"{Kind}: mode {(int)Mode} is unknown; showing a solid colour");
            Mode = SwatchMode.Solid;
        }

        Color = RepairColor(Color, string.Empty, nameof(Color), notes);
        StartColor = RepairColor(StartColor, string.Empty, nameof(StartColor), notes);
        EndColor = RepairColor(EndColor, SwatchFill.DefaultEndColor, nameof(EndColor), notes);

        Alpha = RepairAlpha(Alpha, nameof(Alpha), notes);
        StartAlpha = RepairAlpha(StartAlpha, nameof(StartAlpha), notes);
        EndAlpha = RepairAlpha(EndAlpha, nameof(EndAlpha), notes);

        var angle = GradientAxis.NormaliseAngle(Angle);

        if (!angle.Equals(Angle))
        {
            notes.Add($"{Kind}: angle {Angle} brought into 0..360 as {angle}");
            Angle = angle;
        }

        return notes;
    }

    private string RepairColor(string? value, string fallback, string name, List<string> notes)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? fallback;
        }

        var normal = HexColor.Normalise(value);

        if (normal.Length == 0)
        {
            notes.Add($"{Kind}: {name} '{value}' is not a colour; using the default");
            return fallback;
        }

        return normal;
    }

    private double RepairAlpha(double value, string name, List<string> notes)
    {
        var clamped = SwatchFill.ClampAlpha(value);

        if (!clamped.Equals(value))
        {
            notes.Add($"{Kind}: {name} {value} brought into 0..1 as {clamped}");
        }

        return clamped;
    }

    public override WidgetConfig Clone()
    {
        // Not the seeding constructor's list: a clone is a copy of THIS palette, and starting from
        // the defaults and appending would double every swatch.
        var copy = new ColorSwatchConfig
        {
            Mode = Mode,
            Color = Color,
            Alpha = Alpha,
            StartColor = StartColor,
            StartAlpha = StartAlpha,
            EndColor = EndColor,
            EndAlpha = EndAlpha,
            Angle = Angle,
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
