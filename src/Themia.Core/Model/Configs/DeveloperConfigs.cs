using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>[S-613] Which pull requests the GitHub widget lists. Closed includes merged: anything not open.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PullFilter>))]
public enum PullFilter
{
    Open = 0,
    Closed = 1,
    All = 2,
}

/// <summary>[S-613] The order pull requests are listed in, by when they were opened.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PullOrder>))]
public enum PullOrder
{
    Newest = 0,
    Oldest = 1,
}

/// <summary>
/// What every developer widget shares: how often it asks. (Its name is the card's own label, as
/// for every widget; the classic per-widget <c>label</c> is not a second copy of it.) [S-626] The cadence is minutes, five to sixty: GitHub's and Anthropic's limits are per
/// account and shared by every widget on it.
/// </summary>
public abstract class DeveloperConfig : WidgetConfig
{
    public const int MinRefreshMinutes = 5;

    public const int MaxRefreshMinutes = 60;

    /// <summary>How often to ask, in minutes; clamped to five to sixty on use.</summary>
    public int RefreshMinutes { get; set; } = 5;

    [JsonIgnore]
    public TimeSpan RefreshInterval => TimeSpan.FromMinutes(Math.Clamp(RefreshMinutes, MinRefreshMinutes, MaxRefreshMinutes));

    protected void CopyTo(DeveloperConfig other)
    {
        other.RefreshMinutes = RefreshMinutes;
    }

    protected static List<string> CleanIds(List<string>? ids)
        => ids is null ? [] : [.. ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal)];

    public override IReadOnlyList<string> Repair()
    {
        var clamped = Math.Clamp(RefreshMinutes, MinRefreshMinutes, MaxRefreshMinutes);
        if (clamped == RefreshMinutes)
        {
            return [];
        }

        var note = $"{Kind}: refresh {RefreshMinutes} min is outside {MinRefreshMinutes}..{MaxRefreshMinutes}; using {clamped}";
        RefreshMinutes = clamped;
        return [note];
    }
}

/// <summary>A developer widget that reads one or more GitHub accounts.</summary>
public abstract class GitHubAccountsConfig : DeveloperConfig
{
    /// <summary>
    /// [S-623] The GitHub accounts to read. Empty means every GitHub account signed in to Themia
    /// (the repository widget uses the first). Ids only: the token never leaves the account store.
    /// </summary>
    public List<string> AccountIds { get; set; } = [];

    /// <summary>
    /// [S-740] Show as many as the tile has room for: the widget asks for the most a list may hold
    /// and draws the whole rows that fit, so a bigger tile shows more. Off, the counts are exact.
    /// On by default.
    /// </summary>
    public bool FillTile { get; set; } = true;

    protected void CopyAccountsTo(GitHubAccountsConfig other)
    {
        CopyTo(other);
        other.AccountIds = [.. AccountIds];
        other.FillTile = FillTile;
    }

    public override IReadOnlyList<string> Repair()
    {
        var notes = new List<string>(base.Repair());
        var clean = CleanIds(AccountIds);

        if (clean.Count != AccountIds.Count)
        {
            notes.Add($"{Kind}: dropped empty or repeated account ids");
            AccountIds = clean;
        }

        return notes;
    }
}

/// <summary>[S-610] One repository: its numbers, recent commits, pull requests, issues and workflow runs.</summary>
public sealed class GitHubConfig : GitHubAccountsConfig
{
    public const int MaxListCount = 10;

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.GitHub;

    /// <summary>[S-611] <c>owner/name</c>, or a repository's web address.</summary>
    public string Repo { get; set; } = string.Empty;

    public int CommitCount { get; set; } = 3;

    public int PullCount { get; set; } = 3;

    public PullFilter PullFilter { get; set; } = PullFilter.Open;

    public PullOrder PullOrder { get; set; } = PullOrder.Newest;

    public int IssueCount { get; set; } = 3;

    public int RunCount { get; set; } = 5;

    [JsonIgnore]
    public int EffectiveCommitCount => Math.Clamp(CommitCount, 0, MaxListCount);

    [JsonIgnore]
    public int EffectivePullCount => Math.Clamp(PullCount, 0, MaxListCount);

    [JsonIgnore]
    public int EffectiveIssueCount => Math.Clamp(IssueCount, 0, MaxListCount);

    [JsonIgnore]
    public int EffectiveRunCount => Math.Clamp(RunCount, 0, MaxListCount);

