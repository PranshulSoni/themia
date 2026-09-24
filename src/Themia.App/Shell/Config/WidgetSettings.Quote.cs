using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Widgets;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>
/// The quote widget's settings: how often it turns over, and the list it turns over.
/// <para>
/// There is nothing here about a source, a feed or an account, because there is no network in this
/// widget at all: the list is the user's own, seeded with a bundled set they can edit or throw away.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    public static void Quote(List<FrameworkElement> rows, QuoteConfig config, Action save)
    {
        rows.Add(FluentLayout.Header("Rotation"));

        rows.Add(FluentLayout.Row(Glyphs.Clock, "Changes", "Or whenever the widget is clicked",
            FluentLayout.Choice(
                [
                    (QuoteCadence.Manual, "On click only"),
                    (QuoteCadence.Hourly, "Every hour"),
                    (QuoteCadence.Daily, "Every day"),
                ],
                config.Cadence,
                value => { config.Cadence = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Sort, "Shuffle",
            "Off walks the list in order; on picks an unrelated one each turn",
            FluentLayout.Toggle(config.Shuffle, value => { config.Shuffle = value; save(); })));

        // The shuffle is a pure function of a seed, so "reshuffle" is a new seed and nothing else.
        // That is what makes the same quote come back on the same day on every machine.
        var reshuffle = new Button { Content = "Reshuffle" };
        reshuffle.Click += (_, _) =>
        {
            config.Seed = unchecked(config.Seed + 1);
            save();
        };

        rows.Add(FluentLayout.Row(Glyphs.Reset, "Reshuffle", "Deal the list out in a different order", reshuffle));

        rows.Add(FluentLayout.Row(Glyphs.Contact, "Show author", null,
            FluentLayout.Toggle(config.ShowAuthor, value => { config.ShowAuthor = value; save(); })));

        rows.Add(FluentLayout.Header("Quotes"));

        var list = new StackPanel { Spacing = Fluent.Space1 };
        var add = new Button { Content = "Add a quote" };

        void Refill()
        {
            list.Children.Clear();

            if (config.Quotes.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = "There are no quotes left. The widget will say so until one is added.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = FluentLayout.Themed("TextFillColorSecondaryBrush", Colors.Gray),
                    Margin = new Thickness(0, Fluent.Space1, 0, Fluent.Space1),
                });
            }

            for (var i = 0; i < config.Quotes.Count; i++)
            {
                list.Children.Add(QuoteEditorRow(config, i, save, Refill));
            }

            add.IsEnabled = config.Quotes.Count < QuoteRotation.MaxQuotes;
        }

        add.Click += (_, _) =>
        {
            if (config.Quotes.Count >= QuoteRotation.MaxQuotes)
            {
                return;
            }

            config.Quotes.Add(new QuoteItem());
            save();
            Refill();
        };

        Refill();

        rows.Add(list);
        rows.Add(add);
    }

    /// <summary>
    /// One quote: the line on its own row because a quote is a sentence and not a field, with the
    /// attribution and the ordering commands under it.
    /// </summary>
    private static Border QuoteEditorRow(QuoteConfig config, int index, Action save, Action refill)
    {
        var item = config.Quotes[index];

        var text = new TextBox
        {
            Text = item.Text,
            PlaceholderText = "Something worth rereading",
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = false,
            IsSpellCheckEnabled = false,
        };

        // Written on blur, not per keystroke: the widget is on the desktop behind this window and
        // would otherwise redraw a half-typed sentence on every character.
        text.LostFocus += (_, _) =>
        {
            item.Text = text.Text.Trim();
            save();
        };

        var author = new TextBox
        {
            Text = item.Author,
            PlaceholderText = "Who said it",
            VerticalAlignment = VerticalAlignment.Center,
        };

        author.LostFocus += (_, _) =>
        {
            item.Author = author.Text.Trim();
            save();
        };

        Grid.SetColumn(author, 0);

        var up = SmallCommand(Glyphs.MoveUp, "Move up");
        up.IsEnabled = index > 0;
        up.Click += (_, _) =>
        {
            if (DeskListOrder.Move(config.Quotes, index, -1))
            {
                save();
                refill();
            }
        };

        var down = SmallCommand(Glyphs.MoveDown, "Move down");
        down.IsEnabled = index < config.Quotes.Count - 1;
        down.Click += (_, _) =>
        {
            if (DeskListOrder.Move(config.Quotes, index, 1))
            {
                save();
                refill();
            }
        };

        var remove = SmallCommand(Glyphs.Delete, "Remove");
        remove.Click += (_, _) =>
        {
            if (index < 0 || index >= config.Quotes.Count)
            {
                return;
            }

            config.Quotes.RemoveAt(index);
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

        Grid.SetColumn(commands, 1);

        var bottom = new Grid
        {
            Margin = new Thickness(0, Fluent.Space2, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            Children = { author, commands },
        };

        return new Border
        {
            Child = new StackPanel { Children = { text, bottom } },
            Padding = new Thickness(Fluent.Space4, Fluent.Space3, Fluent.Space4, Fluent.Space3),
            CornerRadius = new CornerRadius(Fluent.ControlRadius),
            Background = FluentLayout.Themed("CardBackgroundFillColorDefaultBrush", Colors.Transparent),
            BorderBrush = FluentLayout.Themed("CardStrokeColorDefaultBrush", Colors.Gray),
            BorderThickness = new Thickness(Fluent.Hairline),
        };
    }
}
