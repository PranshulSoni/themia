using Themia.Core.Geometry;
using Themia.Core.Model;
using Themia.Core.Storage;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>The developer widgets' configs through the real state file, and their repairs.</summary>
public sealed class DeveloperConfigTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public DeveloperConfigTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "themia-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "state.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // ignore: temp cleanup is best effort and must not fail a test run
        }
    }

    private async Task<T> RoundTrip<T>(WidgetKind kind, T config)
        where T : WidgetConfig
    {
        var state = AppState.CreateDefault();
        var view = state.ActiveView();
        var widget = WidgetInstance.Create(kind, new FracRect(0.1, 0.1, 0.3, 0.3));
        widget.Config = config;

        view.Widgets.Clear();
        view.Widgets.Add(widget);

        var store = new AppStateStore(_path, TimeSpan.Zero);
        store.SaveSoon(state);
        await store.FlushAsync();

        var loaded = new AppStateStore(_path, TimeSpan.Zero).Load();
        Assert.Equal(LoadOutcome.Loaded, loaded.Outcome);
        return Assert.IsType<T>(loaded.State.ActiveView().Widgets[0].Config);
    }

    [Theory]
    [InlineData(WidgetKind.GitHub, typeof(GitHubConfig))]
    [InlineData(WidgetKind.GitHubIssues, typeof(GitHubIssuesConfig))]
    [InlineData(WidgetKind.GitHubNotifications, typeof(GitHubNotificationsConfig))]
    [InlineData(WidgetKind.GitHubReviews, typeof(GitHubReviewsConfig))]
    [InlineData(WidgetKind.ClaudeUsage, typeof(ClaudeUsageConfig))]
    public void Each_kind_gets_its_own_config(WidgetKind kind, Type type)
    {
        var config = WidgetConfig.CreateDefault(kind);

        Assert.IsType(type, config);
        Assert.Equal(kind, config.Kind);
        Assert.IsType(type, config.Clone());
    }

    [Fact]
    public async Task S610_repository_settings_survive_the_state_file()
    {
        var loaded = await RoundTrip(WidgetKind.GitHub, new GitHubConfig
        {
            AccountIds = ["github:octocat@api.github.com"],
            Repo = "octocat/Hello-World",
            CommitCount = 4,
            PullCount = 2,
            PullFilter = PullFilter.Closed,
            PullOrder = PullOrder.Oldest,
            IssueCount = 0,
            RunCount = 1,
            RefreshMinutes = 15,
        });

        Assert.Equal(["github:octocat@api.github.com"], loaded.AccountIds);
        Assert.Equal("octocat/Hello-World", loaded.Repo);
        Assert.Equal(PullFilter.Closed, loaded.PullFilter);
        Assert.Equal(PullOrder.Oldest, loaded.PullOrder);
        Assert.Equal(0, loaded.EffectiveIssueCount);
        Assert.Equal(TimeSpan.FromMinutes(15), loaded.RefreshInterval);
    }

    [Fact]
    public async Task S617_notification_settings_survive_the_state_file()
    {
        var loaded = await RoundTrip(WidgetKind.GitHubNotifications, new GitHubNotificationsConfig
        {
            Count = 12,
            IncludeRead = true,
            ParticipatingOnly = true,
            GroupByRepository = false,
        });

        Assert.Equal(12, loaded.EffectiveCount);
        Assert.True(loaded.IncludeRead);
        Assert.True(loaded.ParticipatingOnly);
        Assert.False(loaded.GroupByRepository);
    }

    [Fact]
    public async Task S636_claude_settings_survive_the_state_file()
    {
        var loaded = await RoundTrip(WidgetKind.ClaudeUsage, new ClaudeUsageConfig { AccountId = "claude:default", ShowOpus = true, RefreshMinutes = 30 });

        Assert.Equal("claude:default", loaded.AccountId);
        Assert.True(loaded.ShowOpus);
        Assert.False(loaded.ShowSonnet);
        Assert.Equal(30, loaded.RefreshMinutes);
    }

    [Fact]
    public void S626_repair_clamps_the_cadence_and_drops_junk_ids()
    {
        var config = new GitHubIssuesConfig { RefreshMinutes = 1, AccountIds = ["a", "", "a", " ", "b"] };

        var notes = config.Repair();

        Assert.Equal(2, notes.Count);
        Assert.Equal(5, config.RefreshMinutes);
        Assert.Equal(["a", "b"], config.AccountIds);
        Assert.Empty(new GitHubReviewsConfig().Repair());
    }

    [Fact]
    public void Counts_clamp_to_their_ranges()
    {
        Assert.Equal(25, new GitHubIssuesConfig { Count = 99 }.EffectiveCount);
        Assert.Equal(1, new GitHubReviewsConfig { Count = 0 }.EffectiveCount);
        Assert.Equal(20, new GitHubNotificationsConfig { Count = 50 }.EffectiveCount);
        Assert.Equal(10, new GitHubConfig { CommitCount = 50 }.EffectiveCommitCount);
    }
}
