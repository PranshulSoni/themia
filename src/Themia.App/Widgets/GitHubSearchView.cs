using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Services.Developer;
using Themia.App.Shell;
using Themia.Core.Developer;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// [S-615][S-616] Issues assigned to you, or pull requests waiting on your review — one search
/// across your GitHub accounts, as a Windows 11 list.
/// <para>
/// The ladder ([S-743]): the title alone on the smallest tile; then the coloured number and the
/// age; two lines a row (title and age over the number and the repository) once the tile is tall
/// enough to say more about each item than to list more of them; then, as it widens, who opened
/// it and how many comments it has, then its label chips. Filling the tile ([S-740]) the list asks
/// for the most it may hold and shows the whole rows that fit. Double-click opens the page, as
/// every list on the desktop does; right-click has the page, its link and its repository ([S-749]).
/// </para>
/// </summary>
internal sealed partial class GitHubSearchView : Grid, IWidgetView, IWidgetHeaderContent, IWidgetSurfaceAware, IWidgetContextActions, IWidgetSizeAware
{
    /// <summary>[S-743] Two-line rows from this body height: a little lower than other lists, because the repository is what makes an item findable.</summary>
    private const double TwoLineFrom = 168;

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
    private WidgetArea _area;
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

    /// <inheritdoc />
    public void ApplySize(WidgetArea area)
    {
        _area = area;
        _chrome.Fit(area.Height);

        if (DevRows.LayoutFor(area.Width, area.Height, twoLineFrom: TwoLineFrom) != _layout)
        {
            Render();
        }
    }

    private GitHubAccountsConfig Default() => Reviews ? new GitHubReviewsConfig() : new GitHubIssuesConfig();

    private SearchQuery Query() => _config switch
    {
        GitHubReviewsConfig reviews => new SearchQuery([.. reviews.AccountIds], SearchKind.Reviews, false, reviews.IncludeDrafts, reviews.FetchCount, reviews.RefreshInterval),
        GitHubIssuesConfig issues => new SearchQuery([.. issues.AccountIds], SearchKind.Assigned, issues.IncludeMentioned, true, issues.FetchCount, issues.RefreshInterval),
        _ => new SearchQuery([], SearchKind.Assigned, false, true, GitHubIssuesConfig.MaxCount, TimeSpan.FromMinutes(5)),
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
            now,
            savedAt: snapshot.SavedAt);

        if (!_chrome.Show(screen, _area.Height))
        {
            _scroll.Visibility = Visibility.Collapsed;
            return;
        }

        _scroll.Visibility = Visibility.Visible;
        _chrome.SetCount(items.Count);

        _layout = DevRows.LayoutFor(_area.Width, _area.Height, twoLineFrom: TwoLineFrom);

        foreach (var item in items)
        {
            var meta = Reviews && item.Author.Length > 0 && !_layout.Detail ? $"{item.Repo} · {item.Author}" : item.Repo;
            var lead = Reviews && item.Draft ? $"#{item.Number} draft" : $"#{item.Number}";

            _list.Children.Add(DevRows.Build(
                new DevRowText(
                    item.Title,
                    lead,
                    Reviews ? DevBrushes.Pull(PullState.Open, item.Draft) : DevBrushes.Success,
                    meta,
                    GitHubRules.Ago(item.UpdatedAt, now),
                    item.Draft,
                    item.HtmlUrl,
                    GitHubRules.ItemDetail(item.Author, item.Comments),
                    item.Labels),
                _layout,
                _hovers,
                () => Menu(item)));
        }
    }

    /// <summary>[S-749] The item's page, its link, its repository, and for a pull request the changed files.</summary>
    private MenuFlyout Menu(SearchItem item)
    {
        var menu = DevRows.LinkMenu(item.HtmlUrl, GitHubRules.RepoPageOf(item.HtmlUrl));

        if (Reviews)
        {
            menu.Items.Add(DevRows.Item(Glyphs.PullRequest, "Open changed files", () => DevRows.Open(item.HtmlUrl.TrimEnd('/') + "/files")));
        }

        return menu;
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
