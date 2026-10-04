using System.Globalization;

namespace Themia.Core.Developer;

/// <summary>
/// [S-625] Asking GitHub politely: when a widget's request may go out again, and what makes it a
/// conditional one.
/// <para>
/// GitHub publishes the rules and they are cheap to follow. A request carrying the last answer's
/// <c>ETag</c> (<c>If-None-Match</c>) or <c>Last-Modified</c> (<c>If-Modified-Since</c>) is
/// answered <c>304 Not Modified</c> when nothing changed, and a 304 does not count against the
/// account's budget. The notifications endpoint also names its own minimum cadence in
/// <c>X-Poll-Interval</c>. A desktop that lives for weeks with four widgets on one account is
/// exactly the client those rules are for.
/// </para>
/// </summary>
public static class GitHubPolling
{
    /// <summary>A poll interval longer than this is not believed: the widget's own cadence is at most an hour.</summary>
    public static readonly TimeSpan MaxPollInterval = TimeSpan.FromHours(1);

    /// <summary><c>X-Poll-Interval</c>, in seconds; null when absent or not a positive number.</summary>
    public static TimeSpan? PollInterval(string? header)
    {
        if (!int.TryParse(header?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
        {
            return null;
        }

        var interval = TimeSpan.FromSeconds(seconds);
        return interval > MaxPollInterval ? MaxPollInterval : interval;
    }

    /// <summary>
    /// When an answer fetched at <paramref name="fetchedAt"/> may be asked for again: after the
    /// widget's own cadence or the server's poll interval, whichever is longer, and never before a
    /// rate-limit wait is over.
    /// </summary>
    public static DateTimeOffset NextPoll(DateTimeOffset fetchedAt, TimeSpan refresh, TimeSpan? pollInterval, DateTimeOffset? blockedUntil)
    {
        var wait = pollInterval is { } poll && poll > refresh ? poll : refresh;
        var next = fetchedAt + wait;
        return blockedUntil is { } blocked && blocked > next ? blocked : next;
    }

    /// <summary>
    /// The validators to send with a repeat request: <c>If-None-Match</c> when the last answer had
    /// an entity tag, <c>If-Modified-Since</c> when it had a modification date. Both may go; GitHub
    /// answers 304 only when they agree nothing changed.
    /// </summary>
    public static IReadOnlyList<(string Name, string Value)> Conditional(string? etag, string? lastModified)
    {
        var headers = new List<(string, string)>(2);

        if (!string.IsNullOrWhiteSpace(etag) && etag.All(c => c >= 0x20 && c < 0x7F))
        {
            headers.Add(("If-None-Match", etag.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(lastModified) && lastModified.All(c => c >= 0x20 && c < 0x7F))
        {
            headers.Add(("If-Modified-Since", lastModified.Trim()));
        }

        return headers;
    }
}
