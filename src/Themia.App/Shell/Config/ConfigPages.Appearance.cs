using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Settings;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>The Appearance page: theme, colours, borders, type, scale and the intro animation.</summary>
internal static partial class ConfigPages
{
    private static FrameworkElement BuildAppearance(AppHost host)
    {
        var design = host.State.Design;

        var rows = new List<FrameworkElement>
        {
            FluentLayout.Header("Theme"),

            FluentLayout.Row(Glyphs.Theme, "Theme", "Follows Windows unless you pick one",
                FluentLayout.Choice(
                    [(AppTheme.System, "Use system setting"), (AppTheme.Light, "Light"), (AppTheme.Dark, "Dark")],
                    design.Theme,
                    value => host.Mutate(s => s.Design.Theme = value))),

            FluentLayout.Header("Colours"),

            FluentLayout.ColorPickerRow(Glyphs.Palette, "Accent", "The colour of highlights, meters and selections",
                design.PrimaryColor, value => host.Mutate(s => s.Design.PrimaryColor = value),
                new ColorChoiceOptions { DefaultLabel = "Windows accent colour", DefaultColor = ColorChoice.WindowsAccent }),

            FluentLayout.ColorPickerRow(Glyphs.Surface, "Widget background", "The fill behind every widget",
                design.WidgetBackgroundColor, value => host.Mutate(s => s.Design.WidgetBackgroundColor = value),
                new ColorChoiceOptions { DefaultLabel = "Theme colour", Presets = ColorPresets.Surfaces }),

            FluentLayout.Row(Glyphs.Opacity, "Widget opacity", null,
                FluentLayout.Slider(design.SurfaceOpacity * 100, 20, 100, 5, "%",
                    value => host.Mutate(s => s.Design.SurfaceOpacity = value / 100))),

            FluentLayout.Header("Shape"),

            FluentLayout.Row(Glyphs.Surface, "Corner radius", null,
                FluentLayout.Slider(design.CornerRadiusPx, 0, 32, 1, "px",
                    value => host.Mutate(s => s.Design.CornerRadiusPx = value))),

            // [S-1540] One group, as Settings folds related options under one card: the style is
            // the header's own control, its width, colour and opacity fold under it.
            FluentLayout.Expander(Glyphs.Grid, "Border", "The line round every widget",
                FluentLayout.Choice(
                    [
                        (WidgetBorderStyle.None, "None"),
                        (WidgetBorderStyle.Solid, "Solid"),
                        (WidgetBorderStyle.Dashed, "Dashed"),
                        (WidgetBorderStyle.Dotted, "Dotted"),
                    ],
                    design.BorderStyle,
                    value => host.Mutate(s => s.Design.BorderStyle = value)),
                [
                    FluentLayout.Row(Glyphs.Grid, "Border width", null,
                        FluentLayout.Slider(design.BorderWidthPx, 0, 8, 1, "px",
                            value => host.Mutate(s => s.Design.BorderWidthPx = value))),

                    FluentLayout.ColorPickerRow(Glyphs.Palette, "Border colour", null,
                        design.BorderColor, value => host.Mutate(s => s.Design.BorderColor = value),
                        new ColorChoiceOptions { DefaultLabel = "Theme colour", Presets = ColorPresets.Surfaces }),

                    FluentLayout.Row(Glyphs.Opacity, "Border opacity", null,
                        FluentLayout.Slider(design.BorderOpacity * 100, 0, 100, 5, "%",
                            value => host.Mutate(s => s.Design.BorderOpacity = value / 100))),
                ]),

            FluentLayout.Header("Text"),

            FluentLayout.Row(Glyphs.Font, "Font", "Any family installed on this PC",
                FontChoice(host, design.FontFamily)),

            FluentLayout.Row(Glyphs.TextSize, "Text size", "Multiplies every widget's computed text size",
                FluentLayout.Slider(design.FontScale * 100, 50, 200, 5, "%",
                    value => host.Mutate(s => s.Design.FontScale = value / 100))),

            FluentLayout.Row(Glyphs.Scale, "Widget scale", "Scales widget content without moving the widgets",
                FluentLayout.Slider(design.UiScale * 100, 50, 300, 5, "%",
                    value => host.Mutate(s => s.Design.UiScale = value / 100))),

            FluentLayout.Header("Motion"),

            FluentLayout.Row(Glyphs.Motion, "Intro animation", "Played once when a view appears",
                FluentLayout.Choice(
                    [
                        (IntroAnimation.None, "None"),
                        (IntroAnimation.Fade, "Fade"),
                        (IntroAnimation.Scale, "Scale"),
                        (IntroAnimation.Slide, "Slide"),
                    ],
                    design.IntroAnimation,
                    value => host.Mutate(s => s.Design.IntroAnimation = value))),

            FluentLayout.Row(Glyphs.Timer, "Animation length", null,
                FluentLayout.Slider(design.IntroAnimationMs, 0, 1500, 50, "ms",
                    value => host.Mutate(s => s.Design.IntroAnimationMs = value))),
        };

        return Page("Appearance", rows);
    }

    private static ComboBox FontChoice(AppHost host, string current)
    {
        var families = new List<string> { "system", "Segoe UI", "Cascadia Mono", "Consolas", "Georgia", "Arial" };

        if (!families.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            families.Insert(1, current);
        }

        var combo = new ComboBox { MinWidth = FluentLayout.ChoiceMinWidth };
        foreach (var family in families)
        {
            combo.Items.Add(new ComboBoxItem
            {
                Content = family == "system" ? "System default" : family,
                Tag = family,
            });
        }

        combo.SelectedIndex = Math.Max(0, families.FindIndex(f => f.Equals(current, StringComparison.OrdinalIgnoreCase)));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: string family })
            {
                host.Mutate(s => s.Design.FontFamily = family);
            }
        };

        return combo;
    }
}
