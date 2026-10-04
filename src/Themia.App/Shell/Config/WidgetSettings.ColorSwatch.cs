using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Widgets;
using Themia.Core.Model;
using Themia.Core.Settings;
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
            AddDescription = "Pick a colour, and give it a name if you want one",
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

    /// <summary>
    /// One editable swatch: its colour, from the shared picker ([S-1549]) rather than typed, and its
    /// name. An entry written by hand as rgb() or hsl() opens on the same colour and is stored back
    /// as <c>#RRGGBB</c> once changed.
    /// </summary>
    private static FrameworkElement SwatchBody(SwatchItem item, Action save)
    {
        var start = ColorFormats.TryParse(item.Value, out var parsed)
            ? ColorFormats.Format(parsed, ColorNotation.Hex)
            : string.Empty;

        var color = Shell.ColorChoice.Create(
            start,
            value =>
            {
                item.Value = value;
                save();
            },
            new ColorChoiceOptions { Presets = ColorPresets.Surfaces, PresetsTitle = "Colours" },
            string.IsNullOrWhiteSpace(item.Name) ? "Colour" : item.Name);

        color.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(color, 0);

        var name = new TextBox
        {
            Text = item.Name,
            MinWidth = 110,
            PlaceholderText = "Name",
            Margin = new Thickness(Fluent.Space2, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetColumn(name, 1);

        name.LostFocus += (_, _) =>
        {
            item.Name = name.Text.Trim();
            save();
        };

        return new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
            Children = { color, name },
        };
    }
}
