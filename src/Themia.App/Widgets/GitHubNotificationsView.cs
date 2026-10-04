using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Themia.App.Services;
using Themia.App.Services.Developer;
using Themia.App.Shell;
using Themia.Core.Developer;
using Themia.Core.Model;

namespace Themia.App.Widgets;

/// <summary>
/// [S-617]..[S-621] Your GitHub notifications across your accounts, grouped under each repository.
/// <para>
/// A row leads with why it is there — "review", "mention", "security" — in the reason's colour;
/// a read thread is drawn in the secondary colour. Right-click a row to open it, mark it read, or
/// mark its whole repository read; the change shows at once and the feed then reads the truth.
/// </para>
/// </summary>
internal sealed partial class GitHubNotificationsView : Grid, IWidgetView, IWidgetHeaderContent, IWidgetSurfaceAware, IWidgetContextActions
{
    /// <summary>Below this body height the list is not grouped: a heading would cost one of few rows.</summary>
    private const double GroupingNeedsHeight = 160;

    private readonly DevChrome _chrome = new(Glyphs.Bell);
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly ScrollViewer _scroll;
    private readonly List<IDisposable> _hovers = [];

    /// <summary>Threads marked read here and not yet confirmed by a fetch, so the change shows at once.</summary>
    private readonly HashSet<(string AccountId, string Id)> _readHere = [];

    private IGitHubSource? _source;
    private IDevFeed<IReadOnlyList<NotificationItem>>? _feed;
    private string? _feedKey;
    private IDisposable? _clock;
    private GitHubNotificationsConfig _config = new();
    private string? _actionProblem;
    private int _changesInFlight;
    private bool _grouping = true;
    private DevRowLayout _layout = DevRows.LayoutFor(0, 0);
    private bool _detached;

    public GitHubNotificationsView()
    {
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
        _clock = ticker.Subscribe(TimeSpan.FromMinutes(1), Render);
    }

    public void ApplyConfig(WidgetConfig config)
    {
        _config = config as GitHubNotificationsConfig ?? new GitHubNotificationsConfig();
        Reopen();
        Render();
    }

    private void Reopen()
    {
        if (_source is null)
        {
            return;
        }

        var query = new NotificationQuery([.. _config.AccountIds], _config.IncludeRead, _config.ParticipatingOnly, _config.EffectiveCount, _config.RefreshInterval);
        if (_feed is not null && query.Key == _feedKey)
        {
            return;
        }

        CloseFeed();
        _feedKey = query.Key;
        _feed = _source.OpenNotifications(query);
        _feed.Updated += OnFeedUpdated;
    }

    private void CloseFeed()
    {
        if (_feed is null)
        {
            return;
        }

        _feed.Updated -= OnFeedUpdated;
        _feed.Dispose();
        _feed = null;
        _feedKey = null;
    }

    private void OnFeedUpdated()
    {
        // A fetch that landed after the last change finished has the server's word on it.
        if (_changesInFlight == 0)
        {
            _readHere.Clear();
        }

        Render();
    }

    private void OnAccountsChanged() => Render();

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        _chrome.Fit(args.NewSize.Height);

