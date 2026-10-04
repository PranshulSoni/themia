using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Themia.App.Widgets;
using Themia.Core.Diagnostics;
using Themia.Core.Localization;
using Themia.Core.Settings;
using Themia.Core.Widgets;

namespace Themia.App.Shell;

/// <summary>What one <see cref="ColorChoice"/> offers.</summary>
internal sealed class ColorChoiceOptions
{
    /// <summary>The swatches, in rows of <see cref="ColorPresets.Columns"/>. Windows' accent palette by default.</summary>
    public IReadOnlyList<ColorPreset> Presets { get; init; } = ColorPresets.Windows;

    /// <summary>The heading over the swatches.</summary>
    public string PresetsTitle { get; init; } = Str.Shell_Color_WindowsColours.Text();

    /// <summary>
    /// The label of the "use the default" choice, e.g. "Windows accent colour" — or null when the
    /// setting must hold a colour (a palette entry, the pen's ink). Choosing it stores an empty
    /// value, which every colour setting reads as "follow the theme".
    /// </summary>
    public string? DefaultLabel { get; init; }

    /// <summary>
    /// What the default currently looks like, to paint its swatch (the Windows accent, the calendar's
    /// palette colour). Null when the default is the theme's own surface, which has no one colour.
    /// </summary>
    public Func<Windows.UI.Color?>? DefaultColor { get; init; }

    /// <summary>Whether "Custom colour" opens the full picker. On everywhere unless a setting is a fixed set.</summary>
    public bool AllowCustom { get; init; } = true;
}

/// <summary>
/// The one colour control in the settings: a swatch button that opens a Windows 11-style picker —
/// a "use the default" choice where empty means "follow the theme", the preset swatches as Settings
/// › Personalization › Colours lays them out, and "Custom colour" for WinUI's full
/// <see cref="ColorPicker"/>. [S-1549], [S-1550]
/// <para>
/// Colours are never typed. A hex box is a field most people cannot fill in and the rest mistype;
/// a picker can only ever produce a colour. What is stored stays <c>#RRGGBB</c> (or empty for the
/// default), so nothing that reads a colour setting changes.
/// </para>
/// <para>
/// Use <see cref="FluentLayout.ColorPickerRow"/> for a settings row, or <see cref="Create"/> where
/// the colour sits inside another control (a palette entry, a calendar row).
/// </para>
/// </summary>
internal static class ColorChoice
{
    private const string Scope = "color-choice";

    private const double SwatchSize = 32;

    /// <summary>
    /// The swatch button. <paramref name="onChanged"/> receives <c>#RRGGBB</c>, or an empty string for
    /// the default.
    /// </summary>
    /// <param name="name">What a screen reader calls the button: the setting's title.</param>
    public static DropDownButton Create(string? value, Action<string> onChanged, ColorChoiceOptions? options = null, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(onChanged);
        options ??= new ColorChoiceOptions();

        var current = HexColor.Normalise(value);

        // A setting that must hold a colour but holds none (blank, corrupt) shows its first preset
        // rather than a "default" it does not have.
        if (current.Length == 0 && options.DefaultLabel is null && options.Presets.Count > 0)
        {
            current = HexColor.Normalise(options.Presets[0].Hex);
        }

        var swatch = new Border { Width = 32, Height = 20, VerticalAlignment = VerticalAlignment.Center };
        FluentLayout.ApplyStyle(swatch, "SettingsSwatchBorderStyle");

        var label = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 150,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var button = new DropDownButton
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Fluent.Space3,
                Children = { swatch, label },
            },
            MinWidth = FluentLayout.ChoiceMinWidth,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };

        if (!string.IsNullOrEmpty(name))
        {
            AutomationProperties.SetName(button, name);
        }

        // Kept inside the settings window, like Settings' own pickers, rather than as a separate
        // top-level popup window.
        var flyout = new Flyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight, ShouldConstrainToRootBounds = true };
        button.Flyout = flyout;

        void Show()
        {
            PaintSwatch(swatch, current, options);
            label.Text = Describe(current, options);
            ToolTipService.SetToolTip(button, label.Text);
        }

        void Choose(string hex)
        {
            flyout.Hide();
            if (hex == current)
            {
                return;
            }

            current = hex;
            Show();

            try
            {
                onChanged(hex);
            }
            catch (Exception ex)
            {
                Log.Error(Scope, "saving the colour failed", ex);
            }
        }

        // Built each time it opens, so the ring sits on whatever is chosen now.
        flyout.Opening += (_, _) => flyout.Content = Panel(current, options, Choose, () =>
        {
            flyout.Hide();
            _ = CustomAsync(button, current, options, Choose);
        });

        Show();

