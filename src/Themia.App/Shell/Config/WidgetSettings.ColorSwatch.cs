using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Widgets;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>
/// The palette editor: what a click copies, and the colours themselves.
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
        rows.Add(FluentLayout.Header("Copying"));

        rows.Add(FluentLayout.Row(Glyphs.Copy, "Copy as", ColorFormats.Describe(config.CopyAs),
            FluentLayout.Choice(
                [
                    (ColorNotation.Hex, "Hex"),
                    (ColorNotation.Rgb, "RGB"),
                    (ColorNotation.Hsl, "HSL"),
                ],
                config.CopyAs,
                value => { config.CopyAs = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Rename, "Show names", "The name beside each colour",
            FluentLayout.Toggle(config.ShowNames, value => { config.ShowNames = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Preview, "Show values", "The text a click will copy",
            FluentLayout.Toggle(config.ShowValues, value => { config.ShowValues = value; save(); })));

        rows.Add(FluentLayout.Header("Colours"));

        var list = new StackPanel { Spacing = Fluent.Space1 };
        var add = new Button { Content = "Add a colour" };

        void Refill()
        {
            list.Children.Clear();

            if (config.Swatches.Count == 0)
            {
                var note = new TextBlock
                {
                    Text = "This palette is empty. The widget will say so until a colour is added.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray),
                    Margin = new Thickness(0, Fluent.Space1, 0, Fluent.Space1),
                };

                list.Children.Add(note);
            }

            for (var i = 0; i < config.Swatches.Count; i++)
            {
                list.Children.Add(SwatchEditorRow(config, i, save, Refill));
            }

            add.IsEnabled = config.Swatches.Count < SwatchPalette.MaxSwatches;
        }

        add.Click += (_, _) =>
        {
            if (config.Swatches.Count >= SwatchPalette.MaxSwatches)
            {
                return;
            }

            // A new row starts as a real colour rather than as a blank the widget would drop on
            // sight: an empty entry would simply vanish from the palette and read as a bug.
            config.Swatches.Add(new SwatchItem { Value = "#0078D4", Name = string.Empty });
            save();
            Refill();
        };

        Refill();

        rows.Add(list);
        rows.Add(add);
    }

    /// <summary>
    /// One editable swatch: its colour, its name, and where it sits in the list.
    /// <para>
    /// Shaped like a settings row on purpose — the same padding, radius, fill and stroke as
    /// <see cref="FluentLayout.Row"/> — so a list of them reads as part of the page rather than as
    /// a table dropped into it.
    /// </para>
    /// </summary>
    private static Border SwatchEditorRow(ColorSwatchConfig config, int index, Action save, Action refill)
    {
        var item = config.Swatches[index];

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

        var up = SmallCommand(Glyphs.MoveUp, "Move up");
        up.IsEnabled = index > 0;
        up.Click += (_, _) =>
        {
            if (DeskListOrder.Move(config.Swatches, index, -1))
            {
                save();
                refill();
            }
        };

        var down = SmallCommand(Glyphs.MoveDown, "Move down");
        down.IsEnabled = index < config.Swatches.Count - 1;
        down.Click += (_, _) =>
        {
            if (DeskListOrder.Move(config.Swatches, index, 1))
            {
                save();
                refill();
            }
        };

        var remove = SmallCommand(Glyphs.Delete, "Remove");
        remove.Click += (_, _) =>
        {
            if (index < 0 || index >= config.Swatches.Count)
            {
                return;
            }

            config.Swatches.RemoveAt(index);
            save();
            refill();
        };

        var commands = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space1,
            Margin = new Thickness(Fluent.Space2, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { up, down, remove },
        };

        Grid.SetColumn(commands, 3);

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            Children = { chip, value, name, commands },
        };

        PaintChip(chip, item.Value);

        return new Border
        {
            Child = grid,
            Padding = new Thickness(Fluent.Space4, Fluent.Space3, Fluent.Space4, Fluent.Space3),
            CornerRadius = new CornerRadius(Fluent.ControlRadius),
            Background = FluentLayout.Themed("CardBackgroundFillColorDefaultBrush", Colors.Transparent),
            BorderBrush = FluentLayout.Themed("CardStrokeColorDefaultBrush", Colors.Gray),
            BorderThickness = new Thickness(Fluent.Hairline),
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

    /// <summary>A compact icon command for a list row: the stock subtle fill, nothing at rest.</summary>
    private static Button SmallCommand(string glyph, string tooltip)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = Fluent.IconSize },
            Padding = new Thickness(Fluent.Space2, Fluent.Space1, Fluent.Space2, Fluent.Space1),
            MinWidth = 0,
            CornerRadius = new CornerRadius(Fluent.ControlRadius),
        };

        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }
}
