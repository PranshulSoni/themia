using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.System;

namespace Themia.App.Shell.Config;

internal static partial class WidgetSettings
{
    public static void Rss(List<FrameworkElement> rows, RssConfig config, Action save)
    {
        rows.Add(FluentLayout.Header("Feed"));

        var address = new TextBox
        {
            Text = config.FeedUrl,
            Width = 300,
            MaxLength = WebInput.MaxUrlLength,
            PlaceholderText = "https://example.com/feed.xml",
        };

        var problem = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Foreground = FluentLayout.Themed("SystemFillColorCriticalBrush", Colors.IndianRed),
        };

        void Commit()
        {
            var typed = address.Text.Trim();

            if (typed == config.FeedUrl)
            {
                return;
            }

            var checkedUrl = WebInput.Url(typed);

            // An address that will not pass the gate is refused here, where the user can see why,
            // rather than accepted and then refused again silently on every tick. It is never
            // repaired: prefixing "https://" onto "example.com/feed" would mean the widget fetches
            // something the settings do not say.
            if (typed.Length > 0 && !checkedUrl.IsValid)
            {
                problem.Text = WebInput.Explain(checkedUrl.Problem);
                problem.Visibility = Visibility.Visible;
                return;
            }

            problem.Visibility = Visibility.Collapsed;
            config.FeedUrl = typed;
            save();
        }

        address.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                args.Handled = true;
                Commit();
            }
        };

        address.LostFocus += (_, _) => Commit();

        rows.Add(FluentLayout.Row(Glyphs.Link, "Address", "An RSS or Atom feed, http or https",
            new StackPanel { Spacing = 4, Children = { address, problem } }));

        var title = new TextBox { Text = config.Title, Width = 200, PlaceholderText = "Optional" };

        title.LostFocus += (_, _) =>
        {
            var typed = title.Text.Trim();

            if (typed == config.Title)
            {
                return;
            }

            config.Title = typed;
            save();
        };

        rows.Add(FluentLayout.Row(Glyphs.Rename, "Title", "Shown instead of the feed's own title", title));

        rows.Add(FluentLayout.Header("Articles"));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Show at most",
            "A display cap, not a fetch count: a feed that publishes ten can never fill fifty",
            FluentLayout.Slider(config.EffectiveMaxItems, 5, 50, 1, string.Empty,
                value => { config.MaxItems = (int)Math.Round(value); save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Link, "Show source", "The site each article came from",
            FluentLayout.Toggle(config.ShowSource, value => { config.ShowSource = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Preview, "Show preview", "The first line of the article",
            FluentLayout.Toggle(config.ShowPreview, value => { config.ShowPreview = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Clock, "Show date", "How long ago it was published",
            FluentLayout.Toggle(config.ShowDate, value => { config.ShowDate = value; save(); })));

        AddRefreshChoice(rows, config.RefreshMinutes, value => { config.RefreshMinutes = value; save(); });
    }
}
