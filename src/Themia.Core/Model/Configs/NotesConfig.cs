using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>A free-text note. The body lives in the config, so it travels with copy/paste and backup.</summary>
public sealed class NotesConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Notes;

    /// <summary>The note itself.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>[S-03] Base text size in device-independent pixels, before the global text scale.</summary>
    public double FontSizePx { get; set; } = 13;

    /// <summary>Wrap long lines rather than scrolling sideways.</summary>
    public bool WrapText { get; set; } = true;

    /// <summary>Clamped on read: a persisted 0 or a NaN would render nothing at all. [S-05]</summary>
    [JsonIgnore]
    public double EffectiveFontSizePx => double.IsFinite(FontSizePx) ? Math.Clamp(FontSizePx, 8, 48) : 13;

    public override WidgetConfig Clone() => new NotesConfig
    {
        Text = Text,
        FontSizePx = FontSizePx,
        WrapText = WrapText,
    };
}
