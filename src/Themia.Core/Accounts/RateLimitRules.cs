using System.Globalization;

namespace Themia.Core.Accounts;

/// <summary>What a response said about the account's request budget.</summary>
/// <param name="Limit">Requests allowed per window (<c>X-RateLimit-Limit</c>), when sent.</param>
/// <param name="Remaining">Requests left in this window (<c>X-RateLimit-Remaining</c>), when sent.</param>
/// <param name="ResetAt">When the window starts over (<c>X-RateLimit-Reset</c>, epoch seconds), when sent.</param>
/// <param name="RetryAfter">The server's own "ask again in" (<c>Retry-After</c>), when sent.</param>
public sealed record RateLimitState(int? Limit, int? Remaining, DateTimeOffset? ResetAt, TimeSpan? RetryAfter)
{
    public static RateLimitState None { get; } = new(null, null, null, null);

    /// <summary>The window is used up: asking before <see cref="ResetAt"/> only earns another refusal.</summary>
    public bool Exhausted => Remaining is 0;
}

/// <summary>
/// [S-625] Reading a provider's rate-limit headers, so a throttle is waited out rather than retried.
/// <para>
/// GitHub answers a spent budget with <b>403</b> (or 429) and <c>X-RateLimit-Remaining: 0</c>, and a
/// secondary limit with a 403 carrying <c>Retry-After</c>. Read as a plain 403 that is "forbidden" —
/// a permission problem, retried on the next tick, which is exactly how a budget stays spent. Read
/// here, it is a rate limit with a known end, and nothing asks again before it.
/// </para>
/// </summary>
public static class RateLimitRules
{
    /// <summary>
    /// The longest a reset is believed. GitHub's windows are an hour; a reset further out than this
    /// is a clock problem or a hostile header, and must not silence a widget for days.
    /// </summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromHours(2);

    /// <summary>
    /// What to wait when a throttle names no end. GitHub's own guidance for a secondary limit
    /// without <c>Retry-After</c> is at least a minute.
    /// </summary>
    public static readonly TimeSpan DefaultWait = TimeSpan.FromMinutes(1);

    /// <summary>The budget headers of one response. Unreadable values are absent, never zero.</summary>
    public static RateLimitState Read(IReadOnlyDictionary<string, string>? headers, DateTimeOffset now)
    {
        if (headers is null || headers.Count == 0)
        {
            return RateLimitState.None;
        }

        var limit = Int(headers, "X-RateLimit-Limit");
        var remaining = Int(headers, "X-RateLimit-Remaining");

        DateTimeOffset? reset = null;
        if (headers.TryGetValue("X-RateLimit-Reset", out var resetText)
            && long.TryParse(resetText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var epoch)
            && epoch is > 0 and < 32_503_680_000)
        {
            reset = DateTimeOffset.FromUnixTimeSeconds(epoch);
        }

        var retryAfter = headers.TryGetValue("Retry-After", out var retryText)
            ? RetryRules.ParseRetryAfter(retryText, now)
            : null;

        return new RateLimitState(limit, remaining, reset, retryAfter);
    }

    /// <summary>
    /// Whether a refusal is a throttle rather than a "no": any 429, and a 403 that says the budget
    /// is spent or names a time to come back.
    /// </summary>
    public static bool IsThrottle(int status, RateLimitState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return status == 429 || (status == 403 && (state.Exhausted || state.RetryAfter is not null));
    }

    /// <summary>
    /// When asking again is worth it, or null when nothing says to wait. A throttle waits for
    /// <c>Retry-After</c>, else the reset, else a minute; a success that spent the last request
    /// waits for the reset too — the next request is already known to fail. Never past
    /// <see cref="MaxWait"/>.
    /// </summary>
    public static DateTimeOffset? WaitUntil(int status, RateLimitState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);

        DateTimeOffset? until = null;

        if (IsThrottle(status, state))
        {
            until = state.RetryAfter is { } after
                ? now + after
                : state.ResetAt is { } reset && reset > now ? reset : now + DefaultWait;
        }
        else if (status is >= 200 and < 400 && state.Exhausted && state.ResetAt is { } reset && reset > now)
        {
            until = reset;
        }

        return until is { } at && at - now > MaxWait ? now + MaxWait : until;
    }

    private static int? Int(IReadOnlyDictionary<string, string> headers, string name)
        => headers.TryGetValue(name, out var text)
            && int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
}
