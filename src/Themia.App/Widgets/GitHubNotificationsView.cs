using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Services.Developer;
using Themia.App.Shell;
using Themia.Core.Developer;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets;

/// <summary>
/// [S-617]..[S-621] Your GitHub notifications across your accounts, grouped under each repository.
/// <para>
/// A row leads with why it is there — "review", "mention", "security" — in the reason's colour;
/// an unread one has the accent dot before its title and a read one is drawn in the secondary
/// colour ([S-748]). As the tile widens a row adds what the thread is about, a pull request or an
/// issue ([S-743]); filling the tile ([S-740]) it lists the most GitHub will send and shows the
/// whole rows that fit. Right-click a row to open it, copy its link, open its repository, mark it
/// read, or mark its whole repository read; the widget's own menu marks everything read. A change
/// shows at once and the feed then reads the truth.
/// </para>
/// </summary>
internal sealed partial class GitHubNotificationsView : Grid, IWidgetView, IWidgetHeaderContent, IWidgetSurfaceAware, IWidgetContextActions, IWidgetSizeAware, IWidgetKeys
{
    /// <summary>Below this body height the list is not grouped: a heading would cost one of few rows.</summary>
    private const double GroupingNeedsHeight = 160;

    private readonly DevChrome _chrome = new(Glyphs.Bell);
    private readonly DevColumns _lists = new();
    private readonly List<IDisposable> _hovers = [];

    /// <summary>[S-810] The rows' keyboard cursor, fed by the shell's keyboard lease.</summary>
    private readonly RowCursor _cursor = new();

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
    private WidgetArea _area;
    private DevRowLayout _layout = DevRows.LayoutFor(0, 0);
    private bool _detached;

    public GitHubNotificationsView()
    {
        Children.Add(_lists);
        Children.Add(_chrome.StatePanel);
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
        Strings.Changed += OnLanguage;
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

    /// <inheritdoc />
    public void ApplySize(WidgetArea area)
    {
        _area = area;
        _chrome.Fit(area.Height);

        // Called only when the area changed; the columns, the grouping and the row shape all follow it.
        Render();
    }

    private void Reopen()
    {
        if (_source is null)
        {
            return;
        }

        var query = new NotificationQuery([.. _config.AccountIds], _config.IncludeRead, _config.ParticipatingOnly, _config.FetchCount, _config.RefreshInterval);
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
        RenderRows();

        // [S-810] Once the rows are in the tree, the keyboard focus goes back to its row.
        _cursor.Restore();
    }

    /// <summary>[S-810] A key from the shell's keyboard lease, which a click on the widget gives it.</summary>
    public bool HandleKey(WidgetKey key) => !_detached && _cursor.HandleKey(key);

    private void RenderRows()
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
            (_config.IncludeRead ? Str.Github_NotificationsEmpty : Str.WidgetsA_Github_NoUnread).Text(),
            now,
            Strings.Current,
            savedAt: snapshot.SavedAt);

        if (!_chrome.Show(screen, _area.Height, _actionProblem))
        {
            _lists.Visibility = Visibility.Collapsed;
            return;
        }

        _lists.Visibility = Visibility.Visible;
        _chrome.SetCount(items.Count(i => i.Unread));

        _layout = DevRows.LayoutFor(_area.Width, _area.Height);
        _grouping = Grouping(_area.Height);

        // [S-620] Under each repository, once there is more than one; a single repository's name
        // goes on each row instead, where a lone heading would only cost a row.
        var groups = GitHubRules.GroupByRepository(items);
        var grouped = _grouping && groups.Count > 1;

        // [S-748] The dot's slot only where read and unread are both on the list.
        var marks = items.Any(i => !i.Unread) && items.Any(i => i.Unread);

        if (!grouped)
        {
            _lists.Reset(1);
            foreach (var item in items)
            {
                AddRow(_lists[0], item, item.Repo, GitHubRules.SubjectLabel(item.SubjectType, Strings.Current), marks, now);
            }

            return;
        }

        // [S-742] A wide tile puts the repositories in two columns, filling the first before the second.
        var columns = _area.IsMeasured ? DevLayout.Columns(_area.Width) : 1;
        _lists.Reset(columns);

        if (columns == 2)
        {
            _layout = DevRows.LayoutFor(_area.Width / 2, _area.Height);
        }

        var rowHeight = _layout.TwoLine ? DevRows.TwoLineHeight : DevRows.OneLineHeight;
        var room = _area.Height - Fluent.Space2;
        var used = 0.0;
        var column = 0;

