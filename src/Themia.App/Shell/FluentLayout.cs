using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Themia.App.Shell;

/// <summary>
/// A hand-rolled equivalent of the Community Toolkit's settings card, built from stock Fluent theme
/// resources.
/// <para>
/// Rolled by hand rather than taken from the toolkit purely to keep the dependency surface at one
/// package: every extra assembly is more for the AOT compiler to root, and a settings row is a
/// border with two text blocks.
/// </para>
/// </summary>
internal static class FluentLayout
{
    /// <summary>A settings row: icon, title, optional description, and a control on the right.</summary>
    public static Border Row(string glyph, string title, string? description, FrameworkElement control)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        text.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });

        if (!string.IsNullOrWhiteSpace(description))
        {
            text.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 12,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        Grid.SetColumn(text, 1);

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0),
        };

        Grid.SetColumn(icon, 0);

        control.HorizontalAlignment = HorizontalAlignment.Right;
        control.VerticalAlignment = VerticalAlignment.Center;
        control.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(control, 2);

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            Children = { icon, text, control },
        };

        return new Border
        {
            Child = grid,
            Padding = new Thickness(16, 12, 16, 12),
            CornerRadius = new CornerRadius(4),
            Background = Brush("CardBackgroundFillColorDefaultBrush", Colors.Transparent),
            BorderBrush = Brush("CardStrokeColorDefaultBrush", Colors.Gray),
            BorderThickness = new Thickness(1),
        };
    }

    /// <summary>A group heading above a run of rows.</summary>
    public static TextBlock Header(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 12, 0, 0),
    };

    public static ToggleSwitch Toggle(bool value, Action<bool> onChanged)
    {
        var toggle = new ToggleSwitch { IsOn = value, OnContent = null, OffContent = null };
        toggle.Toggled += (_, _) => onChanged(toggle.IsOn);
        return toggle;
    }

    public static ComboBox Choice<T>(IReadOnlyList<(T Value, string Label)> options, T selected, Action<T> onChanged)
        where T : struct
    {
        var combo = new ComboBox { MinWidth = 150 };

        foreach (var (value, label) in options)
        {
            combo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        }

        for (var i = 0; i < options.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(options[i].Value, selected))
            {
                combo.SelectedIndex = i;
                break;
            }
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: T value })
            {
                onChanged(value);
            }
        };

        return combo;
    }

    /// <summary>A slider with a live numeric readout, used for the gap and corner sizes.</summary>
    public static StackPanel Slider(double value, double min, double max, double step, string unit, Action<double> onChanged)
    {
        var readout = new TextBlock
        {
            Text = $"{value:0}{unit}",
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 44,
            Opacity = 0.7,
        };

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            StepFrequency = step,
            Value = value,
            Width = 160,
            VerticalAlignment = VerticalAlignment.Center,
        };

        slider.ValueChanged += (_, args) =>
        {
            readout.Text = $"{args.NewValue:0}{unit}";
            onChanged(args.NewValue);
        };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { slider, readout },
        };
    }

    private static Brush Brush(string key, Windows.UI.Color fallback)
        => Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(fallback);
}
