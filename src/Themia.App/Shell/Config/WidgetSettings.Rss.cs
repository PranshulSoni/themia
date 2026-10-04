using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.System;

namespace Themia.App.Shell.Config;

/// <summary>
/// The Rss widget's settings rows.
/// <para>
/// The address box is the trust boundary on this page: whatever is typed is fetched on every tick,
/// so it is checked by <see cref="WebInput"/> before it is written and the reason it was refused is
/// shown under the box rather than swallowed. It is never repaired into something that works.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    public static void Rss(List<FrameworkElement> rows, RssConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Feed"));

        var address = new TextBox
        {
            Text = config.FeedUrl,
            Width = 300,
            MaxLength = WebInput.MaxUrlLength,
            PlaceholderText = "https://example.com/feed.xml",
        };

        var verdict = FluentLayout.Verdict();

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
                FluentLayout.Refuse(verdict, WebInput.Explain(checkedUrl.Problem));
                return;
            }

            FluentLayout.Confirm(verdict, string.Empty);
            config.FeedUrl = typed;
            save();
        }

        // On commit, not per keystroke: a URL is only ever valid once it has been fully typed, so
        // validating each character would spend the whole time saying it is wrong.
        address.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                args.Handled = true;
                Commit();
            }
        };

        address.LostFocus += (_, _) => Commit();

        rows.Add(FluentLayout.FieldRow(Glyphs.Link, "Address",
            "An RSS or Atom feed, http or https", address, verdict));

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

        rows.Add(FluentLayout.Row(Glyphs.Link, "Show source", "The site each article came from",
            FluentLayout.Toggle(config.ShowSource, value => { config.ShowSource = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Preview, "Show preview", "The first line of the article",
            FluentLayout.Toggle(config.ShowPreview, value => { config.ShowPreview = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Clock, "Show date", "How long ago it was published",
            FluentLayout.Toggle(config.ShowDate, value => { config.ShowDate = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Articles",
            "How many the list holds. A display cap, not a fetch count: a feed that publishes ten can never fill fifty",
            FluentLayout.Slider(config.EffectiveMaxItems, 5, 50, 1, string.Empty,
                value => { config.MaxItems = (int)Math.Round(value); save(); })));

        FluentLayout.RefreshChoice(rows, config.RefreshMinutes, value => { config.RefreshMinutes = value; save(); });
    }
}
