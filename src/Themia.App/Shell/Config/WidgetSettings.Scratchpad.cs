using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Widgets;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>
/// The scratchpad's settings: the pen, and whether the commands ride in the widget's header.
/// <para>
/// Clearing the drawing is <b>not</b> here. It lives on the pad itself, behind a confirmation,
/// because that is where the drawing is and because two ways to destroy the same thing is one too
/// many.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    /// <summary>
    /// The inks the pen offers. Six is a palette; a colour picker on a settings row for a pen is a
    /// dialog in front of a widget you are trying to watch.
    /// </summary>
    private static readonly (string Value, string Name)[] PenColors =
    [
        ("#0078D4", "Blue"),
        ("#F43F5E", "Rose"),
        ("#F59E0B", "Amber"),
        ("#10B981", "Emerald"),
        ("#7C3AED", "Violet"),
        ("#E7E5E4", "Chalk"),
    ];

    public static void Scratchpad(List<FrameworkElement> rows, ScratchpadConfig config, Action save)
    {
        rows.Add(FluentLayout.Header("Pen"));

        rows.Add(FluentLayout.Row(Glyphs.Palette, "Ink", "What the pen draws with",
            PenPicker(config, save)));

        // The width is in pixels and stays in pixels however the widget is resized: a pen has a
        // nib, so a bigger pad shows more drawing rather than a fatter line.
        rows.Add(FluentLayout.Row(Glyphs.Scratchpad, "Width", "How thick the line is, in pixels",
            FluentLayout.Slider(
                config.EffectivePenWidth,
                InkGeometry.MinWidth,
                InkGeometry.MaxWidth,
                1,
                "px",
                value => { config.PenWidth = value; save(); })));

        rows.Add(FluentLayout.Header("Widget"));

        rows.Add(FluentLayout.Row(Glyphs.Edit, "Show tools",
            "The eraser, undo and clear buttons in the widget's header",
            FluentLayout.Toggle(config.ShowTools, value => { config.ShowTools = value; save(); })));
    }

    /// <summary>
    /// The ink, as the colours themselves rather than as their names in a list. A dropdown reading
    /// "Emerald" tells you less about what the pen will draw than the colour does.
    /// </summary>
    private static StackPanel PenPicker(ScratchpadConfig config, Action save)
    {
        var strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space2,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var chips = new List<(Border Chip, string Value)>();

        foreach (var (value, name) in PenColors)
        {
            var chip = DeskColorChip(value, 24);
            ToolTipService.SetToolTip(chip, name);

            chips.Add((chip, value));

            chip.PointerPressed += (_, _) =>
            {
                config.PenColor = value;
                save();
                MarkPen(chips, config.EffectivePenColor);
            };

            strip.Children.Add(chip);
        }

        MarkPen(chips, config.EffectivePenColor);
        return strip;
    }

    /// <summary>Ring the chosen ink with the accent, the way Windows marks a chosen colour.</summary>
    private static void MarkPen(List<(Border Chip, string Value)> chips, string chosen)
    {
        var accent = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);
        var resting = FluentLayout.Themed("ControlStrokeColorDefaultBrush", Colors.Gray);

        foreach (var (chip, value) in chips)
        {
            var selected = string.Equals(
                HexColor.Normalise(value),
                HexColor.Normalise(chosen),
                StringComparison.OrdinalIgnoreCase);

            chip.BorderBrush = selected ? accent : resting;
            chip.BorderThickness = new Thickness(selected ? 2 : 1);
        }
    }

    /// <summary>
    /// A colour chip. The fill is a literal colour because it <i>is</i> the content — an ink or a
    /// swatch — while its border comes from a named brush, so the chrome round it follows the theme.
    /// <para>
    /// Shared by the scratchpad's pen and the palette editor, which are the two settings pages that
    /// show a colour rather than name one.
    /// </para>
    /// </summary>
    private static Border DeskColorChip(string hex, double size)
    {
        HexColor.TryParse(hex, out var r, out var g, out var b);

        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(Fluent.ControlRadius),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b)),
            BorderBrush = FluentLayout.Themed("ControlStrokeColorDefaultBrush", Colors.Gray),
            BorderThickness = new Thickness(1),
        };
    }
}
