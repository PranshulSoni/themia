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
/// <c>empty</c>, <c>stale</c> (content with a failed refresh), <c>saved</c> (content from the saved
/// copy, not refreshed yet), <c>anonymous</c> (no account: the repository widget reads public data)
/// or <c>norepo</c>.
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
            WidgetKind.ClaudeUsage => new ClaudeUsageConfig { AccountId = state == "gone" ? "fixture:signed-out" : string.Empty, ShowOpus = state != "twobars", ShowSonnet = state != "twobars" }, // [S-792] "twobars": the default rows
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
            "anonymous" => [],
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
        return Feed(Repo(query), [_state == "anonymous" ? DevScreens.AnonymousId : Octo]);
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

    public Task<string?> MarkAllReadAsync(IReadOnlyList<string> accountIds) => Task.FromResult<string?>(null);

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

        var withValue = _state is "" or "empty" or "stale" or "saved" or "anonymous" or "norepo" or "twobars";
        var outcomes = _state == "saved" ? [.. accounts.Select(id => new DevOutcome(id, FetchStatus.Loading))] : accounts.Select(Outcome).ToList();
        var saved = _state == "saved" ? now.AddHours(-3) : (DateTimeOffset?)null;
        return new Fixed<T>(new DevFeedSnapshot<T>(withValue ? value : null, outcomes, saved ?? now.AddMinutes(-2), saved));
    }

    // ---- data ------------------------------------------------------------------------------------

    private static RepoActivity Repo(RepoQuery query)
    {
        var now = DateTimeOffset.UtcNow;
        const string Root = "https://github.com/themia-labs/desk";

        IReadOnlyList<PullItem> pulls =
        [
            new(214, "Pin widgets to a monitor edge", PullState.Open, false, $"{Root}/pull/214", now.AddHours(-3), "rosa"),
            new(212, "Faster cold start for the picker", PullState.Merged, false, $"{Root}/pull/212", now.AddHours(-20), "kenji"),
            new(209, "Draft: experiment with blur on cards", PullState.Open, true, $"{Root}/pull/209", now.AddDays(-2), "ines"),
            new(205, "Rework the settings sidebar", PullState.Closed, false, $"{Root}/pull/205", now.AddDays(-4), "malik"),
            new(203, "Keyboard navigation in lists", PullState.Merged, false, $"{Root}/pull/203", now.AddDays(-5), "rosa"),
            new(198, "Translate the picker into nine languages", PullState.Open, false, $"{Root}/pull/198", now.AddDays(-6), "ines"),
        ];

        return new RepoActivity(
            new RepoSummary("themia-labs/desk", Root, 12_840, 963, 23, "C#", now.AddHours(-2), "main", "Widgets for the Windows desktop: clocks, meters, lists and feeds."),
            [.. new RunItem[]
            {
                new(1, "CI", RunState.Success, "main", now.AddMinutes(-25), $"{Root}/actions/runs/1", "push", "rosa"),
                new(2, "Release", RunState.Running, "v0.14", now.AddMinutes(-8), $"{Root}/actions/runs/2", "workflow dispatch", "kenji"),
                new(3, "Lint", RunState.Failure, "feature/edges", now.AddHours(-5), $"{Root}/actions/runs/3", "pull request", "ines"),
                new(4, "CI", RunState.Success, "main", now.AddHours(-9), $"{Root}/actions/runs/4", "push", "malik"),
                new(5, "Docs", RunState.Other, "main", now.AddDays(-1), $"{Root}/actions/runs/5", "schedule", "octo-dev"),
                new(6, "CI", RunState.Success, "feature/edges", now.AddDays(-1), $"{Root}/actions/runs/6", "pull request", "ines"),
            }.Take(Math.Max(query.Runs, 1))],
            [.. new CommitItem[]
            {
                new("4f9c2e1", "Fix the header ellipsis on narrow cards", $"{Root}/commit/4f9c2e1", now.AddHours(-2), "Rosa Lind"),
                new("b07d913", "Share one HTTP client between widgets", $"{Root}/commit/b07d913", now.AddHours(-6), "Kenji Ito"),
                new("e3a55c0", "Bump the version to 0.14.0", $"{Root}/commit/e3a55c0", now.AddDays(-1), "octo-dev"),
                new("9a1b7d4", "Measure rows before capping the list", $"{Root}/commit/9a1b7d4", now.AddDays(-1), "Ines Duarte"),
                new("c55e021", "Drop the unused picker icons", $"{Root}/commit/c55e021", now.AddDays(-2), "Malik Osei"),
                new("1d0f3aa", "Explain the conditional requests", $"{Root}/commit/1d0f3aa", now.AddDays(-3), "Rosa Lind"),
            }.Take(query.Commits)],
            GitHubRules.FilterSortPulls(pulls, query.Filter, query.Order, query.Pulls),
            [.. new IssueRow[]
            {
                new(231, "Clock drifts after sleep", $"{Root}/issues/231", now.AddHours(-1), "rosa", 4, [new("bug", "D73A4A"), new("clock", "0E8A16")]),
                new(228, "Gallery: add a dark wallpaper behind cards", $"{Root}/issues/228", now.AddDays(-1), "ines", 1, [new("enhancement", "A2EEEF")]),
                new(219, "Tooltip covers the resize handle", $"{Root}/issues/219", now.AddDays(-6), "malik", 0, [new("bug", "D73A4A")]),
                new(217, "Remember the last view per monitor", $"{Root}/issues/217", now.AddDays(-7), "kenji", 7, []),
                new(211, "Settings search misses widget names", $"{Root}/issues/211", now.AddDays(-9), "octo-dev", 2, [new("settings", "FBCA04")]),
            }.Take(query.Issues)],
            new ReleaseItem("v0.14.0", $"{Root}/releases/tag/v0.14.0", "Spring cleaning", now.AddDays(-3)));
    }

    private static List<SearchItem> Assigned()
    {
        var now = DateTimeOffset.UtcNow;
        return
        [
            new(Octo, 231, "Clock drifts after sleep", "https://github.com/themia-labs/desk/issues/231", "themia-labs/desk", now.AddMinutes(-18), false, "rosa", 4, [new("bug", "D73A4A"), new("clock", "0E8A16")]),
            new(Work, 88, "Investigate cache key collisions on Enterprise", "https://ghe.example.com/platform/gateway/issues/88", "platform/gateway", now.AddHours(-6), false, "kenji", 2, [new("performance", "5319E7")]),
            new(Octo, 228, "Gallery: add a dark wallpaper behind cards", "https://github.com/themia-labs/desk/issues/228", "themia-labs/desk", now.AddDays(-1), false, "octo-dev", 1, [new("enhancement", "A2EEEF")]),
            new(Octo, 11, "Polish the empty states", "https://github.com/themia-labs/docs/issues/11", "themia-labs/docs", now.AddDays(-3), false, "ines", 0, [new("design", "F9D0C4")]),
            new(Work, 412, "Rotate the staging certificates", "https://ghe.example.com/platform/infra/issues/412", "platform/infra", now.AddDays(-5), false, "kenji", 6, []),
            new(Octo, 7, "Document the release checklist", "https://github.com/themia-labs/docs/issues/7", "themia-labs/docs", now.AddDays(-12), false, "rosa", 3, [new("docs", "0075CA")]),
            new(Octo, 204, "Card shadow clips on the top monitor edge", "https://github.com/themia-labs/desk/issues/204", "themia-labs/desk", now.AddDays(-13), false, "malik", 5, [new("bug", "D73A4A")]),
            new(Work, 77, "Gateway health check ignores the proxy", "https://ghe.example.com/platform/gateway/issues/77", "platform/gateway", now.AddDays(-14), false, "kenji", 1, []),
            new(Octo, 5, "Screenshots for the store listing", "https://github.com/themia-labs/docs/issues/5", "themia-labs/docs", now.AddDays(-20), false, "ines", 0, [new("docs", "0075CA")]),
            new(Octo, 199, "Weather widget forgets the unit", "https://github.com/themia-labs/desk/issues/199", "themia-labs/desk", now.AddDays(-25), false, "rosa", 2, [new("bug", "D73A4A")]),
            new(Octo, 190, "Allow a custom accent per view", "https://github.com/themia-labs/desk/issues/190", "themia-labs/desk", now.AddDays(-28), false, "octo-dev", 9, [new("enhancement", "A2EEEF")]),
            new(Work, 401, "Retire the old build agents", "https://ghe.example.com/platform/infra/issues/401", "platform/infra", now.AddDays(-30), false, "kenji", 0, []),
        ];
    }

    private static List<SearchItem> Reviews()
    {
        var now = DateTimeOffset.UtcNow;
        return
        [
            new(Octo, 214, "Pin widgets to a monitor edge", "https://github.com/themia-labs/desk/pull/214", "themia-labs/desk", now.AddMinutes(-25), false, "rosa", 6, [new("feature", "1D76DB")]),
            new(Work, 1290, "Retry idempotent requests on 503", "https://ghe.example.com/platform/gateway/pull/1290", "platform/gateway", now.AddHours(-3), false, "kenji", 2, [new("reliability", "B60205")]),
            new(Octo, 209, "Experiment with blur on cards", "https://github.com/themia-labs/desk/pull/209", "themia-labs/desk", now.AddDays(-2), true, "ines", 0, []),
            new(Octo, 40, "Spell-check the widget descriptions", "https://github.com/themia-labs/docs/pull/40", "themia-labs/docs", now.AddDays(-4), false, "malik", 1, [new("docs", "0075CA")]),
            new(Octo, 203, "Keyboard navigation in lists", "https://github.com/themia-labs/desk/pull/203", "themia-labs/desk", now.AddDays(-5), false, "rosa", 11, [new("a11y", "0E8A16")]),
            new(Work, 1284, "Move the gateway to the new runtime", "https://ghe.example.com/platform/gateway/pull/1284", "platform/gateway", now.AddDays(-6), false, "kenji", 3, []),
            new(Octo, 198, "Translate the picker into nine languages", "https://github.com/themia-labs/desk/pull/198", "themia-labs/desk", now.AddDays(-6), false, "ines", 4, [new("i18n", "C5DEF5")]),
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
            new(Octo, "n9", true, NotificationReason.TeamMention, now.AddDays(-2), "Keyboard navigation in lists", "PullRequest", $"{Desk}/pull/203", "themia-labs/desk", Desk),
            new(Octo, "n10", false, NotificationReason.Comment, now.AddDays(-3), "Spell-check the widget descriptions", "PullRequest", $"{Docs}/pull/40", "themia-labs/docs", Docs),
            new(Work, "n11", true, NotificationReason.ReviewRequested, now.AddDays(-3), "Move the gateway to the new runtime", "PullRequest", $"{Gateway}/pull/1284", "platform/gateway", Gateway),
            new(Octo, "n12", false, NotificationReason.StateChange, now.AddDays(-4), "Rework the settings sidebar", "PullRequest", $"{Desk}/pull/205", "themia-labs/desk", Desk),
            new(Octo, "n13", false, NotificationReason.Subscribed, now.AddDays(-5), "Discussion: plugin widgets", "Discussion", Desk, "themia-labs/desk", Desk),
            new(Octo, "n14", false, NotificationReason.Author, now.AddDays(-6), "Card shadow clips on the top monitor edge", "Issue", $"{Desk}/issues/204", "themia-labs/desk", Desk),
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
