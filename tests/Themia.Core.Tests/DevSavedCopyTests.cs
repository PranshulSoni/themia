using Themia.Core.Developer;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>[S-747] The developer widgets' saved copies: what is written, and what is refused on the way back.</summary>
public sealed class DevSavedCopyTests
{
    private const string Key = "search|Assigned||False,True,25|5";
    private const string Account = "github:octo@api.github.com";

    private static readonly DateTimeOffset At = new(2026, 10, 4, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = At.AddHours(3);

    private static IReadOnlyList<SearchItem> Items() =>
    [
        new(Account, 231, "Clock drifts after sleep", "https://github.com/o/r/issues/231", "o/r", At, false, "rosa", 4, [new("bug", "D73A4A")]),
        new(Account, 7, "Docs", "https://github.com/o/d/issues/7", "o/d", At.AddDays(-1), true, string.Empty),
    ];

    [Fact]
    public void S747_a_saved_list_reads_back_whole_with_when_it_was_fetched()
    {
        var json = DevSavedCopy.Encode(Key, Account, At, Items(), DevSavedJsonContext.Default.IReadOnlyListSearchItem);

        var saved = DevSavedCopy.Decode(json, Key, Account, DevSavedJsonContext.Default.IReadOnlyListSearchItem, Now);

        Assert.NotNull(saved);
        Assert.Equal(At, saved.At);
        Assert.Equal(2, saved.Value.Count);
        Assert.Equal("rosa", saved.Value[0].Author);
        Assert.Equal(4, saved.Value[0].Comments);
        Assert.Equal("D73A4A", Assert.Single(saved.Value[0].Labels!).Color);
        Assert.True(saved.Value[1].Draft);
    }

    [Fact]
    public void S747_a_repository_with_enums_and_nested_lists_round_trips()
    {
        var activity = new RepoActivity(
            new RepoSummary("o/r", "https://github.com/o/r", 12, 3, 4, "C#", At, "main", "A repo"),
            [new RunItem(1, "CI", RunState.Failure, "main", At, "https://github.com/o/r/actions/runs/1", "push", "rosa")],
            [new CommitItem("abcdef0", "Fix", "https://github.com/o/r/commit/abcdef0", At, "Rosa")],
            [new PullItem(2, "PR", PullState.Merged, false, "https://github.com/o/r/pull/2", At, "kenji")],
            [new IssueRow(3, "Bug", "https://github.com/o/r/issues/3", At)],
            new ReleaseItem("v1", "https://github.com/o/r/releases/tag/v1", "First", At));

        var json = DevSavedCopy.Encode("repo|k", Account, At, activity, DevSavedJsonContext.Default.RepoActivity);
        var saved = DevSavedCopy.Decode(json, "repo|k", Account, DevSavedJsonContext.Default.RepoActivity, Now);

        Assert.NotNull(saved);
        Assert.Equal(RunState.Failure, saved.Value.Runs[0].State);
        Assert.Equal(PullState.Merged, saved.Value.Pulls[0].State);
        Assert.Equal("A repo", saved.Value.Repo.Description);
        Assert.Equal("First", saved.Value.Release!.Name);
        Assert.Contains("\"Failure\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void S747_a_copy_never_answers_another_feed_or_account()
    {
        var json = DevSavedCopy.Encode(Key, Account, At, Items(), DevSavedJsonContext.Default.IReadOnlyListSearchItem);

        Assert.Null(DevSavedCopy.Decode(json, Key + "x", Account, DevSavedJsonContext.Default.IReadOnlyListSearchItem, Now));
        Assert.Null(DevSavedCopy.Decode(json, Key, "github:other", DevSavedJsonContext.Default.IReadOnlyListSearchItem, Now));
    }

    [Fact]
    public void S747_a_copy_older_than_a_week_is_not_shown()
    {
        var json = DevSavedCopy.Encode(Key, Account, At, Items(), DevSavedJsonContext.Default.IReadOnlyListSearchItem);

        Assert.NotNull(DevSavedCopy.Decode(json, Key, Account, DevSavedJsonContext.Default.IReadOnlyListSearchItem, At.AddDays(7)));
        Assert.Null(DevSavedCopy.Decode(json, Key, Account, DevSavedJsonContext.Default.IReadOnlyListSearchItem, At.AddDays(7).AddMinutes(1)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"key\":\"k\"}")]
    [InlineData("{\"schemaVersion\":99,\"key\":\"k\",\"account\":\"a\",\"at\":\"2026-10-04T09:30:00+00:00\",\"value\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"key\":\"k\",\"account\":\"a\",\"at\":\"yesterday\",\"value\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"key\":\"k\",\"account\":\"a\",\"at\":\"2026-10-04T09:30:00+00:00\",\"value\":null}")]
    [InlineData("{\"schemaVersion\":1,\"key\":\"k\",\"account\":\"a\",\"at\":\"2026-10-04T09:30:00+00:00\",\"value\":{\"oops\":1}}")]
    public void S747_a_corrupt_or_foreign_file_is_nothing_never_half_a_list(string json)
    {
        Assert.Null(DevSavedCopy.Decode(json, "k", "a", DevSavedJsonContext.Default.IReadOnlyListSearchItem, Now));
    }

    [Fact]
    public void S747_a_copy_from_a_clock_ahead_is_not_newer_than_now()
    {
        var json = DevSavedCopy.Encode(Key, Account, Now.AddHours(1), Items(), DevSavedJsonContext.Default.IReadOnlyListSearchItem);

        Assert.Equal(Now, DevSavedCopy.Decode(json, Key, Account, DevSavedJsonContext.Default.IReadOnlyListSearchItem, Now)!.At);
    }

    [Fact]
    public void S747_file_names_hide_the_feed_and_differ_per_account()
    {
        var name = DevSavedCopy.FileName(Key, Account);

        Assert.Matches("^[0-9a-f]{32}\\.json$", name);
        Assert.DoesNotContain("octo", name, StringComparison.Ordinal);
        Assert.NotEqual(name, DevSavedCopy.FileName(Key, "github:other"));
        Assert.Equal(name, DevSavedCopy.FileName(Key, Account));
    }
}
