namespace Themia.Core.Developer;

/// <summary>Where a pull request stands. Merged is its own state: GitHub reports it as <c>closed</c>.</summary>
public enum PullState
{
    Open,
    Merged,
    Closed,
}

/// <summary>[S-610] Where a workflow run stands, folded from GitHub's <c>status</c> and <c>conclusion</c>.</summary>
public enum RunState
{
    /// <summary>Waiting for a runner (<c>queued</c>, <c>waiting</c>, <c>requested</c>, <c>pending</c>).</summary>
    Queued,

    Running,

    Success,

    /// <summary><c>failure</c> or <c>timed_out</c>: something to look at.</summary>
    Failure,

    /// <summary>Finished without a verdict: cancelled, skipped, neutral, stale.</summary>
    Other,
}

/// <summary>
/// [S-618] Why GitHub sent a notification. A closed set, parsed from the API's string so a reason
/// GitHub adds later is <see cref="Other"/> rather than an unknown string reaching the screen.
/// </summary>
public enum NotificationReason
{
    Assign,
    Author,
    Comment,
    CiActivity,
    Invitation,
    Manual,
    Mention,
    ReviewRequested,
    SecurityAlert,
    StateChange,
    Subscribed,
    TeamMention,
    ApprovalRequested,
    Other,
}

/// <summary>[S-618] How loudly a reason is drawn.</summary>
public enum ReasonTone
{
    /// <summary>A security advisory: the critical colour.</summary>
    Critical,

    /// <summary>Someone wants something from you: a review, a mention, an assignment.</summary>
    Attention,

    /// <summary>Activity on something you wrote.</summary>
    Accent,

    Neutral,
}

/// <summary>A repository reference, <c>owner/name</c>, checked to be one.</summary>
public sealed record RepoRef(string Owner, string Name)
{
    public string FullName => $"{Owner}/{Name}";

    public override string ToString() => FullName;
}

/// <summary>The repository's own numbers.</summary>
public sealed record RepoSummary(
    string FullName,
    string HtmlUrl,
    long Stars,
    long Forks,
    long OpenIssues,
    string? Language,
    DateTimeOffset? PushedAt,
    string DefaultBranch);

/// <param name="Message">The first line of the commit message.</param>
public sealed record CommitItem(string Sha, string Message, string HtmlUrl, DateTimeOffset? At);

public sealed record PullItem(int Number, string Title, PullState State, bool Draft, string HtmlUrl, DateTimeOffset CreatedAt);

public sealed record IssueRow(int Number, string Title, string HtmlUrl, DateTimeOffset? CreatedAt);

public sealed record RunItem(long Id, string Name, RunState State, string Branch, DateTimeOffset? At, string HtmlUrl);

public sealed record ReleaseItem(string Tag, string HtmlUrl);

/// <summary>[S-610] Everything the GitHub widget draws for one repository.</summary>
public sealed record RepoActivity(
    RepoSummary Repo,
    IReadOnlyList<RunItem> Runs,
    IReadOnlyList<CommitItem> Commits,
    IReadOnlyList<PullItem> Pulls,
    IReadOnlyList<IssueRow> Issues,
    ReleaseItem? Release);

/// <summary>
/// [S-615][S-616] One issue or pull request from a search, as the assigned-issues and
/// review-request widgets list it.
/// </summary>
/// <param name="AccountId">Which account found it, so a click or a refresh goes back through that account.</param>
/// <param name="Repo"><c>owner/name</c>, read from the API's <c>repository_url</c>.</param>
public sealed record SearchItem(
    string AccountId,
    int Number,
    string Title,
    string HtmlUrl,
    string Repo,
    DateTimeOffset UpdatedAt,
    bool Draft,
    string Author);

/// <summary>[S-617] One notification thread.</summary>
/// <param name="AccountId">[S-621] Which account it belongs to: marking it read goes through that account's host and token.</param>
/// <param name="HtmlUrl">[S-619] Where a click goes: the issue or pull request when that can be worked out, else the repository.</param>
public sealed record NotificationItem(
    string AccountId,
    string Id,
    bool Unread,
    NotificationReason Reason,
    DateTimeOffset UpdatedAt,
    string Title,
    string SubjectType,
    string HtmlUrl,
    string Repo,
    string RepoHtmlUrl);

/// <summary>[S-620] The notifications of one repository, newest first.</summary>
public sealed record NotificationGroup(string Repo, string RepoHtmlUrl, IReadOnlyList<NotificationItem> Items)
{
    public int Unread => Items.Count(i => i.Unread);
}
