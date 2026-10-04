using System.Globalization;
using Themia.Core.Localization;
using Themia.Core.Model;

namespace Themia.Core.Developer;

/// <summary>
/// [S-610]..[S-622] The GitHub widgets' rules: what a repository reference may be, the addresses
/// asked, how GitHub's JSON becomes the model, how pull requests are filtered and sorted, how
/// several accounts' answers are merged, and how notifications are grouped and labelled. Pure, so
/// every one is tested; the network half is <c>GitHubService</c> in the app.
/// </summary>
public static class GitHubRules
{
    /// <summary>[S-744] Labels kept per issue: a row has room for two chips, the tooltip for the rest.</summary>
    public const int MaxLabels = 5;

    // ---- repository references ---------------------------------------------------------------

    /// <summary>
    /// [S-611] A repository from what the user typed: <c>owner/name</c>, or a web address of one
    /// (<c>https://github.com/owner/name</c>, an Enterprise host, a trailing <c>.git</c> or deeper
    /// path). Null when it is neither.
    /// <para>
    /// The host of a typed address is deliberately not used: the account decides where requests
    /// go ([S-39] in the classic app), so a token is only ever sent to the API root the user
    /// signed in to, never to a host pasted into a repository box.
    /// </para>
    /// </summary>
    public static RepoRef? ParseRepo(string? input)
    {
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 300)
        {
            return null;
        }

        string[] parts;
        if (text.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out var url)
                || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
            {
                return null;
            }

