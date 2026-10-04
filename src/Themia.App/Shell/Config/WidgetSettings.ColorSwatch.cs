using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Widgets;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Settings;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>
/// The colour swatch's settings: what it is (a solid block, a gradient, a palette), and that mode's
/// colours. Every colour is chosen with the shared picker ([S-1549]), never typed.
/// <para>
/// The page is built once and never re-runs, so the three modes' rows are all built and the ones
/// that do not apply are collapsed, switching with the mode. The palette's list is rebuilt in place
/// after a structural change by the list editor, so the list on screen is always the config's.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    public static void ColorSwatch(List<FrameworkElement> rows, ColorSwatchConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        var solid = new List<FrameworkElement>();
        var gradient = new List<FrameworkElement>();
        var block = new List<FrameworkElement>();
        var palette = new List<FrameworkElement>();

        void Update()
        {
            Show(solid, config.Mode == SwatchMode.Solid);
            Show(gradient, config.Mode == SwatchMode.Gradient);
            Show(block, config.Mode != SwatchMode.Palette);
            Show(palette, config.Mode == SwatchMode.Palette);
        }

        rows.Add(FluentLayout.Header(Str.Widget_ColorSwatch_Label.Text()));

        // [S-1500] One widget, three shapes; the block is the original and the default.
        rows.Add(FluentLayout.Row(Glyphs.Color, Str.WidgetsA_ColorSwatch_Settings_Mode_Label.Text(), Str.WidgetsA_ColorSwatch_Settings_Mode_Desc.Text(),
            FluentLayout.Choice(
                [
                    (SwatchMode.Solid, Str.Group_Bg_Solid.Text()),
                    (SwatchMode.Gradient, Str.ColorSwatch_ModeGradient.Text()),
                    (SwatchMode.Palette, Str.WidgetsA_ColorSwatch_Palette.Text()),
                ],
                config.Mode,
                value =>
                {
                    config.Mode = value;
                    save();
                    Update();
                })));

        // ---- solid ----------------------------------------------------------------------------

        solid.Add(FluentLayout.ColorPickerRow(Glyphs.Palette, Str.ColorSwatch_ColorLabel.Text(), Str.WidgetsA_ColorSwatch_Settings_Color_Desc.Text(),
            config.Color,
            value =>
            {
                config.Color = value;
                save();
            },
            AccentDefault()));

        solid.Add(OpacityRow(Str.Settings_Style_OpacityLabel.Text(), Str.WidgetsA_ColorSwatch_Settings_Opacity_Desc.Text(), config.Alpha, value =>
        {
            config.Alpha = value;
            save();
        }));

        // ---- gradient -------------------------------------------------------------------------

        gradient.Add(FluentLayout.ColorPickerRow(Glyphs.Palette, Str.ColorSwatch_StartColorLabel.Text(), Str.WidgetsA_ColorSwatch_Settings_Start_Desc.Text(),
            config.StartColor,
            value =>
            {
                config.StartColor = value;
                save();
            },
            AccentDefault()));

        gradient.Add(OpacityRow(Str.WidgetsA_ColorSwatch_Settings_StartOpacity_Label.Text(), null, config.StartAlpha, value =>
        {
            config.StartAlpha = value;
            save();
        }));

        gradient.Add(FluentLayout.ColorPickerRow(Glyphs.Palette, Str.ColorSwatch_EndColorLabel.Text(), Str.WidgetsA_ColorSwatch_Settings_End_Desc.Text(),
            config.EndColor,
            value =>
            {
                config.EndColor = value;
                save();
            },
            new ColorChoiceOptions()));

        gradient.Add(OpacityRow(Str.WidgetsA_ColorSwatch_Settings_EndOpacity_Label.Text(), null, config.EndAlpha, value =>
        {
            config.EndAlpha = value;
            save();
        }));

        // [S-1503] CSS's convention, as the original: 0° runs upwards, 90° to the right.
        gradient.Add(FluentLayout.Row(Glyphs.Rotate, Str.ColorSwatch_Angle_Label.Text(), Str.WidgetsA_ColorSwatch_Settings_Angle_Desc.Text(),
            FluentLayout.Slider(GradientAxis.NormaliseAngle(config.Angle), 0, 359, 1, "°", value =>
            {
                config.Angle = GradientAxis.NormaliseAngle(Math.Round(value));
                save();
            })));

        // [S-1505] The original's "layer: behind / in front" is the widget menu's Bring to front /
        // Send to back now, which every widget has; said here so it is found from the old place.
        block.Add(FluentLayout.Note(Str.WidgetsA_ColorSwatch_Settings_BackdropNote.Text()));

        // ---- palette --------------------------------------------------------------------------

        palette.Add(FluentLayout.Header(Str.WidgetsA_ColorSwatch_Palette.Text()));

        rows.AddRange(solid);
        rows.AddRange(gradient);
        rows.AddRange(block);
        rows.AddRange(palette);

        var editorStart = rows.Count;

        FluentLayout.ListEditor(rows, new ListEditorOptions<SwatchItem>
        {
            Items = config.Swatches,
            Save = save,
            EmptyText = Str.WidgetsA_ColorSwatch_Settings_PaletteEmpty.Text(),
            AddTitle = Str.WidgetsA_ColorSwatch_Settings_Add_Label.Text(),
            AddDescription = Str.WidgetsA_ColorSwatch_Settings_Add_Desc.Text(),
            AddAction = Str.Common_Add.Text(),
            RemoveTooltip = Str.WidgetsA_ColorSwatch_Settings_Remove.Text(),
            Capacity = SwatchPalette.MaxSwatches,

            // A new row starts as a real colour rather than as a blank the widget would drop on
            // sight: an empty entry would simply vanish from the palette and read as a bug.
            Create = () => new SwatchItem { Value = "#0078D4", Name = string.Empty },
            Body = item => SwatchBody(item, save),
        });

        rows.Add(FluentLayout.Row(Glyphs.Rename, Str.WidgetsA_ColorSwatch_Settings_ShowNames_Label.Text(), Str.WidgetsA_ColorSwatch_Settings_ShowNames_Desc.Text(),
            FluentLayout.Toggle(config.ShowNames, value => { config.ShowNames = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Preview, Str.WidgetsA_ColorSwatch_Settings_ShowValues_Label.Text(), Str.WidgetsA_ColorSwatch_Settings_ShowValues_Desc.Text(),
            FluentLayout.Toggle(config.ShowValues, value => { config.ShowValues = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Copy, Str.WidgetsA_ColorSwatch_Settings_CopyAs_Label.Text(), ColorFormats.Describe(config.CopyAs),
            FluentLayout.Choice(
                [
                    (ColorNotation.Hex, "Hex"),
                    (ColorNotation.Rgb, "RGB"),
                    (ColorNotation.Hsl, "HSL"),
                ],
                config.CopyAs,
                value => { config.CopyAs = value; save(); })));

        // Everything the editor and the palette rows after it added belongs to the palette.
        palette.AddRange(rows.Skip(editorStart));

        Update();
    }

    private static void Show(List<FrameworkElement> rows, bool visible)
    {
        foreach (var row in rows)
        {
            row.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>The picker's options for a colour whose default is the accent (the original's default).</summary>
    private static ColorChoiceOptions AccentDefault() => new()
    {
        DefaultLabel = Str.WidgetsA_ColorSwatch_Settings_Accent.Text(),
        DefaultColor = () => (FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue) as SolidColorBrush)?.Color,
    };

    /// <summary>An opacity, shown and set as a percentage and stored 0..1.</summary>
    private static Border OpacityRow(string title, string? description, double alpha, Action<double> onChanged)
        => FluentLayout.Row(Glyphs.Opacity, title, description,
            FluentLayout.Slider(Math.Round(SwatchFill.ClampAlpha(alpha) * 100), 0, 100, 1, "%",
                value => onChanged(SwatchFill.ClampAlpha(value / 100))));

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
            new ColorChoiceOptions { Presets = ColorPresets.Surfaces, PresetsTitle = Str.WidgetsA_ColorSwatch_Settings_Presets.Text() },
            string.IsNullOrWhiteSpace(item.Name) ? Str.ColorSwatch_ColorLabel.Text() : item.Name);

        color.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(color, 0);

        var name = new TextBox
        {
            Text = item.Name,
            MinWidth = 110,
            PlaceholderText = Str.View_Name_Label.Text(),
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
