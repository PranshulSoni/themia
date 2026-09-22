using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Widgets;
using Themia.Core.Model;

namespace Themia.App.Shell;

/// <summary>
/// The "add a widget" gallery. A stock <see cref="ContentDialog"/> so it reads as part of Windows
/// rather than a bespoke panel.
/// </summary>
internal static class WidgetPickerDialog
{
    public static async Task<WidgetKind?> PickAsync(XamlRoot xamlRoot)
    {
        WidgetKind? chosen = null;

        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            IsItemClickEnabled = true,
            MaxHeight = 360,
        };

        foreach (var definition in WidgetCatalog.All)
        {
            list.Items.Add(new ListViewItem
            {
                Tag = definition.Kind,
                Content = new Grid
                {
                    ColumnSpacing = 12,
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Auto },
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    },
                    Children =
                    {
                        Icon(definition.Glyph),
                        Labels(definition.Label, definition.Description),
                    },
                },
            });
        }

        if (list.Items.Count > 0)
        {
            list.SelectedIndex = 0;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Add a widget",
            Content = list,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        // Double-clicking an entry is the shortcut everyone reaches for.
        list.ItemClick += (_, args) =>
        {
            if (args.ClickedItem is ListViewItem { Tag: WidgetKind kind })
            {
                chosen = kind;
                dialog.Hide();
            }
        };

        var result = await dialog.ShowAsync();

        if (chosen is not null)
        {
            return chosen;
        }

        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        return list.SelectedItem is ListViewItem { Tag: WidgetKind selected } ? selected : null;

        static FontIcon Icon(string glyph)
        {
            var icon = new FontIcon { Glyph = glyph, FontSize = 24, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(icon, 0);
            return icon;
        }

        static StackPanel Labels(string label, string description)
        {
            var panel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    new TextBlock
                    {
                        Text = description,
                        FontSize = 12,
                        Opacity = 0.7,
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            };

            Grid.SetColumn(panel, 1);
            return panel;
        }
    }
}
