using System.Text.Json;
using Themia.Core.Developer;
using Themia.Core.Model;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The GitHub widgets' rules ([S-610]..[S-629]), ported from the classic app's
/// <c>_shared.test.ts</c> and <c>prFilterSort.test.ts</c> and the GitHub SPECs' [A-NN]/[N-NN]/[R-NN].
/// </summary>
public sealed class GitHubRulesTests
{
    private static readonly Uri Api = GitHubRules.ApiRoot("https://api.github.com");
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    // ---- [S-611] repository references ---------------------------------------------------------

    [Theory]
    [InlineData("octocat/Hello-World", "octocat", "Hello-World")]
    [InlineData("  octocat/hello.world  ", "octocat", "hello.world")]
    [InlineData("https://github.com/octocat/Hello-World", "octocat", "Hello-World")]
    [InlineData("https://github.com/octocat/Hello-World.git", "octocat", "Hello-World")]
    [InlineData("https://github.com/octocat/Hello-World/tree/main/src", "octocat", "Hello-World")]
    [InlineData("https://ghe.corp.example/team/service", "team", "service")]
    [InlineData("github.com/octocat/Hello-World", "octocat", "Hello-World")]
    public void S611_parses_owner_and_name_from_what_people_paste(string input, string owner, string name)
    {
        var repo = GitHubRules.ParseRepo(input);

        Assert.NotNull(repo);
        Assert.Equal(owner, repo.Owner);
        Assert.Equal(name, repo.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("octocat")]
    [InlineData("octo cat/repo")]
    [InlineData("octocat/../etc")]
    [InlineData("octocat/repo/extra")]
    [InlineData("-bad/repo")]
    [InlineData("javascript://github.com/a/b")]
    [InlineData("https://github.com/onlyowner")]
    public void S611_refuses_anything_that_is_not_a_repository(string? input)
        => Assert.Null(GitHubRules.ParseRepo(input));

    [Fact]
    public void S611_the_account_decides_the_host_not_the_pasted_address()
    {
        // An Enterprise address pasted into the box still asks the account's API root: the token
        // only ever goes where the user signed in.
        var repo = GitHubRules.ParseRepo("https://evil.example/o/r")!;
        Assert.Equal("https://api.github.com/repos/o/r", GitHubRules.RepoUrl(Api, repo).AbsoluteUri);

        var enterprise = GitHubRules.ApiRoot("https://ghe.corp.example/api/v3");
        Assert.Equal("https://ghe.corp.example/api/v3/repos/o/r", GitHubRules.RepoUrl(enterprise, repo).AbsoluteUri);
    }

    [Fact]
    public void S615_repo_name_comes_from_the_repository_url()
    {
        Assert.Equal("octocat/Hello-World", GitHubRules.RepoFromRepositoryUrl("https://api.github.com/repos/octocat/Hello-World"));
        Assert.Equal("team/svc", GitHubRules.RepoFromRepositoryUrl("https://ghe.corp/api/v3/repos/team/svc"));
        Assert.Equal(string.Empty, GitHubRules.RepoFromRepositoryUrl("https://api.github.com/users/octocat"));
        Assert.Equal(string.Empty, GitHubRules.RepoFromRepositoryUrl(null));
    }

    // ---- [S-612] addresses ---------------------------------------------------------------------

    [Fact]
    public void S612_repository_addresses_match_the_classic_app()
    {
        var repo = new RepoRef("o", "r");

        Assert.Equal("https://api.github.com/repos/o/r/actions/runs?per_page=5", GitHubRules.RunsUrl(Api, repo, 5).AbsoluteUri);
        // Zero runs still asks for one: the newest run is the header's CI status.
        Assert.Equal("https://api.github.com/repos/o/r/actions/runs?per_page=1", GitHubRules.RunsUrl(Api, repo, 0).AbsoluteUri);
        Assert.Equal("https://api.github.com/repos/o/r/commits?per_page=3", GitHubRules.CommitsUrl(Api, repo, 3).AbsoluteUri);
        Assert.Equal("https://api.github.com/repos/o/r/issues?state=open&per_page=8", GitHubRules.IssuesUrl(Api, repo, 3).AbsoluteUri);
        Assert.Equal("https://api.github.com/repos/o/r/releases/latest", GitHubRules.LatestReleaseUrl(Api, repo).AbsoluteUri);
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(3, 14)]
    [InlineData(10, 35)]
    [InlineData(40, 100)]
    public void S612_pull_requests_are_over_fetched_in_every_state(int count, int fetched)
    {
        Assert.Equal(fetched, GitHubRules.PullsToFetch(count));
        Assert.Equal(
            $"https://api.github.com/repos/o/r/pulls?state=all&sort=created&direction=desc&per_page={fetched}",
            GitHubRules.PullsUrl(Api, new RepoRef("o", "r"), count).AbsoluteUri);
    }

    [Fact]
    public void S615_assigned_query_ors_in_mentions_only_when_asked()
    {
        Assert.Equal("is:open is:issue archived:false assignee:@me", GitHubRules.AssignedQuery(false));
        Assert.Equal("is:open is:issue archived:false (assignee:@me OR mentions:@me)", GitHubRules.AssignedQuery(true));

        var url = GitHubRules.SearchUrl(Api, GitHubRules.AssignedQuery(false), 8);
        Assert.Equal("/search/issues", url.AbsolutePath);
        Assert.Contains("sort=updated&order=desc&per_page=8", url.Query, StringComparison.Ordinal);
        Assert.Contains("q=is%3Aopen%20is%3Aissue%20archived%3Afalse%20assignee%3A%40me", url.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void S616_review_query_and_search_run_against_the_accounts_host()
    {
        Assert.Equal("is:open is:pr review-requested:@me archived:false", GitHubRules.ReviewQuery);

        var enterprise = GitHubRules.ApiRoot("https://ghe.corp/api/v3/");
        Assert.StartsWith("https://ghe.corp/api/v3/search/issues?q=", GitHubRules.SearchUrl(enterprise, GitHubRules.ReviewQuery, 8).AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void S617_notification_parameters_follow_the_toggles()
    {
        Assert.Equal("https://api.github.com/notifications?all=false&participating=false&per_page=5", GitHubRules.NotificationsUrl(Api, false, false, 5).AbsoluteUri);
        Assert.Equal("https://api.github.com/notifications?all=true&participating=true&per_page=20", GitHubRules.NotificationsUrl(Api, true, true, 20).AbsoluteUri);
    }

    [Fact]
    public void S621_thread_ids_are_escaped_into_one_segment()
    {
        Assert.Equal("https://api.github.com/notifications/threads/123", GitHubRules.ThreadUrl(Api, "123").AbsoluteUri);
        Assert.Equal("https://api.github.com/notifications/threads/..%2Fuser", GitHubRules.ThreadUrl(Api, "../user").AbsoluteUri);
        Assert.Equal("https://api.github.com/repos/o/r/notifications", GitHubRules.RepoNotificationsUrl(Api, new RepoRef("o", "r")).AbsoluteUri);
    }

    // ---- [S-613] pull request filter and order (classic issue #42) ----------------------------

    private static readonly PullItem[] Pulls =
    [
        new(1, "one", PullState.Open, false, "https://github.com/o/r/pull/1", Date(2026, 1, 1)),
        new(2, "two", PullState.Merged, false, "https://github.com/o/r/pull/2", Date(2026, 2, 1)),
        new(3, "three", PullState.Open, false, "https://github.com/o/r/pull/3", Date(2026, 6, 1)),
        new(4, "four", PullState.Closed, false, "https://github.com/o/r/pull/4", Date(2026, 4, 1)),
        new(5, "five", PullState.Open, false, "https://github.com/o/r/pull/5", Date(2026, 3, 15)),
    ];

    private static DateTimeOffset Date(int y, int m, int d) => new(y, m, d, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(PullFilter.Open, PullOrder.Newest, new[] { 3, 5, 1 })]
    [InlineData(PullFilter.Closed, PullOrder.Newest, new[] { 4, 2 })]
    [InlineData(PullFilter.All, PullOrder.Newest, new[] { 3, 4, 5, 2, 1 })]
    [InlineData(PullFilter.All, PullOrder.Oldest, new[] { 1, 2, 5, 4, 3 })]
    public void S613_filters_by_state_and_orders_by_creation(PullFilter filter, PullOrder order, int[] expected)
        => Assert.Equal(expected, GitHubRules.FilterSortPulls(Pulls, filter, order, 10).Select(p => p.Number));

    [Fact]
    public void S613_cuts_to_the_count_and_never_reorders_the_input()
    {
        var before = Pulls.Select(p => p.Number).ToArray();

        Assert.Equal([3, 4], GitHubRules.FilterSortPulls(Pulls, PullFilter.All, PullOrder.Newest, 2).Select(p => p.Number));
        Assert.Equal(before, Pulls.Select(p => p.Number));
        Assert.Equal(PullFilter.Open, new GitHubConfig().PullFilter);
        Assert.Equal(PullOrder.Newest, new GitHubConfig().PullOrder);
    }

    [Fact]
    public void S610_merged_is_its_own_state()
    {
        Assert.Equal(PullState.Merged, GitHubRules.ToPullState("closed", Date(2026, 1, 2)));
        Assert.Equal(PullState.Closed, GitHubRules.ToPullState("closed", null));
        Assert.Equal(PullState.Open, GitHubRules.ToPullState("open", null));
    }

    // ---- [S-610] mapping -----------------------------------------------------------------------

    [Theory]
    [InlineData("in_progress", null, RunState.Running)]
    [InlineData("queued", null, RunState.Queued)]
    [InlineData("completed", "success", RunState.Success)]
    [InlineData("completed", "failure", RunState.Failure)]
    [InlineData("completed", "timed_out", RunState.Failure)]
    [InlineData("completed", "cancelled", RunState.Other)]
    [InlineData("completed", "skipped", RunState.Other)]
    public void S610_run_state_folds_status_and_conclusion(string status, string? conclusion, RunState expected)
        => Assert.Equal(expected, GitHubRules.ToRunState(status, conclusion));

    [Fact]
    public void S610_repo_bundle_maps_from_github_json_and_drops_what_it_cannot_draw()
    {
        var repo = JsonSerializer.Deserialize(
            """{"full_name":"o/r","html_url":"https://github.com/o/r","stargazers_count":1234,"forks_count":56,"open_issues_count":7,"language":"C#","pushed_at":"2026-10-04T10:00:00Z","default_branch":"main","owner":{"login":"o"}}""",
            GitHubWireContext.Default.GhRepo);
        var summary = GitHubRules.ToRepo(repo);

        Assert.NotNull(summary);
        Assert.Equal(1234, summary.Stars);
        Assert.Equal("C#", summary.Language);

        var commits = JsonSerializer.Deserialize(
            """[{"sha":"abcdef1234567","html_url":"https://github.com/o/r/commit/abcdef1","commit":{"message":"Fix the thing\n\nLong body","author":{"name":"A","date":"2026-10-04T09:00:00Z"}}},{"sha":"abc","html_url":"https://github.com/o/r/commit/abc"}]""",
            GitHubWireContext.Default.ListGhCommit);
        var mapped = GitHubRules.ToCommits(commits, 5);

        var commit = Assert.Single(mapped);
        Assert.Equal("abcdef1", commit.Sha);
        Assert.Equal("Fix the thing", commit.Message);

        var issues = JsonSerializer.Deserialize(
            """[{"number":9,"title":"Bug","html_url":"https://github.com/o/r/issues/9"},{"number":10,"title":"PR","html_url":"https://github.com/o/r/pull/10","pull_request":{"merged_at":null}},{"number":11,"title":"bad link","html_url":"javascript:alert(1)"}]""",
            GitHubWireContext.Default.ListGhIssue);
        Assert.Equal([9], GitHubRules.ToIssues(issues, 5).Select(i => i.Number));

        Assert.Null(GitHubRules.ToRepo(new GhRepo { FullName = "o/r", HtmlUrl = "file:///c:/x" }));
    }

    [Fact]
    public void S610_a_link_that_is_not_http_never_reaches_a_row()
    {
        Assert.Null(GitHubRules.WebUrl("javascript:alert(1)"));
        Assert.Null(GitHubRules.WebUrl("https://user:pw@github.com/o/r"));
        Assert.Null(GitHubRules.WebUrl("/relative"));
        Assert.Equal("https://github.com/o/r", GitHubRules.WebUrl("https://github.com/o/r"));
    }

    // ---- [S-619] subject address to web page ---------------------------------------------------

    private const string RepoPage = "https://github.com/o/r";

    [Theory]
    [InlineData("https://api.github.com/repos/o/r/pulls/12", "https://github.com/o/r/pull/12")]
    [InlineData("https://api.github.com/repos/o/r/issues/34", "https://github.com/o/r/issues/34")]
    [InlineData("https://api.github.com/repos/o/r/releases/9", "https://github.com/o/r/releases/9")]
    [InlineData("https://ghe.corp/api/v3/repos/o/r/issues/7", "https://ghe.corp/o/r/issues/7")]
    public void S619_subject_urls_become_web_pages(string subject, string expected)
        => Assert.Equal(expected, GitHubRules.SubjectUrlToHtml(subject, RepoPage));

    [Theory]
    [InlineData(null)]
    [InlineData("https://api.github.com/repos/o/r/issues/comments/12345")] // classic N-22: a comment id is not an issue number
    [InlineData("https://api.github.com/repos/o/r/pulls/comments/98765")]
    [InlineData("https://api.github.com/repos/o/r/commits")]
    [InlineData("not a url")]
    public void S619_anything_else_falls_back_to_the_repository(string? subject)
        => Assert.Equal(RepoPage, GitHubRules.SubjectUrlToHtml(subject, RepoPage));

    // ---- [S-618] reasons -----------------------------------------------------------------------

    [Theory]
    [InlineData("security_alert", ReasonTone.Critical)]
    [InlineData("review_requested", ReasonTone.Attention)]
    [InlineData("mention", ReasonTone.Attention)]
    [InlineData("team_mention", ReasonTone.Attention)]
    [InlineData("assign", ReasonTone.Attention)]
    [InlineData("author", ReasonTone.Accent)]
    [InlineData("subscribed", ReasonTone.Neutral)]
    [InlineData("something_new", ReasonTone.Neutral)]
    public void S618_reason_tones_match_the_classic_colours(string reason, ReasonTone tone)
        => Assert.Equal(tone, GitHubRules.ToneOf(GitHubRules.ParseReason(reason)));

    [Fact]
    public void S618_every_reason_has_a_short_label()
    {
        foreach (var reason in Enum.GetValues<NotificationReason>())
        {
            var label = GitHubRules.ReasonLabel(reason);
            Assert.InRange(label.Length, 2, 10);
        }

        Assert.Equal(NotificationReason.Other, GitHubRules.ParseReason("brand_new_reason"));
        Assert.Equal("review", GitHubRules.ReasonLabel(NotificationReason.ReviewRequested));
    }

    [Fact]
    public void S617_notifications_map_with_account_and_resolved_link()
    {
        var wire = JsonSerializer.Deserialize(
            """
            [
              {"id":"1","unread":true,"reason":"review_requested","updated_at":"2026-10-04T11:00:00Z",
               "subject":{"title":"feat: x","type":"PullRequest","url":"https://api.github.com/repos/o/r/pulls/5","latest_comment_url":"https://api.github.com/repos/o/r/issues/comments/9"},
               "repository":{"full_name":"o/r","html_url":"https://github.com/o/r"}},
              {"id":"2","unread":false,"reason":"subscribed","updated_at":"2026-10-04T10:00:00Z",
               "subject":{"title":"CI","type":"CheckSuite","url":null},
               "repository":{"full_name":"o/r","html_url":"https://github.com/o/r"}},
              {"id":"","reason":"mention","updated_at":"2026-10-04T10:00:00Z","repository":{"full_name":"o/r","html_url":"https://github.com/o/r"}}
            ]
            """,
            GitHubWireContext.Default.ListGhNotification);

        var items = GitHubRules.ToNotifications("acc", wire);

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal("acc", i.AccountId));
        Assert.Equal("https://github.com/o/r/pull/5", items[0].HtmlUrl);
        Assert.Equal(NotificationReason.ReviewRequested, items[0].Reason);
        Assert.Equal("https://github.com/o/r", items[1].HtmlUrl);
        Assert.False(items[1].Unread);
    }

    [Theory]
    [InlineData("PullRequest", "Pull request")]
    [InlineData("Issue", "Issue")]
    [InlineData("CheckSuite", "CI")]
    [InlineData(null, "Update")]
    [InlineData("SomethingNew", "Update")]
    public void S617_subject_types_read_as_words(string? type, string expected)
        => Assert.Equal(expected, GitHubRules.SubjectLabel(type));

    // ---- [S-622] merging accounts --------------------------------------------------------------

    private static SearchItem Item(string account, int number, int minutesAgo, bool draft = false, string repo = "o/r")
        => new(account, number, $"#{number}", $"https://github.com/{repo}/issues/{number}", repo, Now.AddMinutes(-minutesAgo), draft, "me");

    [Fact]
    public void S622_search_results_merge_newest_first_and_cut_to_count()
    {
        var merged = GitHubRules.MergeSearch(
            [[Item("a", 1, 30), Item("a", 2, 5)], [Item("b", 3, 10)]],
            count: 2);

        Assert.Equal([2, 3], merged.Select(i => i.Number));
    }

    [Fact]
    public void S622_the_same_issue_seen_by_two_accounts_is_listed_once()
    {
        var merged = GitHubRules.MergeSearch([[Item("a", 1, 30)], [Item("b", 1, 30)]], count: 10);
        Assert.Single(merged);
    }

    [Fact]
    public void S616_drafts_are_dropped_unless_asked_for()
    {
        IReadOnlyList<SearchItem>[] input = [[Item("a", 1, 1, draft: true), Item("a", 2, 2)]];

        Assert.Equal([2], GitHubRules.MergeSearch(input, 10, includeDrafts: false).Select(i => i.Number));
        Assert.Equal([1, 2], GitHubRules.MergeSearch(input, 10, includeDrafts: true).Select(i => i.Number));
    }

    // ---- [S-620] grouping ----------------------------------------------------------------------

    private static NotificationItem Note(string id, string repo, int minutesAgo, bool unread = true)
        => new("a", id, unread, NotificationReason.Comment, Now.AddMinutes(-minutesAgo), id, "Issue", $"https://github.com/{repo}/issues/1", repo, $"https://github.com/{repo}");

    [Fact]
    public void S620_groups_by_repository_ordered_by_their_newest_thread()
    {
        var items = GitHubRules.MergeNotifications(
            [[Note("1", "a/x", 50), Note("2", "b/y", 5)], [Note("3", "a/x", 1, unread: false), Note("4", "c/z", 100)]],
            count: 10);

        var groups = GitHubRules.GroupByRepository(items);

        Assert.Equal(["a/x", "b/y", "c/z"], groups.Select(g => g.Repo));
        Assert.Equal(["3", "1"], groups[0].Items.Select(i => i.Id));
        Assert.Equal(1, groups[0].Unread);
    }

    [Fact]
    public void S620_same_name_on_two_hosts_is_two_groups()
    {
        var github = Note("1", "o/r", 1);
        var enterprise = github with { Id = "2", RepoHtmlUrl = "https://ghe.corp/o/r" };

        Assert.Equal(2, GitHubRules.GroupByRepository([github, enterprise]).Count);
    }

    // ---- [S-629] text --------------------------------------------------------------------------

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1000, "1k")]
    [InlineData(1234, "1.2k")]
    [InlineData(12_345, "12k")]
    [InlineData(3_456_789, "3.4M")]
    public void S629_counts_shorten_like_github(long value, string expected)
        => Assert.Equal(expected, GitHubRules.Compact(value));

    [Fact]
    public void S629_relative_times()
    {
        Assert.Equal("now", GitHubRules.Ago(Now.AddSeconds(-20), Now));
        Assert.Equal("now", GitHubRules.Ago(Now.AddMinutes(3), Now)); // provider clock ahead
        Assert.Equal("5m", GitHubRules.Ago(Now.AddMinutes(-5), Now));
        Assert.Equal("3h", GitHubRules.Ago(Now.AddHours(-3), Now));
        Assert.Equal("2d", GitHubRules.Ago(Now.AddDays(-2), Now));
        Assert.Equal("4 Mar", GitHubRules.Ago(new DateTimeOffset(2026, 3, 4, 9, 0, 0, TimeSpan.Zero), Now));
        Assert.Equal("Mar 2025", GitHubRules.Ago(new DateTimeOffset(2025, 3, 4, 9, 0, 0, TimeSpan.Zero), Now));
        Assert.Equal(string.Empty, GitHubRules.Ago(null, Now));
    }
}
