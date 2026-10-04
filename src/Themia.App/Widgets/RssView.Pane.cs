using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Shell;
using Themia.Core.Localization;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// [S-1306] The reading pane of the largest tile: the selected item (the newest until one is
/// picked) with its site, its whole headline, its date, its whole summary and an Open button.
/// </summary>
internal sealed partial class RssView
{
    private readonly Border _pane = new();
    private readonly StackPanel _paneSource = new() { Orientation = Orientation.Horizontal, Spacing = Fluent.Space2 };
    private readonly TextBlock _paneHost = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly TextBlock _paneTitle = Fluent.Text(FluentType.Subtitle);
    private readonly TextBlock _paneDate = Fluent.Text(FluentType.Caption, "TextFillColorTertiaryBrush");
    private readonly TextBlock _paneSummary = Fluent.Text(FluentType.Body, "TextFillColorSecondaryBrush");
    private readonly Button _paneOpen = new();
    private readonly TextBlock _openText = new();
    private string _paneLink = string.Empty;

    private void BuildPane()
    {
        _paneTitle.TextWrapping = TextWrapping.Wrap;
        _paneSummary.TextWrapping = TextWrapping.Wrap;

        _openText.Text = Str.WidgetsC_Rss_OpenArticle.Text();
        _paneOpen.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Fluent.Space2,
            Children =
            {
                Fluent.Icon(Glyphs.OpenExternal, "TextFillColorPrimaryBrush", 14),
                _openText,
            },
        };
        _paneOpen.Margin = new Thickness(0, Fluent.Space2, 0, 0);
        _paneOpen.Click += OnPaneOpen;

        var content = new StackPanel
        {
            Spacing = Fluent.Space2,
            Children = { _paneSource, _paneTitle, _paneDate, _paneSummary, _paneOpen },
        };

        _pane.Child = new ScrollViewer
        {
            Content = content,
            Padding = new Thickness(Fluent.Space4, Fluent.Space1, Fluent.Space4, Fluent.Space4),
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };

        // A hairline between the list and the pane, as Mail and Outlook draw it.
        _pane.BorderThickness = new Thickness(Fluent.Hairline, 0, 0, 0);
        _pane.BorderBrush = FluentLayout.Themed("CardStrokeColorDefaultBrush", Colors.Gray);
        _pane.Visibility = Visibility.Collapsed;
        SetColumn(_pane, 1);
    }

    private void UpdatePane()
    {
        if (_plan.Arrangement != FeedArrangement.Reading || _merged.Entries.Count == 0)
        {
            return;
        }

        var index = _cursor is { } at && at < _merged.Entries.Count ? at : 0;
        var entry = _merged.Entries[index];
        var now = DateTimeOffset.Now;

        _paneSource.Children.Clear();

        if (_config.ShowSource && FeedAggregate.SiteHost(entry.Link) is { Length: > 0 } host)
        {
            _paneSource.Children.Add(Favicon(entry.Link));
            _paneHost.Text = host;
            _paneSource.Children.Add(_paneHost);
            _paneSource.Visibility = Visibility.Visible;
        }
        else
        {
            _paneSource.Visibility = Visibility.Collapsed;
        }

        _paneTitle.Text = entry.Title.Length > 0 ? entry.Title : Str.WidgetsC_Rss_Untitled.Text();

        if (entry.Published is { } published)
        {
            var local = published.ToLocalTime();
            _paneDate.Text = Str.WidgetsC_Rss_PaneDate.Format(
                ("date", LocaleFormat.Date(Strings.Current, DateOnly.FromDateTime(local.DateTime), DateStyle.Full)),
                ("time", LocaleFormat.Time(Strings.Current, local.DateTime)),
                ("age", FetchState.Freshness(published, now)));
            _paneDate.Visibility = Visibility.Visible;
        }
        else
        {
            _paneDate.Visibility = Visibility.Collapsed;
        }

        _paneSummary.Text = entry.Summary;
        _paneSummary.Visibility = entry.Summary.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        _paneLink = entry.Link;
        _paneOpen.Visibility = entry.Link.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPaneOpen(object sender, RoutedEventArgs args)
    {
        if (_paneLink.Length > 0)
        {
            Open(_paneLink);
        }
    }

    private void ClearPaneHooks()
    {
        // The button and its handler belong to this view and die with it; only the icon, which
        // waits on a shared download, is let go.
        _paneSource.Children.Clear();
    }
}
