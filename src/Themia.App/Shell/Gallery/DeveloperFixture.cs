#if DEBUG
using Themia.App.Services;
using Themia.App.Services.Developer;
using Themia.Core.Developer;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-637] Made-up GitHub and Claude data for the debug gallery, which has no accounts (and must
/// never sign in to one). The plain page of each developer widget is a fresh install's "sign in"
/// state; the <c>+fixture</c> page is populated. Every login, repository and title is invented.
/// Debug builds only.
/// <para>
/// <c>THEMIA_DEV_FIXTURE</c> picks another state for the populated page while working on the
/// widgets: <c>signin</c>, <c>gone</c>, <c>loading</c>, <c>offline</c>, <c>ratelimited</c>,
/// <c>empty</c>, <c>stale</c> (content with a failed refresh) or <c>norepo</c>.
/// </para>
/// </summary>
internal sealed class DeveloperFixture : IGitHubSource, IClaudeUsageSource
{
    private const string Octo = "fixture:octo";
    private const string Work = "fixture:work";
    private const string Claude = "fixture:claude";

    private readonly bool _populated;
    private readonly string _state;

    private DeveloperFixture(bool populated)
    {
        _populated = populated;
        _state = populated ? (Environment.GetEnvironmentVariable("THEMIA_DEV_FIXTURE") ?? string.Empty).Trim().ToLowerInvariant() : string.Empty;
    }

    /// <summary>
    /// Put a fixture where the developer widgets look for their sources — unless the app's real
    /// services are there: the gallery can also be opened inside the running app, and a debug page
    /// must never take the user's own accounts out from under their widgets.
    /// </summary>
    public static void Install(bool populated)
    {
        if (AppServices.GitHub is not null and not DeveloperFixture)
        {
            return;
        }

        var fixture = new DeveloperFixture(populated);
        AppServices.GitHub = fixture;
        AppServices.ClaudeUsage = fixture;
    }

    /// <summary>The fixture page's config for a kind, or null when it is not a developer widget.</summary>
    public static WidgetConfig? Config(WidgetKind kind)
    {
        var state = (Environment.GetEnvironmentVariable("THEMIA_DEV_FIXTURE") ?? string.Empty).Trim().ToLowerInvariant();

        // "gone": the widget names an account that is no longer signed in.
        List<string> chosen = state == "gone" ? [Octo] : [];

        return kind switch
        {
            WidgetKind.GitHub => new GitHubConfig { AccountIds = chosen, Repo = state == "norepo" ? string.Empty : "themia-labs/desk", PullFilter = PullFilter.All },
            WidgetKind.GitHubIssues => new GitHubIssuesConfig { AccountIds = chosen },
            WidgetKind.GitHubReviews => new GitHubReviewsConfig { AccountIds = chosen, IncludeDrafts = true },
            WidgetKind.GitHubNotifications => new GitHubNotificationsConfig { AccountIds = chosen, Count = 10, IncludeRead = true },
            WidgetKind.ClaudeUsage => new ClaudeUsageConfig { AccountId = state == "gone" ? "fixture:signed-out" : string.Empty, ShowOpus = true, ShowSonnet = true },
            _ => null,
        };
    }

    public event Action? AccountsChanged
    {
        add { }
        remove { }
    }

    // ---- accounts --------------------------------------------------------------------------------

    IReadOnlyList<DevAccount> IGitHubSource.Accounts => !_populated
        ? []
        : _state switch
        {
            "signin" => [new DevAccount(Octo, "octo-dev", "GitHub refused the sign-in. Sign in again.")],
            "gone" => [new DevAccount("fixture:other", "someone-else", null)],
            _ => [new DevAccount(Octo, "octo-dev", null), new DevAccount(Work, "octo-at-work", null)],
        };

    IReadOnlyList<DevAccount> IClaudeUsageSource.Accounts => !_populated
        ? []
        : _state == "signin"
            ? [new DevAccount(Claude, "Claude Code", "Claude wants you to sign in again.")]
            : [new DevAccount(Claude, "Claude Code", null)];

    // ---- feeds -----------------------------------------------------------------------------------

    public IDevFeed<RepoActivity> OpenRepo(RepoQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Feed(Repo(query), [Octo]);
    }

    public IDevFeed<IReadOnlyList<SearchItem>> OpenSearch(SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<SearchItem> items = _state == "empty"
            ? []
            : GitHubRules.MergeSearch(
                [query.Kind == SearchKind.Reviews ? Reviews() : Assigned()],
                query.Count,
                query.Kind != SearchKind.Reviews || query.IncludeDrafts);

        return Feed(items, [Octo, Work]);
    }

    public IDevFeed<IReadOnlyList<NotificationItem>> OpenNotifications(NotificationQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<NotificationItem> items = _state == "empty"
            ? []
            : GitHubRules.MergeNotifications([Notifications().Where(n => query.IncludeRead || n.Unread).ToList()], query.Count);

        return Feed(items, [Octo, Work]);
    }

    public IDevFeed<ClaudeUsageReport> Open(string accountId, TimeSpan refresh)
    {
        var now = DateTimeOffset.UtcNow;
        var usage = new ClaudeUsageReport(
            new UsageWindow(42.5, now.AddHours(2).AddMinutes(10)),
            new UsageWindow(73, now.AddDays(3).AddHours(4)),
            new UsageWindow(18, now.AddDays(3).AddHours(4)),
            new UsageWindow(94, now.AddDays(3).AddHours(4)));

        return Feed(usage, [Claude]);
    }

    public Task<string?> MarkReadAsync(NotificationItem item) => Task.FromResult<string?>(null);

    public Task<string?> MarkRepositoryReadAsync(NotificationItem item) => Task.FromResult<string?>(null);

    /// <summary>A feed holding one fixed snapshot, shaped by the requested state.</summary>
    private Fixed<T> Feed<T>(T value, IReadOnlyList<string> accounts)
        where T : class
    {
        var now = DateTimeOffset.UtcNow;

        DevOutcome Outcome(string id) => _state switch
        {
            "loading" => new DevOutcome(id, FetchStatus.Loading),
            "offline" or "stale" => new DevOutcome(id, FetchStatus.Offline),
            "ratelimited" => new DevOutcome(id, FetchStatus.RateLimited, null, now.AddMinutes(38)),
            _ => new DevOutcome(id, FetchStatus.Ok),
        };

        var withValue = _state is "" or "empty" or "stale" or "norepo";
        return new Fixed<T>(new DevFeedSnapshot<T>(withValue ? value : null, [.. accounts.Select(Outcome)], now.AddMinutes(-2)));
    }

    // ---- data ------------------------------------------------------------------------------------

    private static RepoActivity Repo(RepoQuery query)
    {
        var now = DateTimeOffset.UtcNow;
        const string Root = "https://github.com/themia-labs/desk";

        IReadOnlyList<PullItem> pulls =
        [
            new(214, "Pin widgets to a monitor edge", PullState.Open, false, $"{Root}/pull/214", now.AddHours(-3)),
            new(212, "Faster cold start for the picker", PullState.Merged, false, $"{Root}/pull/212", now.AddHours(-20)),
            new(209, "Draft: experiment with blur on cards", PullState.Open, true, $"{Root}/pull/209", now.AddDays(-2)),
            new(205, "Rework the settings sidebar", PullState.Closed, false, $"{Root}/pull/205", now.AddDays(-4)),
        ];

        return new RepoActivity(
            new RepoSummary("themia-labs/desk", Root, 12_840, 963, 23, "C#", now.AddHours(-2), "main"),
            [
                new RunItem(1, "CI", RunState.Success, "main", now.AddMinutes(-25), $"{Root}/actions/runs/1"),
                new RunItem(2, "Release", RunState.Running, "v0.14", now.AddMinutes(-8), $"{Root}/actions/runs/2"),
                new RunItem(3, "Lint", RunState.Failure, "feature/edges", now.AddHours(-5), $"{Root}/actions/runs/3"),
                new RunItem(4, "CI", RunState.Success, "main", now.AddHours(-9), $"{Root}/actions/runs/4"),
                new RunItem(5, "Docs", RunState.Other, "main", now.AddDays(-1), $"{Root}/actions/runs/5"),
            ],
            [
                new CommitItem("4f9c2e1", "Fix the header ellipsis on narrow cards", $"{Root}/commit/4f9c2e1", now.AddHours(-2)),
                new CommitItem("b07d913", "Share one HTTP client between widgets", $"{Root}/commit/b07d913", now.AddHours(-6)),
                new CommitItem("e3a55c0", "Bump the version to 0.14.0", $"{Root}/commit/e3a55c0", now.AddDays(-1)),
            ],
            GitHubRules.FilterSortPulls(pulls, query.Filter, query.Order, query.Pulls),
            [
                new IssueRow(231, "Clock drifts after sleep", $"{Root}/issues/231", now.AddHours(-1)),
                new IssueRow(228, "Gallery: add a dark wallpaper behind cards", $"{Root}/issues/228", now.AddDays(-1)),
                new IssueRow(219, "Tooltip covers the resize handle", $"{Root}/issues/219", now.AddDays(-6)),
            ],
            new ReleaseItem("v0.14.0", $"{Root}/releases/tag/v0.14.0"));
    }

    private static List<SearchItem> Assigned()
    {
        var now = DateTimeOffset.UtcNow;
        return
        [
            new(Octo, 231, "Clock drifts after sleep", "https://github.com/themia-labs/desk/issues/231", "themia-labs/desk", now.AddMinutes(-18), false, "rosa"),
            new(Work, 88, "Investigate cache key collisions on Enterprise", "https://ghe.example.com/platform/gateway/issues/88", "platform/gateway", now.AddHours(-6), false, "kenji"),
            new(Octo, 228, "Gallery: add a dark wallpaper behind cards", "https://github.com/themia-labs/desk/issues/228", "themia-labs/desk", now.AddDays(-1), false, "octo-dev"),
            new(Octo, 11, "Polish the empty states", "https://github.com/themia-labs/docs/issues/11", "themia-labs/docs", now.AddDays(-3), false, "ines"),
            new(Work, 412, "Rotate the staging certificates", "https://ghe.example.com/platform/infra/issues/412", "platform/infra", now.AddDays(-5), false, "kenji"),
            new(Octo, 7, "Document the release checklist", "https://github.com/themia-labs/docs/issues/7", "themia-labs/docs", now.AddDays(-12), false, "rosa"),
        ];
    }

    private static List<SearchItem> Reviews()
    {
        var now = DateTimeOffset.UtcNow;
        return
        [
            new(Octo, 214, "Pin widgets to a monitor edge", "https://github.com/themia-labs/desk/pull/214", "themia-labs/desk", now.AddMinutes(-25), false, "rosa"),
            new(Work, 1290, "Retry idempotent requests on 503", "https://ghe.example.com/platform/gateway/pull/1290", "platform/gateway", now.AddHours(-3), false, "kenji"),
            new(Octo, 209, "Experiment with blur on cards", "https://github.com/themia-labs/desk/pull/209", "themia-labs/desk", now.AddDays(-2), true, "ines"),
            new(Octo, 40, "Spell-check the widget descriptions", "https://github.com/themia-labs/docs/pull/40", "themia-labs/docs", now.AddDays(-4), false, "malik"),
        ];
    }

    private static List<NotificationItem> Notifications()
    {
        var now = DateTimeOffset.UtcNow;
        const string Desk = "https://github.com/themia-labs/desk";
        const string Docs = "https://github.com/themia-labs/docs";
        const string Gateway = "https://ghe.example.com/platform/gateway";

        return
        [
            new(Octo, "n1", true, NotificationReason.ReviewRequested, now.AddMinutes(-12), "Pin widgets to a monitor edge", "PullRequest", $"{Desk}/pull/214", "themia-labs/desk", Desk),
            new(Octo, "n2", true, NotificationReason.Mention, now.AddMinutes(-90), "Clock drifts after sleep", "Issue", $"{Desk}/issues/231", "themia-labs/desk", Desk),
            new(Work, "n3", true, NotificationReason.SecurityAlert, now.AddHours(-2), "Dependency advisory in a transitive package", "RepositoryDependabotAlertsThread", Gateway, "platform/gateway", Gateway),
            new(Work, "n4", false, NotificationReason.Author, now.AddHours(-4), "Retry idempotent requests on 503", "PullRequest", $"{Gateway}/pull/1290", "platform/gateway", Gateway),
            new(Octo, "n5", true, NotificationReason.Comment, now.AddHours(-7), "Polish the empty states", "Issue", $"{Docs}/issues/11", "themia-labs/docs", Docs),
            new(Octo, "n6", false, NotificationReason.CiActivity, now.AddHours(-9), "Lint failed on feature/edges", "CheckSuite", Desk, "themia-labs/desk", Desk),
            new(Octo, "n7", false, NotificationReason.Subscribed, now.AddDays(-1), "v0.14.0", "Release", $"{Desk}/releases/tag/v0.14.0", "themia-labs/desk", Desk),
            new(Work, "n8", false, NotificationReason.Assign, now.AddDays(-2), "Rotate the staging certificates", "Issue", "https://ghe.example.com/platform/infra/issues/412", "platform/infra", "https://ghe.example.com/platform/infra"),
        ];
    }

    private sealed class Fixed<T>(DevFeedSnapshot<T> snapshot) : IDevFeed<T>
        where T : class
    {
        public event Action? Updated
        {
            add { }
            remove { }
        }

        public DevFeedSnapshot<T> Snapshot { get; } = snapshot;

        public void Refresh()
        {
            // ignore: a fixed fixture has nothing newer to fetch.
        }

        public void Dispose()
        {
            // ignore: nothing held.
        }
    }
}
#endif
