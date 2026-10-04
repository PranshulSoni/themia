using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Themia.App.Widgets;
using Themia.Core.Localization;

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
    private static readonly (int CodePoint, Str Name)[] Offered =
    [
        (0xE80F, Str.Shell_Icon_Home), (0xE821, Str.Shell_Icon_Work), (0xE7FC, Str.Shell_Icon_Games), (0xE8D6, Str.Shell_Icon_Music), (0xE91B, Str.Shell_Icon_Photos), (0xE714, Str.Shell_Icon_Video),
        (0xE787, Str.Shell_Icon_Calendar), (0xE715, Str.Shell_Icon_Mail), (0xE716, Str.Shell_Icon_People), (0xEB51, Str.Shell_Icon_Heart), (0xE734, Str.Shell_Icon_Star), (0xE7C1, Str.Shell_Icon_Flag),
        (0xE718, Str.Shell_Icon_Pin), (0xE8B7, Str.Shell_Icon_Folder), (0xE8A5, Str.Shell_Icon_Document), (0xE774, Str.Shell_Icon_Web), (0xE943, Str.Shell_Icon_Code), (0xEA80, Str.Shell_Icon_Ideas),
        (0xE719, Str.Shell_Icon_Shopping), (0xE917, Str.Shell_Icon_Time), (0xE713, Str.Shell_Icon_Settings), (0xE753, Str.Shell_Icon_Cloud), (0xE9D2, Str.Shell_Icon_Charts), (0xE8EC, Str.Shell_Icon_Tag),
        (0xE8F1, Str.Shell_Icon_Library), (0xE717, Str.Shell_Icon_Phone), (0xE804, Str.Shell_Icon_Travel), (0xE709, Str.Shell_Icon_Flights), (0xE7BE, Str.Shell_Icon_Learning), (0xE8D4, Str.Shell_Icon_Tasks),
        (0xE77B, Str.Shell_Icon_Me), (0xE945, Str.Shell_Icon_Lightning),
    ];

    /// <param name="value">The current glyph, or null for the default.</param>
    /// <param name="defaultGlyph">What the default looks like.</param>
    /// <param name="onChanged">Gets the chosen glyph, or null for the default.</param>
    /// <param name="defaultText">
    /// [S-903] Text the default shows instead of <paramref name="defaultGlyph"/> — a view's initials,
    /// which is what a view without an icon really looks like in the view bar and switchers.
    /// </param>
    /// <param name="defaultName">The default cell's tooltip and accessible name.</param>
    public static DropDownButton Create(string? value, string defaultGlyph, Action<string?> onChanged, string? defaultText = null, string? defaultName = null)
    {
        ArgumentNullException.ThrowIfNull(onChanged);
        defaultName ??= Str.Common_Default.Text();

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
        choices.AddRange(Offered.Select(o => ((string?)char.ConvertFromUtf32(o.CodePoint), char.ConvertFromUtf32(o.CodePoint), o.Name.Text())));

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