            parts = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        }
        else
        {
            parts = text.Split('/', StringSplitOptions.RemoveEmptyEntries);

            // "github.com/owner/name", pasted without its scheme: a host has a dot, a login never does.
            if (parts.Length >= 3 && parts[0].Contains('.', StringComparison.Ordinal))
            {
                parts = parts[1..];
            }
            else if (parts.Length != 2)
            {
                return null;
            }
        }

        if (parts.Length < 2)
        {
            return null;
        }

        var owner = parts[0];
        var name = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];

        return IsOwner(owner) && IsRepoName(name) ? new RepoRef(owner, name) : null;
    }

    /// <summary>GitHub logins: letters, digits and single hyphens, up to 39 characters.</summary>
    private static bool IsOwner(string owner)
        => owner.Length is > 0 and <= 39
            && owner.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            && owner[0] != '-';

    /// <summary>Repository names: letters, digits, <c>.</c>, <c>-</c> and <c>_</c>; never a path step.</summary>
    private static bool IsRepoName(string name)
        => name.Length is > 0 and <= 100
            && name is not ("." or "..")
            && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');

    /// <summary>
    /// [S-615] <c>owner/name</c> from an API <c>repository_url</c>
    /// (<c>https://api.github.com/repos/owner/name</c>, or the Enterprise <c>/api/v3/repos/…</c>).
    /// Empty when it is not that shape.
    /// </summary>
    public static string RepoFromRepositoryUrl(string? repositoryUrl)
    {
        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var url))
        {
            return string.Empty;
        }

        var parts = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var at = Array.IndexOf(parts, "repos");

        return at >= 0 && at + 3 == parts.Length && IsOwner(parts[at + 1]) && IsRepoName(parts[at + 2])
            ? $"{parts[at + 1]}/{parts[at + 2]}"
            : string.Empty;
    }

    /// <summary>
    /// [S-749] The repository page an issue or pull request lives in, from its own page:
    /// <c>…/owner/name/issues/12</c> → <c>…/owner/name</c>. Null when it is not that shape.
    /// </summary>
    public static string? RepoPageOf(string? itemHtmlUrl)
    {
        if (WebUrl(itemHtmlUrl) is not { } raw || !Uri.TryCreate(raw, UriKind.Absolute, out var url))
        {
            return null;
        }

        var parts = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 4 && parts[^2] is "issues" or "pull" or "pulls" && IsOwner(parts[^4]) && IsRepoName(parts[^3])
            ? $"{url.Scheme}://{url.Authority}/{string.Join('/', parts[..^2])}"
            : null;
    }

    // ---- addresses -----------------------------------------------------------------------------

    /// <summary>The account's REST root as a base address ending in a slash, so relative paths append.</summary>
    public static Uri ApiRoot(string? apiBaseUrl)
    {
        var normalised = Accounts.OAuthClients.GitHub.NormaliseApiBase(apiBaseUrl) ?? Accounts.OAuthClients.GitHub.DefaultApiBase;
        return new Uri(normalised + "/");
    }

    private static string Escape(RepoRef repo) => $"{Uri.EscapeDataString(repo.Owner)}/{Uri.EscapeDataString(repo.Name)}";

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>[S-612] The repository itself.</summary>
    public static Uri RepoUrl(Uri api, RepoRef repo) => new(api, $"repos/{Escape(repo)}");

    /// <summary>[S-612] Recent workflow runs. Always at least one: the newest run is the header's CI status.</summary>
    public static Uri RunsUrl(Uri api, RepoRef repo, int count) => new(api, $"repos/{Escape(repo)}/actions/runs?per_page={Count(Math.Clamp(count, 1, 100))}");

    public static Uri CommitsUrl(Uri api, RepoRef repo, int count) => new(api, $"repos/{Escape(repo)}/commits?per_page={Count(Math.Clamp(count, 1, 100))}");

    /// <summary>
    /// [S-612] Pull requests in every state, newest first, over-fetched: the state filter is applied
    /// here rather than by GitHub, so one request serves every filter, and there must be enough left
    /// after filtering to fill the count.
    /// </summary>
    public static Uri PullsUrl(Uri api, RepoRef repo, int count)
        => new(api, $"repos/{Escape(repo)}/pulls?state=all&sort=created&direction=desc&per_page={Count(PullsToFetch(count))}");

    public static int PullsToFetch(int count) => Math.Min((Math.Max(count, 1) * 3) + 5, 100);

    /// <summary>[S-612] Open issues, with five spare: GitHub returns pull requests here too and they are dropped.</summary>
    public static Uri IssuesUrl(Uri api, RepoRef repo, int count) => new(api, $"repos/{Escape(repo)}/issues?state=open&per_page={Count(Math.Clamp(count + 5, 1, 100))}");

    public static Uri LatestReleaseUrl(Uri api, RepoRef repo) => new(api, $"repos/{Escape(repo)}/releases/latest");

    /// <summary>[S-615] The assigned-issues query; with mentions OR-ed in when asked.</summary>
    public static string AssignedQuery(bool includeMentioned)
        => includeMentioned
            ? "is:open is:issue archived:false (assignee:@me OR mentions:@me)"
            : "is:open is:issue archived:false assignee:@me";

    /// <summary>[S-616] Open pull requests waiting on the account's review.</summary>
    public const string ReviewQuery = "is:open is:pr review-requested:@me archived:false";

    /// <summary>[S-615][S-616] An issue search, most recently updated first.</summary>
    public static Uri SearchUrl(Uri api, string query, int count)
        => new(api, $"search/issues?q={Uri.EscapeDataString(query)}&sort=updated&order=desc&per_page={Count(Math.Clamp(count, 1, 100))}");

    /// <summary>[S-617] The notifications list.</summary>
    public static Uri NotificationsUrl(Uri api, bool includeRead, bool participatingOnly, int count)
        => new(api, $"notifications?all={(includeRead ? "true" : "false")}&participating={(participatingOnly ? "true" : "false")}&per_page={Count(Math.Clamp(count, 1, 50))}");

    /// <summary>[S-621] One thread, for <c>PATCH</c> (mark read). The id is provider data: escaped into one segment.</summary>
    public static Uri ThreadUrl(Uri api, string threadId) => new(api, $"notifications/threads/{Uri.EscapeDataString(threadId)}");

    /// <summary>[S-748] Every notification of the account, for <c>PUT</c> (mark all read).</summary>
    public static Uri AllNotificationsUrl(Uri api) => new(api, "notifications");

    /// <summary>[S-621] A repository's notifications, for <c>PUT</c> (mark all read).</summary>
    public static Uri RepoNotificationsUrl(Uri api, RepoRef repo) => new(api, $"repos/{Escape(repo)}/notifications");

    // ---- mapping -------------------------------------------------------------------------------

    /// <summary>
    /// An address a row may open: absolute http or https and nothing else. Everything a click
    /// opens is data from the network; the launcher checks it again, this keeps a bad one off the
    /// screen in the first place.
    /// </summary>
    public static string? WebUrl(string? raw)
        => Uri.TryCreate(raw, UriKind.Absolute, out var url)
            && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp)
            && string.IsNullOrEmpty(url.UserInfo)
                ? url.AbsoluteUri
                : null;

    /// <summary>The first line, trimmed; a commit message or a title is never drawn across lines.</summary>
    public static string FirstLine(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var end = text.IndexOfAny(['\r', '\n']);
        return (end < 0 ? text : text[..end]).Trim();
    }

    public static RepoSummary? ToRepo(GhRepo? repo)
    {
        if (repo is null || string.IsNullOrWhiteSpace(repo.FullName) || WebUrl(repo.HtmlUrl) is not { } html)
        {
            return null;
        }

        return new RepoSummary(
            repo.FullName,
            html,
            Math.Max(0, repo.Stars ?? 0),
            Math.Max(0, repo.Forks ?? 0),
            Math.Max(0, repo.OpenIssues ?? 0),
            string.IsNullOrWhiteSpace(repo.Language) ? null : repo.Language,
            repo.PushedAt,
            string.IsNullOrWhiteSpace(repo.DefaultBranch) ? "main" : repo.DefaultBranch,
            FirstLine(repo.Description) is { Length: > 0 } description ? description : null);
    }

    /// <summary>
    /// [S-744] An issue's labels: named ones only, at most <see cref="MaxLabels"/>, the colour kept
    /// only when it is GitHub's six hex digits (anything else draws neutral, never as given).
    /// </summary>
    public static IReadOnlyList<IssueLabel> ToLabels(List<GhLabel>? labels)
        => [.. (labels ?? [])
            .Where(l => l is { Name.Length: > 0 } && !string.IsNullOrWhiteSpace(l.Name))
            .Select(l => new IssueLabel(
                l.Name!.Trim(),
                l.Color is { Length: 6 } color && color.All(char.IsAsciiHexDigit) ? color.ToUpperInvariant() : string.Empty))
            .Take(MaxLabels)];

    /// <summary>[S-610] Where a run stands, from GitHub's two fields.</summary>
    public static RunState ToRunState(string? status, string? conclusion) => status switch
    {
        "in_progress" => RunState.Running,
        "queued" or "waiting" or "requested" or "pending" => RunState.Queued,
        _ => conclusion switch
        {
            "success" => RunState.Success,
            "failure" or "timed_out" or "startup_failure" => RunState.Failure,
            _ when status != "completed" && conclusion is null => RunState.Queued,
            _ => RunState.Other,
        },
    };

    /// <summary>[S-610] The header's word for the newest run.</summary>
    public static string RunLabel(RunState state, Locale locale) => Strings.Get(locale, state switch
    {
        RunState.Success => Str.Github_PipelinePassing,
        RunState.Failure => Str.Github_PipelineFailing,
        RunState.Running => Str.Github_PipelineRunning,
        RunState.Queued => Str.WidgetsA_Github_Run_Queued,
        _ => Str.WidgetsA_Github_Run_None,
    });

    public static IReadOnlyList<RunItem> ToRuns(GhRunList? list, int count)
        => [.. (list?.Runs ?? [])
            .Where(r => r is not null)
            .Select(r => WebUrl(r.HtmlUrl) is { } html
                ? new RunItem(r.Id ?? 0, string.IsNullOrWhiteSpace(r.Name) ? "Workflow" : r.Name.Trim(), ToRunState(r.Status, r.Conclusion), r.HeadBranch ?? string.Empty, r.CreatedAt, html, EventLabel(r.Event), r.Actor?.Login ?? string.Empty)
                : null)
            .OfType<RunItem>()
            .Take(Math.Max(0, count))];

    public static IReadOnlyList<CommitItem> ToCommits(List<GhCommit>? list, int count)
        => [.. (list ?? [])
            .Where(c => c is { Sha.Length: >= 7 })
            .Select(c => WebUrl(c.HtmlUrl) is { } html
                ? new CommitItem(c.Sha![..7], FirstLine(c.Commit?.Message), html, c.Commit?.Author?.Date, CommitAuthor(c))
                : null)
            .OfType<CommitItem>()
            .Take(Math.Max(0, count))];

    /// <summary>[S-610] Merged is its own state; GitHub reports a merged pull request as closed.</summary>
    public static PullState ToPullState(string? state, DateTimeOffset? mergedAt)
        => mergedAt is not null ? PullState.Merged : state == "open" ? PullState.Open : PullState.Closed;

    public static IReadOnlyList<PullItem> ToPulls(List<GhPull>? list)
        => [.. (list ?? [])
            .Where(p => p is { Number: > 0, CreatedAt: not null })
            .Select(p => WebUrl(p.HtmlUrl) is { } html
                ? new PullItem(p.Number!.Value, FirstLine(p.Title), ToPullState(p.State, p.MergedAt), p.Draft == true, html, p.CreatedAt!.Value, p.User?.Login ?? string.Empty)
                : null)
            .OfType<PullItem>()];

    /// <summary>
    /// [S-613] Filter by state and order by when each was opened. Closed means anything not open,
    /// merged included. A new list; the input is never reordered.
    /// </summary>
    public static IReadOnlyList<PullItem> FilterSortPulls(IReadOnlyList<PullItem> pulls, PullFilter filter, PullOrder order, int count)
    {
        ArgumentNullException.ThrowIfNull(pulls);

        var kept = pulls.Where(p => filter switch
        {
            PullFilter.Open => p.State == PullState.Open,
            PullFilter.Closed => p.State != PullState.Open,
            _ => true,
        });

        var sorted = order == PullOrder.Oldest
            ? kept.OrderBy(p => p.CreatedAt).ThenBy(p => p.Number)
            : kept.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Number);

        return [.. sorted.Take(Math.Max(0, count))];
    }

    /// <summary>[S-612] Open issues of a repository, pull requests dropped.</summary>
    public static IReadOnlyList<IssueRow> ToIssues(List<GhIssue>? list, int count)
        => [.. (list ?? [])
            .Where(i => i is { Number: > 0, PullRequest: null })
            .Select(i => WebUrl(i.HtmlUrl) is { } html
                ? new IssueRow(i.Number!.Value, FirstLine(i.Title), html, i.CreatedAt, i.User?.Login ?? string.Empty, Math.Max(0, i.Comments ?? 0), ToLabels(i.Labels))
                : null)
            .OfType<IssueRow>()
            .Take(Math.Max(0, count))];

    public static ReleaseItem? ToRelease(GhRelease? release)
        => release is { TagName.Length: > 0 } && WebUrl(release.HtmlUrl) is { } html
            ? new ReleaseItem(
                release.TagName,
                html,
                FirstLine(release.Name) is { Length: > 0 } name && !string.Equals(name, release.TagName, StringComparison.Ordinal) ? name : string.Empty,
                release.PublishedAt)
            : null;

    /// <summary>[S-744] Who a commit is by: the name in the commit, else the login GitHub matched it to.</summary>
    private static string CommitAuthor(GhCommit commit)
        => FirstLine(commit.Commit?.Author?.Name) is { Length: > 0 } name ? name : commit.Author?.Login ?? string.Empty;

    /// <summary>[S-744] A run's trigger in words: <c>pull_request</c> → "pull request".</summary>
    public static string EventLabel(string? trigger)
        => string.IsNullOrWhiteSpace(trigger) ? string.Empty : trigger.Trim().Replace('_', ' ');

    /// <summary>[S-615][S-616] One account's search results.</summary>
    public static IReadOnlyList<SearchItem> ToSearchItems(string accountId, GhSearch? search)
        => [.. (search?.Items ?? [])
            .Where(i => i is { Number: > 0, UpdatedAt: not null })
            .Select(i => WebUrl(i.HtmlUrl) is { } html
                ? new SearchItem(accountId, i.Number!.Value, FirstLine(i.Title), html, RepoFromRepositoryUrl(i.RepositoryUrl), i.UpdatedAt!.Value, i.Draft == true, i.User?.Login ?? string.Empty, Math.Max(0, i.Comments ?? 0), ToLabels(i.Labels))
                : null)
            .OfType<SearchItem>()];

    /// <summary>[S-617] One account's notifications. A thread without an id or a repository is dropped.</summary>
    public static IReadOnlyList<NotificationItem> ToNotifications(string accountId, List<GhNotification>? list)
        => [.. (list ?? [])
            .Where(n => n is { Id.Length: > 0, UpdatedAt: not null, Repository.FullName.Length: > 0 })
            .Select(n => WebUrl(n.Repository!.HtmlUrl) is { } repoHtml
                ? new NotificationItem(
                    accountId,
                    n.Id!,
                    n.Unread != false,
                    ParseReason(n.Reason),
                    n.UpdatedAt!.Value,
                    FirstLine(n.Subject?.Title) is { Length: > 0 } title ? title : "(untitled)",
                    n.Subject?.Type ?? string.Empty,
                    // [S-619] subject.url carries the number; latest_comment_url never converts.
                    SubjectUrlToHtml(n.Subject?.Url, repoHtml),
                    n.Repository.FullName!,
                    repoHtml)
                : null)
            .OfType<NotificationItem>()];

    /// <summary>
    /// [S-619] The web page for a notification's subject, from its API address:
    /// <c>…/repos/o/r/pulls/12</c> → <c>…/o/r/pull/12</c>, <c>issues</c>, <c>releases</c> and
    /// <c>commits</c> likewise, <c>api.github.com</c> → <c>github.com</c> and an Enterprise
    /// <c>/api/v3</c> prefix dropped. Anything else — a comment's address carries the comment's id,
    /// not the issue's number — falls back to the repository rather than to a page that 404s.
    /// </summary>
    public static string SubjectUrlToHtml(string? subjectUrl, string repoHtmlUrl)
    {
        if (!Uri.TryCreate(subjectUrl, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
        {
            return repoHtmlUrl;
        }

        var host = string.Equals(url.Host, "api.github.com", StringComparison.OrdinalIgnoreCase) ? "github.com" : url.Authority;
        var path = url.AbsolutePath;

        if (path.StartsWith("/api/v3/", StringComparison.Ordinal))
        {
            path = path["/api/v3".Length..];
        }

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // repos / owner / name / kind / id — exactly five segments, so a comments/<id> tail never matches.
        if (parts.Length != 5 || parts[0] != "repos" || !IsOwner(parts[1]) || !IsRepoName(parts[2]) || parts[4].Length == 0)
        {
            return repoHtmlUrl;
        }

        var kind = parts[3] switch
        {
            "pulls" when parts[4].All(char.IsAsciiDigit) => "pull",
            "issues" when parts[4].All(char.IsAsciiDigit) => "issues",
            "releases" or "commits" => parts[3],
            _ => null,
        };

        return kind is null
            ? repoHtmlUrl
            : $"{url.Scheme}://{host}/{parts[1]}/{parts[2]}/{kind}/{Uri.EscapeDataString(Uri.UnescapeDataString(parts[4]))}";
    }

    // ---- notification reasons ------------------------------------------------------------------

    public static NotificationReason ParseReason(string? reason) => reason switch
    {
        "assign" => NotificationReason.Assign,
        "author" => NotificationReason.Author,
        "comment" => NotificationReason.Comment,
        "ci_activity" => NotificationReason.CiActivity,
        "invitation" => NotificationReason.Invitation,
        "manual" => NotificationReason.Manual,
        "mention" => NotificationReason.Mention,
        "review_requested" => NotificationReason.ReviewRequested,
        "security_alert" or "security_advisory_credit" or "security_advisory" => NotificationReason.SecurityAlert,
        "state_change" => NotificationReason.StateChange,
        "subscribed" => NotificationReason.Subscribed,
        "team_mention" => NotificationReason.TeamMention,
        "approval_requested" => NotificationReason.ApprovalRequested,
        _ => NotificationReason.Other,
    };

    /// <summary>[S-618] The short word a notification row leads with.</summary>
    public static string ReasonLabel(NotificationReason reason, Locale locale) => Strings.Get(locale, reason switch
    {
        NotificationReason.Assign => Str.WidgetsA_Github_Reason_Assign,
        NotificationReason.Author => Str.WidgetsA_Github_Reason_Author,
        NotificationReason.Comment => Str.WidgetsA_Github_Reason_Comment,
        NotificationReason.CiActivity => Str.WidgetsA_Github_Reason_Ci,
        NotificationReason.Invitation => Str.WidgetsA_Github_Reason_Invitation,
        NotificationReason.Manual => Str.WidgetsA_Github_Reason_Manual,
        NotificationReason.Mention => Str.WidgetsA_Github_Reason_Mention,
        NotificationReason.ReviewRequested => Str.WidgetsA_Github_Reason_Review,
        NotificationReason.SecurityAlert => Str.WidgetsA_Github_Reason_Security,
        NotificationReason.StateChange => Str.WidgetsA_Github_Reason_State,
        NotificationReason.Subscribed => Str.WidgetsA_Github_Reason_Subscribed,
        NotificationReason.TeamMention => Str.WidgetsA_Github_Reason_Team,
        NotificationReason.ApprovalRequested => Str.WidgetsA_Github_Reason_Approval,
        _ => Str.WidgetsA_Github_Reason_Update,
    });

    /// <summary>
    /// [S-618] Security is critical; a review, a mention, a team mention, an assignment or an
    /// approval request wants your attention; activity on what you wrote is accent; the rest is
    /// neutral.
    /// </summary>
    public static ReasonTone ToneOf(NotificationReason reason) => reason switch
    {
        NotificationReason.SecurityAlert => ReasonTone.Critical,
        NotificationReason.ReviewRequested or NotificationReason.Mention or NotificationReason.TeamMention
            or NotificationReason.Assign or NotificationReason.ApprovalRequested => ReasonTone.Attention,
        NotificationReason.Author => ReasonTone.Accent,
        _ => ReasonTone.Neutral,
    };

    /// <summary>[S-617] What a notification is about, in words: <c>PullRequest</c> → "Pull request".</summary>
    public static string SubjectLabel(string? type, Locale locale) => Strings.Get(locale, type switch
    {
        "PullRequest" => Str.WidgetsA_Github_Subject_PullRequest,
        "Issue" => Str.WidgetsA_Github_Subject_Issue,
        "Release" => Str.WidgetsA_Github_Subject_Release,
        "CheckSuite" or "WorkflowRun" => Str.WidgetsA_Github_Reason_Ci,
        "Discussion" => Str.WidgetsA_Github_Subject_Discussion,
        "Commit" => Str.WidgetsA_Github_Subject_Commit,
        "RepositoryVulnerabilityAlert" or "RepositoryDependabotAlertsThread" => Str.WidgetsA_Github_Subject_SecurityAlert,
        "RepositoryInvitation" => Str.WidgetsA_Github_Subject_Invitation,
        _ => Str.WidgetsA_Github_Subject_Update,
    });

    // ---- merging -------------------------------------------------------------------------------

    /// <summary>
    /// [S-615][S-616][S-622] Several accounts' search results as one list: most recently updated
    /// first, drafts dropped unless asked for, the same issue seen by two accounts listed once, cut
    /// to the count.
    /// </summary>
    public static IReadOnlyList<SearchItem> MergeSearch(IEnumerable<IReadOnlyList<SearchItem>> perAccount, int count, bool includeDrafts = true)
    {
        ArgumentNullException.ThrowIfNull(perAccount);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return [.. perAccount
            .SelectMany(items => items)
            .Where(i => includeDrafts || !i.Draft)
            .OrderByDescending(i => i.UpdatedAt)
            .Where(i => seen.Add(i.HtmlUrl))
            .Take(Math.Max(0, count))];
    }

    /// <summary>[S-617][S-622] Several accounts' notifications as one list, newest first, cut to the count.</summary>
    public static IReadOnlyList<NotificationItem> MergeNotifications(IEnumerable<IReadOnlyList<NotificationItem>> perAccount, int count)
    {
        ArgumentNullException.ThrowIfNull(perAccount);

        var seen = new HashSet<(string, string)>();
        return [.. perAccount
            .SelectMany(items => items)
            .OrderByDescending(n => n.UpdatedAt)
            .Where(n => seen.Add((n.AccountId, n.Id)))
            .Take(Math.Max(0, count))];
    }

    /// <summary>
    /// [S-620] Notifications under their repository: groups ordered by their newest thread, threads
    /// within a group newest first. Repositories are told apart by their web address, not their
    /// name: the same <c>owner/name</c> on github.com and on an Enterprise host are two repositories.
    /// </summary>
    public static IReadOnlyList<NotificationGroup> GroupByRepository(IReadOnlyList<NotificationItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return [.. items
            .GroupBy(i => i.RepoHtmlUrl, StringComparer.OrdinalIgnoreCase)
            .Select(g => new NotificationGroup(
                g.First().Repo,
                g.First().RepoHtmlUrl,
                [.. g.OrderByDescending(i => i.UpdatedAt)]))
            .OrderByDescending(g => g.Items[0].UpdatedAt)];
    }

    // ---- text ----------------------------------------------------------------------------------

    /// <summary>[S-743] A row's detail, "rosa · 4 comments": who opened it and how much it has been talked about; either half alone when the other is missing.</summary>
    public static string ItemDetail(string? author, int comments, Locale locale)
    {
        var talk = comments <= 0 ? string.Empty : Strings.Plural(locale, Str.WidgetsA_Github_Comments, comments);

        var who = author?.Trim() ?? string.Empty;
        return who.Length > 0 && talk.Length > 0 ? $"{who} · {talk}" : who.Length > 0 ? who : talk;
    }

    /// <summary>
    /// [S-629] A count the way GitHub shows one: <c>999</c>, <c>1.2k</c>, <c>12k</c>, <c>3.4M</c>.
    /// One decimal only while it carries information.
    /// </summary>
    public static string Compact(long value)
    {
        if (value < 1_000)
        {
            return Math.Max(0, value).ToString(CultureInfo.InvariantCulture);
        }

        var (scaled, suffix) = value >= 1_000_000 ? (value / 1_000_000d, "M") : (value / 1_000d, "k");
        var text = scaled >= 10
            ? Math.Floor(scaled).ToString("0", CultureInfo.InvariantCulture)
            : (Math.Floor(scaled * 10) / 10).ToString("0.#", CultureInfo.InvariantCulture);
        return text + suffix;
    }

    /// <summary>
    /// [S-629] How long ago, in a list column's terms: <c>now</c>, <c>5m</c>, <c>3h</c>, <c>2d</c>
    /// within the week, then a date (<c>4 Mar</c>). A provider clock ahead of this one is "now".
    /// </summary>
    public static string Ago(DateTimeOffset? when, DateTimeOffset now, Locale locale)
    {
        if (when is not { } at)
        {
            return string.Empty;
        }

        var delta = now - at;

        if (delta < TimeSpan.FromMinutes(1))
        {
            return Strings.Get(locale, Str.WidgetsA_Github_AgoNow);
        }

        if (delta < TimeSpan.FromHours(1))
        {
            return Strings.Format(locale, Str.WidgetsA_Github_AgoMinutes, ("count", (int)delta.TotalMinutes));
        }

        if (delta < TimeSpan.FromDays(1))
        {
            return Strings.Format(locale, Str.WidgetsA_Github_AgoHours, ("count", (int)delta.TotalHours));
        }

        if (delta < TimeSpan.FromDays(7))
        {
            return Strings.Format(locale, Str.WidgetsA_Github_AgoDays, ("count", (int)delta.TotalDays));
        }

        var local = DateOnly.FromDateTime(at.ToOffset(now.Offset).DateTime);
        return local.Year == now.Year
            ? LocaleFormat.Date(locale, local, DateStyle.MonthDayShort)
            : Strings.Format(locale, Str.WidgetsA_Github_MonthYear,
                ("month", LocaleFormat.MonthName(locale, local.Month, NameWidth.Short)), ("year", local.Year.ToString(CultureInfo.InvariantCulture)));
    }
}