#if DEBUG
        // The settings render draws the picker's panel in the page: a flyout on an off-screen,
        // never-activated window does not render.
        DebugPanels.AddOrUpdate(button, new Func<FrameworkElement>(() => Panel(current, options, _ => { }, () => { })));
#endif

        return button;
    }

#if DEBUG
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DropDownButton, Func<FrameworkElement>> DebugPanels = [];

    /// <summary>Debug renders only: the panel this button's flyout would show.</summary>
    internal static FrameworkElement? DebugPanel(DropDownButton button)
        => DebugPanels.TryGetValue(button, out var build) ? build() : null;
#endif

    /// <summary>"Default blue", "Custom", or the default's own label.</summary>
    internal static string Describe(string hex, ColorChoiceOptions options)
    {
        if (hex.Length == 0)
        {
            return options.DefaultLabel ?? Str.Common_None.Text();
        }

        return ColorPresets.Find(options.Presets, hex)?.Name ?? Str.Settings_Style_Preset_Custom.Text();
    }

    private static void PaintSwatch(Border swatch, string hex, ColorChoiceOptions options)
    {
        if (HexColor.TryParse(hex, out var r, out var g, out var b))
        {
            swatch.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
            return;
        }

        swatch.Background = options.DefaultColor?.Invoke() is { } fallback
            ? new SolidColorBrush(fallback)
            : FluentLayout.Themed("SolidBackgroundFillColorTertiaryBrush", Colors.Gray);
    }

    /// <summary>The flyout: the default, the swatches, and the way to a custom colour.</summary>
    private static StackPanel Panel(string current, ColorChoiceOptions options, Action<string> choose, Action custom)
    {
        var panel = new StackPanel { Spacing = Fluent.Space3, Width = (SwatchSize * ColorPresets.Columns) + (Fluent.Space1 * (ColorPresets.Columns - 1)) };

        if (options.DefaultLabel is { } defaultLabel)
        {
            var preview = new Border { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center };
            FluentLayout.ApplyStyle(preview, "SettingsSwatchBorderStyle");
            PaintSwatch(preview, string.Empty, options);

            var check = new FontIcon
            {
                Glyph = Glyphs.Done,
                FontSize = 12,
                Visibility = current.Length == 0 ? Visibility.Visible : Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var text = new TextBlock { Text = defaultLabel, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 1);
            Grid.SetColumn(check, 2);

            var useDefault = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new Grid
                {
                    ColumnSpacing = Fluent.Space3,
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Auto },
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                        new ColumnDefinition { Width = GridLength.Auto },
                    },
                    Children = { preview, text, check },
                },
            };

            AutomationProperties.SetName(useDefault, defaultLabel);
            useDefault.Click += (_, _) => choose(string.Empty);
            panel.Children.Add(useDefault);
        }

        if (options.Presets.Count > 0)
        {
            panel.Children.Add(Caption(options.PresetsTitle));
            panel.Children.Add(Swatches(current, options.Presets, choose));
        }

        if (options.AllowCustom)
        {
            var row = new Grid
            {
                ColumnSpacing = Fluent.Space3,
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
            };

            // A custom colour in use is shown, ringed, beside the way to change it.
            var isCustom = current.Length > 0 && ColorPresets.Find(options.Presets, current) is null;
            var mine = new Border { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center, Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed };
            FluentLayout.ApplyStyle(mine, "SettingsSwatchBorderStyle");
            PaintSwatch(mine, current, options);

            var title = new TextBlock { Text = Str.Shell_Color_Custom.Text(), VerticalAlignment = VerticalAlignment.Center };
            var view = new Button { Content = Str.Shell_Color_ViewColours.Text() };
            view.Click += (_, _) => custom();

            Grid.SetColumn(title, 1);
            Grid.SetColumn(view, 2);
            row.Children.Add(mine);
            row.Children.Add(title);
            row.Children.Add(view);
            panel.Children.Add(row);
        }

        return panel;
    }

    private static TextBlock Caption(string text)
    {
        var caption = new TextBlock { Text = text };
        FluentLayout.ApplyStyle(caption, "SettingsCaptionTextBlockStyle");
        return caption;
    }

    /// <summary>The swatch grid, rows of eight; the chosen one carries a check in a colour that reads on it.</summary>
    private static Grid Swatches(string current, IReadOnlyList<ColorPreset> presets, Action<string> choose)
    {
        var grid = new Grid { RowSpacing = Fluent.Space1, ColumnSpacing = Fluent.Space1 };

        for (var c = 0; c < ColorPresets.Columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SwatchSize) });
        }

        for (var r = 0; r < (presets.Count + ColorPresets.Columns - 1) / ColorPresets.Columns; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SwatchSize) });
        }

        for (var i = 0; i < presets.Count; i++)
        {
            var preset = presets[i];
            var hex = HexColor.Normalise(preset.Hex);
            HexColor.TryParse(hex, out var r, out var g, out var b);

            var chosen = hex == current;
            var fill = new Border
            {
                CornerRadius = new CornerRadius(Fluent.ControlRadius),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b)),
                Child = chosen
                    ? new FontIcon
                    {
                        Glyph = Glyphs.Done,
                        FontSize = 14,
                        FontWeight = FontWeights.Bold,

                        // The check is drawn ON the colour, so its colour is the colour's contrast,
                        // not a theme brush.
                        Foreground = new SolidColorBrush(IsLight(r, g, b) ? Colors.Black : Colors.White),
                    }
                    : null,
            };

            var swatch = new Button
            {
                Width = SwatchSize,
                Height = SwatchSize,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(Fluent.ControlRadius),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Content = fill,
            };

            if (chosen)
            {
                swatch.BorderBrush = FluentLayout.Themed("AccentFillColorDefaultBrush", Colors.DodgerBlue);
                swatch.BorderThickness = new Thickness(2);
            }

            ToolTipService.SetToolTip(swatch, preset.Name);
            AutomationProperties.SetName(swatch, preset.Name);
            swatch.Click += (_, _) => choose(hex);

            Grid.SetRow(swatch, i / ColorPresets.Columns);
            Grid.SetColumn(swatch, i % ColorPresets.Columns);
            grid.Children.Add(swatch);
        }

        return grid;
    }

    private static bool IsLight(byte r, byte g, byte b) => (0.299 * r) + (0.587 * g) + (0.114 * b) > 150;

    /// <summary>
    /// "Custom colour": WinUI's colour picker in a dialog, as Settings opens one for a custom accent.
    /// Text entry is off — the spectrum and the slider only — because a typed value is exactly what
    /// this control exists to avoid.
    /// </summary>
    private static async Task CustomAsync(FrameworkElement source, string current, ColorChoiceOptions options, Action<string> choose)
    {
        if (source.XamlRoot is not { } root)
        {
            Log.Warn(Scope, "no XamlRoot for the custom colour dialog");
            return;
        }

        Windows.UI.Color start;
        if (HexColor.TryParse(current, out var r, out var g, out var b))
        {
            start = Windows.UI.Color.FromArgb(255, r, g, b);
        }
        else
        {
            start = options.DefaultColor?.Invoke() ?? Windows.UI.Color.FromArgb(255, 0x00, 0x78, 0xD4);
        }

        var picker = new ColorPicker
        {
            Color = start,
            IsAlphaEnabled = false,
            IsHexInputVisible = false,
            IsColorChannelTextInputVisible = false,
            IsMoreButtonVisible = false,
            IsColorSliderVisible = true,
            IsColorPreviewVisible = true,
        };

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = source.ActualTheme,
            Title = Str.Shell_Color_PickCustom.Text(),
            Content = picker,
            PrimaryButtonText = Str.Common_Done.Text(),
            CloseButtonText = Str.Common_Cancel.Text(),
            DefaultButton = ContentDialogButton.Primary,
        };

        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }
        catch (Exception ex)
        {
            // ShowAsync throws when another dialog already owns the window. Nothing changes.
            Log.Error(Scope, "could not show the colour picker", ex);
            return;
        }

        var color = picker.Color;
        choose(HexColor.From(color.R, color.G, color.B));
    }

    /// <summary>The Windows accent colour, for an accent setting's "default" swatch.</summary>
    public static Windows.UI.Color? WindowsAccent()
    {
        try
        {
            return new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
        }
        catch (Exception ex)
        {
            Log.Warn(Scope, "could not read the Windows accent colour", ex);
            return null;
        }
    }
}
