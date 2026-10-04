using System.Text.Json.Serialization;

namespace Themia.Core.Developer;

// The GitHub REST shapes the developer widgets read, and nothing more. Every property is nullable
// because every one of them is data from the network: GitHubRules maps them into the model and
// drops an item that is missing what the widget needs, rather than drawing half of it.

/// <summary><c>GET /repos/{owner}/{repo}</c>.</summary>
public sealed class GhRepo
{
    [JsonPropertyName("full_name")]
    public string? FullName { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("stargazers_count")]
    public long? Stars { get; set; }

    [JsonPropertyName("forks_count")]
    public long? Forks { get; set; }

    /// <summary>Issues and pull requests together, as GitHub counts them.</summary>
    [JsonPropertyName("open_issues_count")]
    public long? OpenIssues { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("pushed_at")]
    public DateTimeOffset? PushedAt { get; set; }

    [JsonPropertyName("default_branch")]
    public string? DefaultBranch { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary><c>GET /repos/{owner}/{repo}/actions/runs</c>.</summary>
public sealed class GhRunList
{
    [JsonPropertyName("workflow_runs")]
    public List<GhRun>? Runs { get; set; }
}

public sealed class GhRun
{
    [JsonPropertyName("id")]
    public long? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary><c>queued</c>, <c>in_progress</c>, <c>completed</c>, <c>waiting</c>, <c>requested</c>, <c>pending</c>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary><c>success</c>, <c>failure</c>, <c>cancelled</c>, <c>skipped</c>, <c>timed_out</c>… or null while running.</summary>
    [JsonPropertyName("conclusion")]
    public string? Conclusion { get; set; }

    [JsonPropertyName("head_branch")]
    public string? HeadBranch { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    /// <summary><c>push</c>, <c>pull_request</c>, <c>schedule</c>, <c>workflow_dispatch</c>…</summary>
    [JsonPropertyName("event")]
    public string? Event { get; set; }

    [JsonPropertyName("actor")]
    public GhUser? Actor { get; set; }
}

/// <summary>One entry of <c>GET /repos/{owner}/{repo}/commits</c>.</summary>
public sealed class GhCommit
{
    [JsonPropertyName("sha")]
    public string? Sha { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("commit")]
    public GhCommitDetail? Commit { get; set; }

    /// <summary>The GitHub user the commit is attributed to, when GitHub could match one.</summary>
    [JsonPropertyName("author")]
    public GhUser? Author { get; set; }
}

public sealed class GhCommitDetail
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("author")]
    public GhCommitAuthor? Author { get; set; }
}

public sealed class GhCommitAuthor
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("date")]
    public DateTimeOffset? Date { get; set; }
}

public sealed class GhUser
{
    [JsonPropertyName("login")]
    public string? Login { get; set; }
}

/// <summary>One entry of <c>GET /repos/{owner}/{repo}/pulls</c>.</summary>
public sealed class GhPull
{
    [JsonPropertyName("number")]
    public int? Number { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("merged_at")]
    public DateTimeOffset? MergedAt { get; set; }

    [JsonPropertyName("draft")]
    public bool? Draft { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("user")]
    public GhUser? User { get; set; }
}

/// <summary>
/// An issue as <c>/repos/…/issues</c> and <c>/search/issues</c> return it. Pull requests come
/// back from both and carry a <c>pull_request</c> object.
/// </summary>
public sealed class GhIssue
{
    [JsonPropertyName("number")]
    public int? Number { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    /// <summary><c>https://api.github.com/repos/{owner}/{repo}</c>.</summary>
    [JsonPropertyName("repository_url")]
    public string? RepositoryUrl { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("draft")]
    public bool? Draft { get; set; }

    [JsonPropertyName("user")]
    public GhUser? User { get; set; }

    [JsonPropertyName("pull_request")]
    public GhPullMarker? PullRequest { get; set; }

    [JsonPropertyName("comments")]
    public int? Comments { get; set; }

    [JsonPropertyName("labels")]
    public List<GhLabel>? Labels { get; set; }
}

public sealed class GhLabel
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Six hex digits, no <c>#</c>.</summary>
    [JsonPropertyName("color")]
    public string? Color { get; set; }
}

public sealed class GhPullMarker
{
    [JsonPropertyName("merged_at")]
    public DateTimeOffset? MergedAt { get; set; }
}

/// <summary><c>GET /repos/{owner}/{repo}/releases/latest</c>.</summary>
public sealed class GhRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }
}

/// <summary><c>GET /search/issues</c>.</summary>
public sealed class GhSearch
{
    [JsonPropertyName("total_count")]
    public int? TotalCount { get; set; }

    [JsonPropertyName("items")]
    public List<GhIssue>? Items { get; set; }
}

/// <summary>One entry of <c>GET /notifications</c>.</summary>
public sealed class GhNotification
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("unread")]
    public bool? Unread { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("subject")]
    public GhSubject? Subject { get; set; }

    [JsonPropertyName("repository")]
    public GhNotificationRepo? Repository { get; set; }
}

public sealed class GhSubject
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary><c>Issue</c>, <c>PullRequest</c>, <c>Release</c>, <c>CheckSuite</c>, <c>Discussion</c>…</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("latest_comment_url")]
    public string? LatestCommentUrl { get; set; }
}

public sealed class GhNotificationRepo
{
    [JsonPropertyName("full_name")]
    public string? FullName { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }
}

/// <summary>Source-generated contracts for the GitHub endpoints. Native AOT has no reflection serializer.</summary>
[JsonSourceGenerationOptions(NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(GhRepo))]
[JsonSerializable(typeof(GhRunList))]
[JsonSerializable(typeof(List<GhCommit>))]
[JsonSerializable(typeof(List<GhPull>))]
[JsonSerializable(typeof(List<GhIssue>))]
[JsonSerializable(typeof(GhRelease))]
[JsonSerializable(typeof(GhSearch))]
[JsonSerializable(typeof(List<GhNotification>))]
public sealed partial class GitHubWireContext : JsonSerializerContext;