        if (DevRows.LayoutFor(args.NewSize.Width, args.NewSize.Height) != _layout || Grouping(args.NewSize.Height) != _grouping)
        {
            Render();
        }
    }


    /// <summary>
    /// [S-620] Headings cost a row each, so a short tile lists without them; the repository is in
    /// each row's tooltip there.
    /// </summary>
    private bool Grouping(double height) => _config.GroupByRepository && (height <= 0 || height >= GroupingNeedsHeight);

    /// <summary>The feed's items with what was marked read here applied.</summary>
    private List<NotificationItem> Items()
    {
        var items = new List<NotificationItem>();

        foreach (var item in _feed?.Snapshot.Value ?? [])
        {
            if (!_readHere.Contains((item.AccountId, item.Id)))
            {
                items.Add(item);
            }
            else if (_config.IncludeRead)
            {
                items.Add(item with { Unread = false });
            }
        }

        return items;
    }

    private void Render()
    {
        if (_detached)
        {
            return;
        }

        ClearRows();

        var now = DateTimeOffset.Now;
        var snapshot = _feed?.Snapshot ?? DevFeedSnapshot<IReadOnlyList<NotificationItem>>.Empty;
        var items = Items();
        var screen = DevScreens.Decide(
            "GitHub",
            _source?.Accounts ?? [],
            _config.AccountIds,
            single: false,
            snapshot.Outcomes,
            items.Count > 0,
            _config.IncludeRead ? "No notifications." : "No unread notifications.",
            now);

        if (!_chrome.Show(screen, ActualHeight, _actionProblem))
        {
            _scroll.Visibility = Visibility.Collapsed;
            return;
        }

        _scroll.Visibility = Visibility.Visible;
        _chrome.SetCount(items.Count(i => i.Unread));

        _layout = DevRows.LayoutFor(ActualWidth, ActualHeight);
        _grouping = Grouping(ActualHeight);

        // [S-620] Under each repository, once there is more than one; a single repository's name
        // goes on each row instead, where a lone heading would only cost a row.
        var groups = GitHubRules.GroupByRepository(items);
        var grouped = _grouping && groups.Count > 1;

        if (grouped)
        {
            foreach (var group in groups)
            {
                _list.Children.Add(DevRows.Section(group.Repo, group.Unread > 0 ? $"{group.Unread} unread" : null));

                foreach (var item in group.Items)
                {
                    AddRow(item, GitHubRules.SubjectLabel(item.SubjectType), now);
                }
            }
        }
        else
        {
            foreach (var item in items)
            {
                AddRow(item, item.Repo, now);
            }
        }
    }

    private void AddRow(NotificationItem item, string meta, DateTimeOffset now)
    {
        var reason = GitHubRules.ReasonLabel(item.Reason);

        // "CI · CI" says nothing twice.
        if (string.Equals(meta, reason, StringComparison.OrdinalIgnoreCase))
        {
            meta = string.Empty;
        }

        var row = DevRows.Build(
            new DevRowText(
                item.Title,
                reason,
                item.Unread ? DevBrushes.Reason(GitHubRules.ToneOf(item.Reason)) : DevBrushes.Tertiary,
                meta,
                GitHubRules.Ago(item.UpdatedAt, now),
                !item.Unread,
                item.HtmlUrl),
            _layout,
            _hovers);

        row.RightTapped += (_, args) => ShowMenu(row, item, args);
        _list.Children.Add(row);
    }

    /// <summary>[S-621] A thread's own menu: open it, mark it read, or mark its repository read.</summary>
    private void ShowMenu(FrameworkElement row, NotificationItem item, RightTappedRoutedEventArgs args)
    {
        var menu = new MenuFlyout();

        var open = new MenuFlyoutItem { Text = "Open in browser", Icon = new FontIcon { Glyph = Glyphs.OpenExternal } };
        open.Click += (_, _) => DevRows.Open(item.HtmlUrl);
        menu.Items.Add(open);
        menu.Items.Add(new MenuFlyoutSeparator());

        var read = new MenuFlyoutItem { Text = "Mark as read", Icon = new FontIcon { Glyph = Glyphs.MailRead }, IsEnabled = item.Unread };
        read.Click += (_, _) => _ = ChangeAsync([item], s => s.MarkReadAsync(item));
        menu.Items.Add(read);

        var repo = new MenuFlyoutItem { Text = $"Mark all in {item.Repo} as read", Icon = new FontIcon { Glyph = Glyphs.Done } };
        repo.Click += (_, _) => _ = ChangeAsync(
            [.. Items().Where(i => i.AccountId == item.AccountId && string.Equals(i.RepoHtmlUrl, item.RepoHtmlUrl, StringComparison.OrdinalIgnoreCase))],
            s => s.MarkRepositoryReadAsync(item));
        menu.Items.Add(repo);

        menu.ShowAt(row, args.GetPosition(row));
        args.Handled = true;
    }

    /// <summary>Show the change now, make it, and put it back with the reason if it did not take.</summary>
    private async Task ChangeAsync(IReadOnlyList<NotificationItem> affected, Func<IGitHubSource, Task<string?>> change)
    {
        if (_source is not { } source)
        {
            return;
        }

        foreach (var item in affected)
        {
            _readHere.Add((item.AccountId, item.Id));
        }

        _actionProblem = null;
        _changesInFlight++;
        Render();

        string? problem;
        try
        {
            problem = await change(source).ConfigureAwait(true);
        }
        finally
        {
            _changesInFlight--;
        }

        if (problem is not null)
        {
            foreach (var item in affected)
            {
                _readHere.Remove((item.AccountId, item.Id));
            }

            _actionProblem = $"Could not mark as read: {problem}";
        }

        Render();
    }

    /// <inheritdoc />
    public IReadOnlyList<WidgetAction> ContextActions => [new(Glyphs.Refresh, "Refresh", Refresh)];

    private void Refresh()
    {
        _actionProblem = null;
        _feed?.Refresh();
        Render();
    }

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