    /// <summary>[S-740] What to ask GitHub for: 0 still hides a list; any other count is the most a list holds when filling.</summary>
    public int FetchCount(int effective) => effective <= 0 ? 0 : FillTile ? MaxListCount : effective;

    public override WidgetConfig Clone()
    {
        var copy = new GitHubConfig
        {
            Repo = Repo,
            CommitCount = CommitCount,
            PullCount = PullCount,
            PullFilter = PullFilter,
            PullOrder = PullOrder,
            IssueCount = IssueCount,
            RunCount = RunCount,
        };
        CopyAccountsTo(copy);
        return copy;
    }
}

/// <summary>[S-615] Open issues assigned to you, across your GitHub accounts.</summary>
public sealed class GitHubIssuesConfig : GitHubAccountsConfig
{
    public const int MinCount = 1;

    public const int MaxCount = 25;

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.GitHubIssues;

    public int Count { get; set; } = 8;

    /// <summary>Also list open issues that mention you.</summary>
    public bool IncludeMentioned { get; set; }

    [JsonIgnore]
    public int EffectiveCount => Math.Clamp(Count, MinCount, MaxCount);

    public override WidgetConfig Clone()
    {
        var copy = new GitHubIssuesConfig { Count = Count, IncludeMentioned = IncludeMentioned };
        CopyAccountsTo(copy);
        return copy;
    }

    /// <summary>[S-740] What to ask for: the most a list may hold when filling the tile, else the count.</summary>
    [JsonIgnore]
    public int FetchCount => FillTile ? MaxCount : EffectiveCount;
}

/// <summary>[S-616] Open pull requests waiting for your review.</summary>
public sealed class GitHubReviewsConfig : GitHubAccountsConfig
{
    public const int MinCount = 1;

    public const int MaxCount = 25;

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.GitHubReviews;

    public int Count { get; set; } = 8;

    /// <summary>Also list draft pull requests. Off by default: a draft is not yet asking.</summary>
    public bool IncludeDrafts { get; set; }

    [JsonIgnore]
    public int EffectiveCount => Math.Clamp(Count, MinCount, MaxCount);

    public override WidgetConfig Clone()
    {
        var copy = new GitHubReviewsConfig { Count = Count, IncludeDrafts = IncludeDrafts };
        CopyAccountsTo(copy);
        return copy;
    }

    /// <summary>[S-740] What to ask for: the most a list may hold when filling the tile, else the count.</summary>
    [JsonIgnore]
    public int FetchCount => FillTile ? MaxCount : EffectiveCount;
}

/// <summary>[S-617] Your GitHub notifications.</summary>
public sealed class GitHubNotificationsConfig : GitHubAccountsConfig
{
    public const int MinCount = 1;

    public const int MaxCount = 20;

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.GitHubNotifications;

    public int Count { get; set; } = 5;

    /// <summary>Also list notifications already read.</summary>
    public bool IncludeRead { get; set; }

    /// <summary>Only threads you take part in or were mentioned in.</summary>
    public bool ParticipatingOnly { get; set; }

    /// <summary>[S-620] Group the list under each repository's name.</summary>
    public bool GroupByRepository { get; set; } = true;

    [JsonIgnore]
    public int EffectiveCount => Math.Clamp(Count, MinCount, MaxCount);

    public override WidgetConfig Clone()
    {
        var copy = new GitHubNotificationsConfig
        {
            Count = Count,
            IncludeRead = IncludeRead,
            ParticipatingOnly = ParticipatingOnly,
            GroupByRepository = GroupByRepository,
        };
        CopyAccountsTo(copy);
        return copy;
    }

    /// <summary>[S-740] What to ask for: the most a list may hold when filling the tile, else the count.</summary>
    [JsonIgnore]
    public int FetchCount => FillTile ? MaxCount : EffectiveCount;
}

/// <summary>[S-630] How much of a Claude subscription's limits is used.</summary>
public sealed class ClaudeUsageConfig : DeveloperConfig
{
    public ClaudeUsageConfig() => RefreshMinutes = 15;

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.ClaudeUsage;

    /// <summary>[S-636] The Claude account, or empty for the first one signed in.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Show the weekly Opus limit as its own bar.</summary>
    public bool ShowOpus { get; set; }

    /// <summary>Show the weekly Sonnet limit as its own bar.</summary>
    public bool ShowSonnet { get; set; }

    public override WidgetConfig Clone()
    {
        var copy = new ClaudeUsageConfig { AccountId = AccountId, ShowOpus = ShowOpus, ShowSonnet = ShowSonnet };
        CopyTo(copy);
        return copy;
    }
}
