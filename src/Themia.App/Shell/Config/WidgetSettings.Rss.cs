using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Widgets;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.System;

namespace Themia.App.Shell.Config;

/// <summary>
/// The Rss widget's settings rows.
/// <para>
/// The address boxes are the trust boundary on this page: whatever is typed is fetched on every
/// tick, so each is checked by <see cref="WebInput"/> before it is written and the reason it was
/// refused is shown under the box rather than swallowed. It is never repaired into something that
/// works. [S-1302] Several feeds, merged newest first, as the original's feed selection.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    public static void Rss(List<FrameworkElement> rows, RssConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Feeds"));

        FluentLayout.ListEditor(rows, new ListEditorOptions<RssFeed>
        {
            Items = config.Feeds,
            Body = feed => FeedAddress(feed, save),
            Save = save,
            EmptyText = "No feeds yet. Add the address of an RSS or Atom feed.",
            AddTitle = "Add a feed",
            AddDescription = "The articles of every feed are merged, newest first",
            AddAction = "Add feed",
            Create = () => new RssFeed(),
            Capacity = RssConfig.MaxFeeds,
            RemoveTooltip = "Remove feed",
        });

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

        rows.Add(FluentLayout.Row(Glyphs.Rename, "Title",
            "Shown instead of the feed's own title (several feeds show their names joined)", title));

        rows.Add(FluentLayout.Header("Articles"));

        rows.Add(FluentLayout.Row(Glyphs.Link, "Show source",
            "The site each article came from: its icon, and its name where there is room. The icon is fetched from the site itself",
            FluentLayout.Toggle(config.ShowSource, value => { config.ShowSource = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Preview, "Show preview", "The start of the article, where the widget has room",
            FluentLayout.Toggle(config.ShowPreview, value => { config.ShowPreview = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Clock, "Show date", "How long ago it was published",
            FluentLayout.Toggle(config.ShowDate, value => { config.ShowDate = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Articles",
            "How many the list holds, across all feeds. A display cap, not a fetch count: a feed that publishes ten can never fill fifty",
            FluentLayout.Slider(config.EffectiveMaxItems, 5, 50, 1, string.Empty,
                value => { config.MaxItems = (int)Math.Round(value); save(); })));

        FluentLayout.RefreshChoice(rows, config.RefreshMinutes, value => { config.RefreshMinutes = value; save(); });
    }

    /// <summary>
    /// One feed's address box, checked on commit (Enter or leaving the box), with its verdict
    /// under it. A refused address is not written; an empty one is kept as a blank to fill in.
    /// </summary>
    private static FrameworkElement FeedAddress(RssFeed feed, Action save)
    {
        var address = new TextBox
        {
            Text = feed.Url,
            MinWidth = 260,
            MaxLength = WebInput.MaxUrlLength,
            PlaceholderText = "https://example.com/feed.xml",
        };

        var verdict = FluentLayout.Verdict();

        void Commit()
        {
            var typed = address.Text.Trim();

            if (typed == feed.Url)
            {
                return;
            }

            var checkedUrl = WebInput.Url(typed);

            // Refused here, where the user can see why, rather than accepted and then refused again
            // silently on every tick. Never repaired: prefixing "https://" onto "example.com/feed"
            // would mean the widget fetches something the settings do not say.
            if (typed.Length > 0 && !checkedUrl.IsValid)
            {
                FluentLayout.Refuse(verdict, WebInput.Explain(checkedUrl.Problem));
                return;
            }

            FluentLayout.Confirm(verdict, string.Empty);
            feed.Url = typed;
            save();
        }

        // On commit, not per keystroke: a URL is only valid once it has been fully typed.
        address.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                args.Handled = true;
                Commit();
            }
        };

        address.LostFocus += (_, _) => Commit();

        return new StackPanel { Spacing = Fluent.Space1, Children = { address, verdict } };
    }
}
