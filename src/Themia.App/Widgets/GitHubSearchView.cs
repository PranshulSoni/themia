using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Services.Developer;
using Themia.App.Shell;
using Themia.Core.Developer;
using Themia.Core.Model;

namespace Themia.App.Widgets;

/// <summary>
/// [S-615][S-616] Issues assigned to you, or pull requests waiting on your review — one search
/// across your GitHub accounts, as a Windows 11 list.
/// <para>
/// Two lines a row where the tile has the height (title and age over the coloured number and the
/// repository), one line where it does not; the age goes before the title does on a narrow tile.
/// Double-click opens the page, as every list on the desktop does: a single stray click must not
/// send a browser anywhere.
/// </para>
/// </summary>
internal sealed partial class GitHubSearchView : Grid, IWidgetView, IWidgetHeaderContent, IWidgetSurfaceAware, IWidgetContextActions
{
    private readonly WidgetKind _kind;
    private readonly DevChrome _chrome;
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly ScrollViewer _scroll;
    private readonly List<IDisposable> _hovers = [];

    private IGitHubSource? _source;
    private IDevFeed<IReadOnlyList<SearchItem>>? _feed;
    private string? _feedKey;
    private IDisposable? _clock;
    private GitHubAccountsConfig _config;
    private DevRowLayout _layout = DevRows.LayoutFor(0, 0);
    private bool _detached;

    public GitHubSearchView(WidgetKind kind)
    {
        _kind = kind;
        _config = Default();
        _chrome = new DevChrome(kind == WidgetKind.GitHubReviews ? Glyphs.PullRequest : Glyphs.Issue);

        _scroll = new ScrollViewer
        {
            Padding = Fluent.ListPadding,
            Content = _list,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };

        Children.Add(new WholeRows(_scroll, _list));
        Children.Add(_chrome.StatePanel);

        SizeChanged += OnSizeChanged;
    }

    private bool Reviews => _kind == WidgetKind.GitHubReviews;

    /// <inheritdoc />
    public FrameworkElement? HeaderTrailing => _chrome.HeaderTrailing;

    public void Bind(IWidgetSurface surface)
    {
        // ignore: the catalogue's name is the right title; the card's own label still wins.
    }

    public void Attach(Ticker ticker, WidgetConfig config)
    {
        _detached = false;
        _source = AppServices.GitHub;

        if (_source is not null)
        {
            _source.AccountsChanged += OnAccountsChanged;
        }

        ApplyConfig(config);

        // Only the ages ("4m") move between fetches; the feed itself is pushed.
        _clock = ticker.Subscribe(TimeSpan.FromMinutes(1), Render);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = Reviews
            ? config as GitHubReviewsConfig ?? new GitHubReviewsConfig()
            : config as GitHubIssuesConfig ?? new GitHubIssuesConfig();
        Reopen();
        Render();
    }

    private GitHubAccountsConfig Default() => Reviews ? new GitHubReviewsConfig() : new GitHubIssuesConfig();

    private SearchQuery Query() => _config switch
    {
        GitHubReviewsConfig reviews => new SearchQuery([.. reviews.AccountIds], SearchKind.Reviews, false, reviews.IncludeDrafts, reviews.EffectiveCount, reviews.RefreshInterval),
        GitHubIssuesConfig issues => new SearchQuery([.. issues.AccountIds], SearchKind.Assigned, issues.IncludeMentioned, true, issues.EffectiveCount, issues.RefreshInterval),
        _ => new SearchQuery([], SearchKind.Assigned, false, true, 8, TimeSpan.FromMinutes(5)),
    };

    /// <summary>Open the feed this config asks for, letting go of the old one only if it changed.</summary>
    private void Reopen()
    {
        if (_source is null)
        {
            return;
        }

        var query = Query();
        if (_feed is not null && query.Key == _feedKey)
        {
            return;
        }

        CloseFeed();
        _feedKey = query.Key;
        _feed = _source.OpenSearch(query);
        _feed.Updated += Render;
    }

    private void CloseFeed()
    {
        if (_feed is null)
        {
            return;
        }

        _feed.Updated -= Render;
        _feed.Dispose();
        _feed = null;
        _feedKey = null;
    }

    private void OnAccountsChanged() => Render();

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        _chrome.Fit(args.NewSize.Height);

        if (DevRows.LayoutFor(args.NewSize.Width, args.NewSize.Height) != _layout)
        {
            Render();
        }
    }


    private void Render()
    {
        if (_detached)
        {
            return;
        }

        ClearRows();

        var now = DateTimeOffset.Now;
        var snapshot = _feed?.Snapshot ?? DevFeedSnapshot<IReadOnlyList<SearchItem>>.Empty;
        var items = snapshot.Value ?? [];
        var screen = DevScreens.Decide(
            "GitHub",
            _source?.Accounts ?? [],
            _config.AccountIds,
            single: false,
            snapshot.Outcomes,
            items.Count > 0,
            Reviews ? "No pull requests are waiting for your review." : "No open issues are assigned to you.",
            now);

        if (!_chrome.Show(screen, ActualHeight))
        {
            _scroll.Visibility = Visibility.Collapsed;
            return;
        }

        _scroll.Visibility = Visibility.Visible;
        _chrome.SetCount(items.Count);

        _layout = DevRows.LayoutFor(ActualWidth, ActualHeight);

        foreach (var item in items)
        {
            var meta = Reviews && item.Author.Length > 0 ? $"{item.Repo} · {item.Author}" : item.Repo;
            var lead = Reviews && item.Draft ? $"#{item.Number} draft" : $"#{item.Number}";

            _list.Children.Add(DevRows.Build(
                new DevRowText(
                    item.Title,
                    lead,
                    Reviews ? DevBrushes.Pull(PullState.Open, item.Draft) : DevBrushes.Success,
                    meta,
                    GitHubRules.Ago(item.UpdatedAt, now),
                    item.Draft,
                    item.HtmlUrl),
                _layout,
                _hovers));
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<WidgetAction> ContextActions => [new(Glyphs.Refresh, "Refresh", () => _feed?.Refresh())];

    private void ClearRows()
    {
        foreach (var hover in _hovers)
        {
            hover.Dispose();
        }

        _hovers.Clear();
        _list.Children.Clear();
    }

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;
        SizeChanged -= OnSizeChanged;
        _chrome.Detach();
        ClearRows();

        _clock?.Dispose();
        _clock = null;

        CloseFeed();

        if (_source is not null)
        {
            _source.AccountsChanged -= OnAccountsChanged;
            _source = null;
        }
    }
}
