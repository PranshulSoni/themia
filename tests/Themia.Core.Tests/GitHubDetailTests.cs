using System.Text.Json;
using Themia.Core.Localization;
using Themia.Core.Developer;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// [S-740]..[S-749] The GitHub widgets' third pass: filling the tile, the fields the bigger rows
/// show, anonymous public access, the saved-copy note, and the menus' addresses.
/// </summary>
public sealed class GitHubDetailTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri Api = GitHubRules.ApiRoot(null);

    // ---- [S-740] filling the tile ------------------------------------------------------------------

    [Fact]
    public void S740_fill_is_on_by_default_and_asks_for_the_most_a_list_holds()
    {
        Assert.True(new GitHubIssuesConfig().FillTile);
        Assert.Equal(GitHubIssuesConfig.MaxCount, new GitHubIssuesConfig { Count = 3 }.FetchCount);
        Assert.Equal(GitHubReviewsConfig.MaxCount, new GitHubReviewsConfig().FetchCount);
        Assert.Equal(GitHubNotificationsConfig.MaxCount, new GitHubNotificationsConfig().FetchCount);
    }

    [Fact]
    public void S740_off_the_count_is_exact()
    {
        Assert.Equal(3, new GitHubIssuesConfig { Count = 3, FillTile = false }.FetchCount);
        Assert.Equal(20, new GitHubNotificationsConfig { Count = 50, FillTile = false }.FetchCount);
    }

    [Fact]
    public void S740_a_repository_list_at_zero_stays_hidden_while_filling()
    {
        var config = new GitHubConfig { CommitCount = 0, PullCount = 2 };

        Assert.Equal(0, config.FetchCount(config.EffectiveCommitCount));
        Assert.Equal(GitHubConfig.MaxListCount, config.FetchCount(config.EffectivePullCount));

        config.FillTile = false;
        Assert.Equal(2, config.FetchCount(config.EffectivePullCount));
    }

    [Fact]
    public void S740_fill_is_cloned()
    {
        var clone = (GitHubReviewsConfig)new GitHubReviewsConfig { FillTile = false }.Clone();

        Assert.False(clone.FillTile);
    }

    // ---- [S-743][S-744][S-745] the fields --------------------------------------------------------

    [Theory]
    [InlineData("rosa", 4, "rosa · 4 comments")]
    [InlineData("rosa", 1, "rosa · 1 comment")]
    [InlineData("rosa", 0, "rosa")]
    [InlineData("", 3, "3 comments")]
    [InlineData(null, -2, "")]
    public void S743_a_row_detail_says_who_and_how_much_talk(string? author, int comments, string expected)
    {
        Assert.Equal(expected, GitHubRules.ItemDetail(author, comments, Locale.En));
    }

    [Fact]
    public void S744_issues_carry_author_comments_and_checked_labels()
    {
        var list = JsonSerializer.Deserialize(
            """
            [{"number":3,"title":"Bug","html_url":"https://github.com/o/r/issues/3","created_at":"2026-10-01T00:00:00Z",
              "user":{"login":"rosa"},"comments":4,
              "labels":[{"name":"bug","color":"d73a4a"},{"name":"odd","color":"red"},{"name":"","color":"000000"},
                        {"name":"a","color":"000000"},{"name":"b"},{"name":"c"},{"name":"d"},{"name":"e"}]}]
            """,
            GitHubWireContext.Default.ListGhIssue);

        var issue = Assert.Single(GitHubRules.ToIssues(list, 5));

        Assert.Equal("rosa", issue.Author);
        Assert.Equal(4, issue.Comments);
        Assert.Equal(GitHubRules.MaxLabels, issue.Labels!.Count);
        Assert.Equal(new IssueLabel("bug", "D73A4A"), issue.Labels[0]);
        Assert.Equal(new IssueLabel("odd", string.Empty), issue.Labels[1]);
        Assert.DoesNotContain(issue.Labels, l => l.Name.Length == 0);
    }

    [Fact]
    public void S744_search_items_carry_comments_and_labels()
    {
        var search = JsonSerializer.Deserialize(
            """
            {"total_count":1,"items":[{"number":9,"title":"T","html_url":"https://github.com/o/r/pull/9",
              "repository_url":"https://api.github.com/repos/o/r","updated_at":"2026-10-04T10:00:00Z",
              "user":{"login":"kenji"},"comments":-3,"labels":[{"name":"docs","color":"0075ca"}]}]}
            """,
            GitHubWireContext.Default.GhSearch);

        var item = Assert.Single(GitHubRules.ToSearchItems("a", search));

        Assert.Equal("kenji", item.Author);
        Assert.Equal(0, item.Comments);
        Assert.Equal("0075CA", Assert.Single(item.Labels!).Color);
    }

    [Fact]
    public void S744_commits_pulls_and_runs_say_who_and_why()
    {
        var commits = JsonSerializer.Deserialize(
            """
            [{"sha":"abcdef0123","html_url":"https://github.com/o/r/commit/abcdef0","commit":{"message":"Fix\nmore","author":{"name":"Rosa Lind","date":"2026-10-04T10:00:00Z"}},"author":{"login":"rosa"}},
             {"sha":"bbbbbbb123","html_url":"https://github.com/o/r/commit/bbbbbbb","commit":{"message":"x","author":{"name":" "}},"author":{"login":"kenji"}}]
            """,
            GitHubWireContext.Default.ListGhCommit);
        var pulls = JsonSerializer.Deserialize(
            """[{"number":2,"title":"PR","state":"open","html_url":"https://github.com/o/r/pull/2","created_at":"2026-10-04T10:00:00Z","user":{"login":"ines"}}]""",
            GitHubWireContext.Default.ListGhPull);
        var runs = JsonSerializer.Deserialize(
            """{"workflow_runs":[{"id":1,"name":"CI","status":"completed","conclusion":"success","head_branch":"main","html_url":"https://github.com/o/r/actions/runs/1","event":"pull_request","actor":{"login":"malik"}}]}""",
            GitHubWireContext.Default.GhRunList);

        var parsed = GitHubRules.ToCommits(commits, 5);
        Assert.Equal("Rosa Lind", parsed[0].Author);
        Assert.Equal("kenji", parsed[1].Author);
        Assert.Equal("ines", Assert.Single(GitHubRules.ToPulls(pulls)).Author);

        var run = Assert.Single(GitHubRules.ToRuns(runs, 5));
        Assert.Equal("pull request", run.Event);
        Assert.Equal("malik", run.Actor);
    }

    [Fact]
    public void S745_description_and_release_name_and_date()
    {
        var repo = GitHubRules.ToRepo(JsonSerializer.Deserialize(
            """{"full_name":"o/r","html_url":"https://github.com/o/r","description":"Widgets\nsecond line"}""",
            GitHubWireContext.Default.GhRepo));
        var release = GitHubRules.ToRelease(JsonSerializer.Deserialize(
            """{"tag_name":"v1","html_url":"https://github.com/o/r/releases/tag/v1","name":"Spring","published_at":"2026-10-01T00:00:00Z"}""",
            GitHubWireContext.Default.GhRelease));
        var sameName = GitHubRules.ToRelease(JsonSerializer.Deserialize(
            """{"tag_name":"v1","html_url":"https://github.com/o/r/releases/tag/v1","name":"v1"}""",
            GitHubWireContext.Default.GhRelease));
        var blank = GitHubRules.ToRepo(JsonSerializer.Deserialize(
            """{"full_name":"o/r","html_url":"https://github.com/o/r","description":"  "}""",
            GitHubWireContext.Default.GhRepo));

        Assert.Equal("Widgets", repo!.Description);
        Assert.Null(blank!.Description);
        Assert.Equal("Spring", release!.Name);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), release.PublishedAt);
        Assert.Equal(string.Empty, sameName!.Name);
    }

    // ---- [S-746] anonymous public access -----------------------------------------------------------

    [Fact]
    public void S746_with_no_account_the_repository_widget_reads_anonymously()
    {
        var targets = DevScreens.Targets([], [], single: true, anonymous: true);

        Assert.Equal(DevScreens.AnonymousId, Assert.Single(targets).Id);
        Assert.Empty(DevScreens.Targets([], [], single: true));
    }

    [Fact]
    public void S746_an_account_or_a_choice_wins_over_anonymous()
    {
        var work = new DevAccount("gh:work", "work", null);

        Assert.Equal("gh:work", Assert.Single(DevScreens.Targets([work], [], single: true, anonymous: true)).Id);
        Assert.Empty(DevScreens.Targets([], ["gh:gone"], single: true, anonymous: true));
    }

    [Fact]
    public void S746_anonymous_screens_load_and_show_rather_than_asking_for_a_sign_in()
    {
        var loading = DevScreens.Decide("GitHub", [], [], true, [], false, string.Empty, Now, Locale.En, anonymous: true);
        var content = DevScreens.Decide("GitHub", [], [], true, [new DevOutcome(DevScreens.AnonymousId, FetchStatus.Ok)], true, string.Empty, Now, Locale.En, anonymous: true);
        var chosenGone = DevScreens.Decide("GitHub", [], ["gh:gone"], true, [], false, string.Empty, Now, Locale.En, anonymous: true);

        Assert.Equal(DevScreenKind.Loading, loading.Kind);
        Assert.Equal(DevScreenKind.Content, content.Kind);
        Assert.Equal(DevScreenKind.NoAccounts, chosenGone.Kind);
    }

    [Theory]
    [InlineData(5, false, 5)]
    [InlineData(5, true, 15)]
    [InlineData(30, true, 30)]
    public void S746_anonymous_never_asks_faster_than_every_quarter_hour(int minutes, bool anonymous, int expected)
    {
        Assert.Equal(TimeSpan.FromMinutes(expected), GitHubPolling.Cadence(TimeSpan.FromMinutes(minutes), anonymous));
    }

    // ---- [S-747] the saved-copy note ---------------------------------------------------------------

    [Fact]
    public void S747_content_from_the_saved_copy_says_so_until_it_is_refreshed()
    {
        var work = new DevAccount("gh:work", "work", null);
        var screen = DevScreens.Decide("GitHub", [work], [], false, [new DevOutcome("gh:work", FetchStatus.Loading)], true, "Empty", Now, Locale.En, savedAt: Now.AddHours(-3));

        Assert.Equal(DevScreenKind.Content, screen.Kind);
        Assert.Equal("Showing what was saved 3 hr. ago; refreshing.", screen.Warning);
    }

    [Fact]
    public void S747_a_failed_refresh_outranks_the_saved_note()
    {
        var work = new DevAccount("gh:work", "work", null);
        var screen = DevScreens.Decide("GitHub", [work], [], false, [new DevOutcome("gh:work", FetchStatus.Offline)], true, "Empty", Now, Locale.En, savedAt: Now.AddHours(-3));

        Assert.Equal("Could not refresh. No network.", screen.Warning);
    }

    [Theory]
    [InlineData(0.5, "Showing what was just saved; refreshing.")]
    [InlineData(42, "Showing what was saved 42 min. ago; refreshing.")]
    [InlineData(60 * 26, "Showing what was saved 1 day ago; refreshing.")]
    public void S747_the_note_says_how_old(double minutes, string expected)
    {
        Assert.Equal(expected, DevScreens.Saved(Now.AddMinutes(-minutes), Now, Locale.En));
    }

    // ---- [S-748][S-749] menus ------------------------------------------------------------------------

    [Fact]
    public void S748_mark_all_read_puts_to_the_accounts_notifications()
    {
        Assert.Equal("https://api.github.com/notifications", GitHubRules.AllNotificationsUrl(Api).AbsoluteUri);
        Assert.Equal("https://ghe.example.com/api/v3/notifications", GitHubRules.AllNotificationsUrl(GitHubRules.ApiRoot("https://ghe.example.com/api/v3")).AbsoluteUri);
    }

    [Theory]
    [InlineData("https://github.com/o/r/issues/12", "https://github.com/o/r")]
    [InlineData("https://github.com/o/r/pull/9", "https://github.com/o/r")]
    [InlineData("https://ghe.example.com/team/svc/pull/1290", "https://ghe.example.com/team/svc")]
    [InlineData("https://github.com/o/r", null)]
    [InlineData("https://github.com/o/r/commit/abc", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData(null, null)]
    public void S749_open_repository_from_an_item_page(string? item, string? repo)
    {
        Assert.Equal(repo, GitHubRules.RepoPageOf(item));
    }
}
