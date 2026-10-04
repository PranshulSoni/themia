using Themia.Core.Accounts;
using Themia.Core.Developer;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>[S-625] Asking GitHub politely: conditional requests, the poll interval, and rate limits.</summary>
public sealed class GitHubPollingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static Dictionary<string, string> Headers(params (string Name, string Value)[] pairs)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in pairs)
        {
            headers[name] = value;
        }

        return headers;
    }

    [Theory]
    [InlineData("60", 60)]
    [InlineData(" 120 ", 120)]
    [InlineData("99999", 3600)]
    public void S625_poll_interval_is_read_and_capped(string header, int seconds)
        => Assert.Equal(TimeSpan.FromSeconds(seconds), GitHubPolling.PollInterval(header));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("soon")]
    public void S625_an_unusable_poll_interval_is_absent(string? header)
        => Assert.Null(GitHubPolling.PollInterval(header));

    [Fact]
    public void S625_next_poll_honours_the_longer_of_cadence_and_poll_interval()
    {
        var five = TimeSpan.FromMinutes(5);

        Assert.Equal(Now + five, GitHubPolling.NextPoll(Now, five, TimeSpan.FromSeconds(60), null));
        Assert.Equal(Now + TimeSpan.FromMinutes(10), GitHubPolling.NextPoll(Now, five, TimeSpan.FromMinutes(10), null));
    }

    [Fact]
    public void S625_nothing_asks_before_a_rate_limit_is_over()
    {
        var blocked = Now.AddMinutes(40);
        Assert.Equal(blocked, GitHubPolling.NextPoll(Now, TimeSpan.FromMinutes(5), null, blocked));
        // A block that ends before the cadence does not shorten it.
        Assert.Equal(Now.AddMinutes(5), GitHubPolling.NextPoll(Now, TimeSpan.FromMinutes(5), null, Now.AddMinutes(1)));
    }

    [Fact]
    public void S625_conditional_headers_carry_the_last_validators()
    {
        Assert.Empty(GitHubPolling.Conditional(null, null));
        Assert.Equal([("If-None-Match", "W/\"abc\"")], GitHubPolling.Conditional("W/\"abc\"", null));
        Assert.Equal(
            [("If-None-Match", "\"e\""), ("If-Modified-Since", "Sat, 04 Oct 2026 11:00:00 GMT")],
            GitHubPolling.Conditional("\"e\"", "Sat, 04 Oct 2026 11:00:00 GMT"));
        // A header value with a line break in it is never sent back.
        Assert.Empty(GitHubPolling.Conditional("a\r\nX-Evil: 1", null));
    }

    // ---- rate limits ---------------------------------------------------------------------------

    [Fact]
    public void S625_reads_github_budget_headers()
    {
        var reset = Now.AddMinutes(30);
        var state = RateLimitRules.Read(
            Headers(("x-ratelimit-limit", "5000"), ("X-RateLimit-Remaining", "0"), ("X-RateLimit-Reset", reset.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture))),
            Now);

        Assert.Equal(5000, state.Limit);
        Assert.True(state.Exhausted);
        Assert.Equal(reset, state.ResetAt);
        Assert.Null(state.RetryAfter);
    }

    [Fact]
    public void S625_a_403_with_a_spent_budget_is_a_throttle_not_a_refusal()
    {
        var spent = new RateLimitState(5000, 0, Now.AddMinutes(30), null);
        var fine = new RateLimitState(5000, 4000, Now.AddMinutes(30), null);

        Assert.True(RateLimitRules.IsThrottle(403, spent));
        Assert.False(RateLimitRules.IsThrottle(403, fine));
        Assert.True(RateLimitRules.IsThrottle(403, fine with { RetryAfter = TimeSpan.FromSeconds(60) })); // secondary limit
        Assert.True(RateLimitRules.IsThrottle(429, RateLimitState.None));
        Assert.False(RateLimitRules.IsThrottle(404, spent));
    }

    [Fact]
    public void S625_waits_for_retry_after_then_the_reset_then_a_minute()
    {
        var reset = Now.AddMinutes(30);

        Assert.Equal(Now.AddSeconds(90), RateLimitRules.WaitUntil(403, new RateLimitState(null, 0, reset, TimeSpan.FromSeconds(90)), Now));
        Assert.Equal(reset, RateLimitRules.WaitUntil(403, new RateLimitState(null, 0, reset, null), Now));
        Assert.Equal(Now + RateLimitRules.DefaultWait, RateLimitRules.WaitUntil(429, RateLimitState.None, Now));
    }

    [Fact]
    public void S625_a_success_that_spent_the_last_request_waits_for_the_reset()
    {
        var reset = Now.AddMinutes(12);
        Assert.Equal(reset, RateLimitRules.WaitUntil(200, new RateLimitState(60, 0, reset, null), Now));
        Assert.Null(RateLimitRules.WaitUntil(200, new RateLimitState(60, 3, reset, null), Now));
        Assert.Null(RateLimitRules.WaitUntil(304, RateLimitState.None, Now));
    }

    [Fact]
    public void S625_a_reset_days_away_is_not_believed()
    {
        var wait = RateLimitRules.WaitUntil(403, new RateLimitState(null, 0, Now.AddDays(3), null), Now);
        Assert.Equal(Now + RateLimitRules.MaxWait, wait);
    }

    [Fact]
    public void S625_junk_headers_read_as_absent_not_zero()
    {
        var state = RateLimitRules.Read(Headers(("X-RateLimit-Remaining", "lots"), ("X-RateLimit-Reset", "-1")), Now);

        Assert.Null(state.Remaining);
        Assert.False(state.Exhausted);
        Assert.Null(state.ResetAt);
        Assert.Same(RateLimitState.None, RateLimitRules.Read(null, Now));
    }
}
