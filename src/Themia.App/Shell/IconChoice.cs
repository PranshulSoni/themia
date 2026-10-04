using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Themia.App.Widgets;

namespace Themia.App.Shell;

/// <summary>
/// [S-863] A glyph setting: a button showing the current icon that opens a grid of Segoe Fluent
/// glyphs, with the default first. Stores the glyph itself (one character), or null for the default.
/// Built for a group's icon; a view's icon can use the same control.
/// </summary>
internal static class IconChoice
{
    private const int Columns = 8;

    /// <summary>The offered glyphs: things an area of the desktop is usually about.</summary>
    private static readonly (int CodePoint, string Name)[] Offered =
    [
        (0xE80F, "Home"), (0xE821, "Work"), (0xE7FC, "Games"), (0xE8D6, "Music"), (0xE91B, "Photos"), (0xE714, "Video"),
        (0xE787, "Calendar"), (0xE715, "Mail"), (0xE716, "People"), (0xEB51, "Heart"), (0xE734, "Star"), (0xE7C1, "Flag"),
        (0xE718, "Pin"), (0xE8B7, "Folder"), (0xE8A5, "Document"), (0xE774, "Web"), (0xE943, "Code"), (0xEA80, "Ideas"),
        (0xE719, "Shopping"), (0xE917, "Time"), (0xE713, "Settings"), (0xE753, "Cloud"), (0xE9D2, "Charts"), (0xE8EC, "Tag"),
        (0xE8F1, "Library"), (0xE717, "Phone"), (0xE804, "Travel"), (0xE709, "Flights"), (0xE7BE, "Learning"), (0xE8D4, "Tasks"),
        (0xE77B, "Me"), (0xE945, "Lightning"),
    ];

    /// <param name="value">The current glyph, or null for the default.</param>
    /// <param name="defaultGlyph">What the default looks like.</param>
    /// <param name="onChanged">Gets the chosen glyph, or null for the default.</param>
    /// <param name="defaultText">
    /// [S-903] Text the default shows instead of <paramref name="defaultGlyph"/> — a view's initials,
    /// which is what a view without an icon really looks like in the view bar and switchers.
    /// </param>
    /// <param name="defaultName">The default cell's tooltip and accessible name.</param>
    public static DropDownButton Create(string? value, string defaultGlyph, Action<string?> onChanged, string? defaultText = null, string defaultName = "Default")
    {
        ArgumentNullException.ThrowIfNull(onChanged);

        // The default's look and an icon's look differ in kind when the default is text.
        UIElement Look(string? glyph) => glyph is null && defaultText is not null
            ? new TextBlock { Text = defaultText, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }
            : new FontIcon { Glyph = glyph ?? defaultGlyph, FontSize = 16 };

        var button = new DropDownButton { Content = Look(string.IsNullOrEmpty(value) ? null : value) };
        var flyout = new Flyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };

        var grid = new Grid { RowSpacing = Fluent.Space1, ColumnSpacing = Fluent.Space1 };
        for (var c = 0; c < Columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        var choices = new List<(string? Glyph, string Shown, string Name)> { (null, defaultGlyph, defaultName) };
        choices.AddRange(Offered.Select(o => ((string?)char.ConvertFromUtf32(o.CodePoint), char.ConvertFromUtf32(o.CodePoint), o.Name)));

        for (var i = 0; i < choices.Count; i++)
        {
            var (glyph, _, name) = choices[i];
            if (i % Columns == 0)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            var cell = new ToggleButton
            {
                Content = Look(glyph),
                Width = 36,
                Height = 36,
                Padding = new Thickness(0),
                IsChecked = string.Equals(glyph, string.IsNullOrEmpty(value) ? null : value, StringComparison.Ordinal),
                Background = FluentLayout.Themed("SubtleFillColorTransparentBrush", Colors.Transparent),
                BorderThickness = new Thickness(0),
            };

            ToolTipService.SetToolTip(cell, name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(cell, name);
            cell.Click += (_, _) =>
            {
                button.Content = Look(glyph);
                foreach (var other in grid.Children.OfType<ToggleButton>())
                {
                    other.IsChecked = ReferenceEquals(other, cell);
                }

                flyout.Hide();
                onChanged(glyph);
            };

            Grid.SetRow(cell, i / Columns);
            Grid.SetColumn(cell, i % Columns);
            grid.Children.Add(cell);
        }

        flyout.Content = grid;
        button.Flyout = flyout;
        return button;
    }
}
