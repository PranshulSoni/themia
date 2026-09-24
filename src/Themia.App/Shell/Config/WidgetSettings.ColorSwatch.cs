using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Widgets;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>
/// The palette editor: the colours themselves, and what a click copies.
/// <para>
/// The list is built here rather than in the widget because adding, removing and reordering are
/// editing, and the widget is for reading. The rows are rebuilt in place after a structural change
/// — the settings page is built once and never re-runs — so the list on screen is always the list
/// in the config rather than a copy of it that has drifted.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    public static void ColorSwatch(List<FrameworkElement> rows, ColorSwatchConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Colours"));

        FluentLayout.ListEditor(rows, new ListEditorOptions<SwatchItem>
        {
            Items = config.Swatches,
            Save = save,
            EmptyText = "This palette is empty. The widget will say so until a colour is added.",
            AddTitle = "Add a colour",
            AddDescription = "Hex, rgb() or hsl(), with a name if you want one",
            AddAction = "Add",
            RemoveTooltip = "Remove colour",
            Capacity = SwatchPalette.MaxSwatches,

            // A new row starts as a real colour rather than as a blank the widget would drop on
            // sight: an empty entry would simply vanish from the palette and read as a bug.
            Create = () => new SwatchItem { Value = "#0078D4", Name = string.Empty },
            Body = item => SwatchBody(item, save),
        });

        rows.Add(FluentLayout.Row(Glyphs.Rename, "Show names", "The name beside each colour",
            FluentLayout.Toggle(config.ShowNames, value => { config.ShowNames = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Preview, "Show values", "The text a click will copy",
            FluentLayout.Toggle(config.ShowValues, value => { config.ShowValues = value; save(); })));

        rows.Add(FluentLayout.Header("Behaviour"));

        rows.Add(FluentLayout.Row(Glyphs.Copy, "Copy as", ColorFormats.Describe(config.CopyAs),
            FluentLayout.Choice(
                [
                    (ColorNotation.Hex, "Hex"),
                    (ColorNotation.Rgb, "RGB"),
                    (ColorNotation.Hsl, "HSL"),
                ],
                config.CopyAs,
                value => { config.CopyAs = value; save(); })));
    }

    /// <summary>One editable swatch: its colour and its name.</summary>
    private static FrameworkElement SwatchBody(SwatchItem item, Action save)
    {
        var chip = DeskColorChip(item.Value, 28);
        chip.Margin = new Thickness(0, 0, Fluent.Space3, 0);
        Grid.SetColumn(chip, 0);

        var value = new TextBox
        {
            Text = item.Value,
            MinWidth = 130,
            PlaceholderText = "#0078D4",
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTipService.SetToolTip(value, "Hex, rgb() or hsl()");
        Grid.SetColumn(value, 1);

        var name = new TextBox
        {
            Text = item.Name,
            MinWidth = 110,
            PlaceholderText = "Name",
            Margin = new Thickness(Fluent.Space2, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetColumn(name, 2);

        // Written on blur rather than per keystroke: a half-typed "#00" is not a colour, and saving
        // it would empty the swatch on the desktop between one character and the next.
        value.LostFocus += (_, _) =>
        {
            item.Value = value.Text.Trim();
            save();
            PaintChip(chip, item.Value);
        };

        name.LostFocus += (_, _) =>
        {
            item.Name = name.Text.Trim();
            save();
        };

        PaintChip(chip, item.Value);

        return new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
            Children = { chip, value, name },
        };
    }

    /// <summary>
    /// Show what the entry actually is. A value that does not name a colour gets the disabled fill
    /// and says so, rather than a black square that looks like a deliberate colour — the widget
    /// drops the entry, and settings is the only place that can explain why.
    /// </summary>
    private static void PaintChip(Border chip, string? hex)
    {
        if (ColorFormats.TryParse(hex, out var color))
        {
            chip.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, color.R, color.G, color.B));
            ToolTipService.SetToolTip(chip, ColorFormats.Format(color, ColorNotation.Hex));
            return;
        }

        chip.Background = FluentLayout.Themed("ControlAltFillColorDisabledBrush", Colors.Transparent);
        ToolTipService.SetToolTip(chip, "Not a colour; this one is left out of the palette");
    }
}