        foreach (var group in groups)
        {
            // A repository that would not fit whole under the ones before it starts the second
            // column, so a group is never cut off at the first column's foot while the second has room.
            var cost = DevRows.SectionHeight + (group.Items.Count * rowHeight);
            if (column == 0 && columns == 2 && used > 0 && used + cost > room + Disclosure.Tolerance)
            {
                column = 1;
            }

            used += cost;

            var list = _lists[column];
            list.Children.Add(DevRows.Section(group.Repo, group.Unread > 0 ? Str.WidgetsA_Github_UnreadCount.Plural(group.Unread) : null));

            foreach (var item in group.Items)
            {
                // [S-743] Grouped, the subject type is the meta; nothing more to say beside it.
                AddRow(list, item, GitHubRules.SubjectLabel(item.SubjectType, Strings.Current), string.Empty, marks, now);
            }
        }
    }

    private void AddRow(StackPanel list, NotificationItem item, string meta, string detail, bool marks, DateTimeOffset now)
    {
        var reason = GitHubRules.ReasonLabel(item.Reason, Strings.Current);

        // "CI · CI" says nothing twice.
        if (string.Equals(meta, reason, StringComparison.OrdinalIgnoreCase))
        {
            meta = string.Empty;
        }

        if (string.Equals(detail, reason, StringComparison.OrdinalIgnoreCase))
        {
            detail = string.Empty;
        }

        var row = DevRows.Build(
            new DevRowText(
                item.Title,
                reason,
                item.Unread ? DevBrushes.Reason(GitHubRules.ToneOf(item.Reason)) : DevBrushes.Tertiary,
                meta,
                GitHubRules.Ago(item.UpdatedAt, now, Strings.Current),
                !item.Unread,
                item.HtmlUrl,
                detail,
                Unread: marks ? item.Unread : null),
            _layout,
            _hovers,
            () => Menu(item));

        list.Children.Add(row);
        _cursor.Add(row, item.HtmlUrl, () => Menu(item));
    }

    /// <summary>[S-621][S-749] A thread's own menu: open it, copy its link, open its repository, mark it or its repository read.</summary>
    private List<ItemMenuEntry> Menu(NotificationItem item)
    {
        var menu = DevRows.LinkMenu(item.HtmlUrl, item.RepoHtmlUrl);
        menu.Add(ItemMenuEntry.Separator);

        menu.Add(new(Glyphs.MailRead, Str.Github_MarkAsRead.Text(), () => _ = ChangeAsync([item], s => s.MarkReadAsync(item)), Disabled: !item.Unread));

        menu.Add(new(Glyphs.Done, Str.WidgetsA_Github_MarkRepoRead.Format(("repo", item.Repo)), () => _ = ChangeAsync(
            [.. Items().Where(i => i.AccountId == item.AccountId && string.Equals(i.RepoHtmlUrl, item.RepoHtmlUrl, StringComparison.OrdinalIgnoreCase))],
            s => s.MarkRepositoryReadAsync(item))));

        return menu;
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

            _actionProblem = Str.WidgetsA_Github_MarkReadFailed.Format(("problem", problem));
        }

        Render();
    }

    /// <summary>
    /// [S-748] Every notification of every account this widget reads, read — through each
    /// account's own <c>PUT notifications</c>, not one thread at a time.
    /// </summary>
    private void MarkAllRead()
    {
        if (_source is not { } source)
        {
            return;
        }

        var accounts = DevScreens.Targets(source.Accounts, _config.AccountIds, single: false)
            .Where(a => a.Problem is null)
            .Select(a => a.Id)
            .ToList();

        if (accounts.Count == 0)
        {
            return;
        }

        _ = ChangeAsync([.. Items().Where(i => i.Unread)], s => s.MarkAllReadAsync(accounts));
    }

    /// <inheritdoc />
    public IReadOnlyList<WidgetAction> ContextActions =>
    [
        new(Glyphs.Refresh, Str.Common_Refresh.Text(), Refresh),
        new(Glyphs.Done, Str.WidgetsA_Github_MarkAllRead.Text(), MarkAllRead),
    ];

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
        _cursor.Clear();
        _lists.Reset(_lists.Count);
    }

    /// <summary>[S-1109] Everything on the tile is worded from the snapshot in hand; nothing is refetched.</summary>
    private void OnLanguage(Locale locale) => Render();

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        Strings.Changed -= OnLanguage;

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
