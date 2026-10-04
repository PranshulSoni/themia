using Microsoft.UI;
using Microsoft.UI.Dispatching;
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

/// <summary>
/// The Appearance page: a style preset, theme, colours, shape, border, type, scale and the intro
/// animation. Every setting here takes effect live through the appearance engine
/// (<see cref="DesignApplier"/>, <see cref="WidgetCard.ApplyDesign"/>). [S-820]..[S-834]
/// </summary>
internal static partial class ConfigPages
{
    /// <summary>The value the style choice uses for "Custom": no preset matches the design.</summary>
    private const int CustomPreset = -1;

    private static FrameworkElement BuildAppearance(AppHost host)
    {
        var page = Page("Appearance", []);
        FillAppearance(page, host);
        return page;
    }

    /// <summary>
    /// The rows, built into <paramref name="page"/>. A preset changes a dozen settings at once, so
    /// picking one rebuilds the rows in place to show the new values; nothing else does, so a
    /// slider is never torn down under the pointer.
    /// </summary>
    private static void FillAppearance(SettingsPage page, AppHost host)
    {
        var design = host.State.Design;
        page.Children.Clear();

        var rows = new List<FrameworkElement>
        {
            FluentLayout.Header("Style"),

            // [S-834] One pick sets the look; changing anything it owns afterwards reads as Custom.
            FluentLayout.Row(Glyphs.Palette, "Style", "Sets theme, colours, font, border and shape together. Text size and widget scale stay yours",
                PresetChoice(page, host)),

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

            // [S-828] The header strip, on its own terms, as in the original.
            FluentLayout.ColorPickerRow(Glyphs.Surface, "Header background", "The strip with the widget's name. The default shares the widget's fill",
                design.HeaderBackgroundColor, value => host.Mutate(s => s.Design.HeaderBackgroundColor = value),
                new ColorChoiceOptions { DefaultLabel = "Same as the widget", Presets = ColorPresets.Surfaces }),

            FluentLayout.Row(Glyphs.Opacity, "Header opacity", "Applies to a header background of its own",
                FluentLayout.Slider(design.HeaderOpacity * 100, 0, 100, 5, "%",
                    value => host.Mutate(s => s.Design.HeaderOpacity = value / 100))),

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

            FluentLayout.Row(Glyphs.Font, "Font", "Any family installed on this PC. Icons keep their own font",
                FontChoice(host, design.FontFamily)),

            FluentLayout.Row(Glyphs.TextSize, "Text size", "Every widget's text, along the same type ramp, so layouts still fit",
                FluentLayout.Slider(design.FontScale * 100, FluentType.MinTextScale * 100, FluentType.MaxTextScale * 100, 5, "%",
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

        page.With(rows);
    }

    /// <summary>
    /// [S-834] The presets, plus "Custom" while the design matches none of them. Choosing one writes
    /// its fields in one mutation and rebuilds the page so every row shows them.
    /// </summary>
    private static ComboBox PresetChoice(SettingsPage page, AppHost host)
    {
        var match = DesignPresets.Match(host.State.Design);
        var options = new List<(int Value, string Label)>();

        foreach (var preset in DesignPresets.All)
        {
            options.Add(((int)preset.Id, preset.Label));
        }

        if (match is null)
        {
            options.Add((CustomPreset, "Custom"));
        }

        return FluentLayout.Choice(options, match is { } id ? (int)id : CustomPreset, value =>
        {
            if (value == CustomPreset || !Enum.IsDefined((DesignPresetId)value))
            {
                return;
            }

            host.Mutate(s => DesignPresets.Apply(s.Design, (DesignPresetId)value));

            // After the selection change has finished: rebuilding inside it would tear down the
            // combo box that is still raising the event.
            if (page.DispatcherQueue is not { } queue || !queue.TryEnqueue(() => FillAppearance(page, host)))
            {
                Log.Warn(Scope, "could not refresh the Appearance page after a preset; reopen it to see the values");
            }
        });
    }

    /// <summary>
    /// [S-827] The font picker: the four presets, then every installed family. The installed list
    /// arrives off the UI thread; until it does the current family is listed on its own so the
    /// choice shows what is set.
    /// </summary>
    private static ComboBox FontChoice(AppHost host, string current)
    {
        var combo = new ComboBox { MinWidth = FluentLayout.ChoiceMinWidth, MaxDropDownHeight = 420 };
        var filling = true;

        void Add(string family, string label) => combo.Items.Add(new ComboBoxItem { Content = label, Tag = family });

        Add(DesignLook.SystemFont, "System default");
        Add(DesignLook.SansFont, "Sans serif");
        Add(DesignLook.SerifFont, "Serif");
        Add(DesignLook.MonoFont, "Monospace");

        var listedOwn = !DesignLook.FontPresets.Contains(current, StringComparer.OrdinalIgnoreCase) && DesignLook.IsFamilyName(current);
        if (listedOwn)
        {
            Add(current, current);
        }

        void Select()
        {
            for (var i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem { Tag: string family } && family.Equals(current, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }

            combo.SelectedIndex = 0;
        }

        Select();
        filling = false;

        combo.SelectionChanged += (_, _) =>
        {
            if (!filling && combo.SelectedItem is ComboBoxItem { Tag: string family })
            {
                current = family;
                host.Mutate(s => s.Design.FontFamily = family);
            }
        };

        var queue = DispatcherQueue.GetForCurrentThread();
        var own = current;
        _ = SystemFonts.LoadAsync().ContinueWith(
            task =>
            {
                if (!task.IsCompletedSuccessfully)
                {
                    Log.Warn(Scope, "the installed font list could not be read; only the presets are offered", task.Exception);
                    return;
                }

                if (queue is null || !queue.TryEnqueue(() =>
                {
                    filling = true;
                    try
                    {
                        foreach (var family in task.Result)
                        {
                            // The family already listed on its own is not listed twice.
                            if (!(listedOwn && family.Equals(own, StringComparison.OrdinalIgnoreCase)))
                            {
                                Add(family, family);
                            }
                        }

                        Select();
                    }
                    finally
                    {
                        filling = false;
                    }
                }))
                {
                    Log.Warn(Scope, "could not list the installed fonts on the settings thread");
                }
            },
            TaskScheduler.Default);

        return combo;
    }
}
