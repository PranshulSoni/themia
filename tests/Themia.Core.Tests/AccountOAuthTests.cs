using Themia.Core.Accounts;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The sign-in flows' pure parts: PKCE, the loopback redirect, the device-code polling machine, the
/// Claude paste, and the URLs each provider is sent to.
/// </summary>
public class AccountOAuthTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void S508_challenge_matches_the_rfc7636_vector()
        => Assert.Equal(
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

    [Fact]
    public void S508_verifier_is_unreserved_and_long_enough_and_challenge_unpadded()
    {
        var verifier = Pkce.CreateVerifier();

        Assert.InRange(verifier.Length, 43, 128);
        Assert.All(verifier, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~'));
        Assert.NotEqual(verifier, Pkce.CreateVerifier());

        var challenge = Pkce.Challenge("x");
        Assert.DoesNotContain('=', challenge);
        Assert.DoesNotContain('+', challenge);
        Assert.DoesNotContain('/', challenge);
    }

    [Fact]
    public void S508_state_comparison()
    {
        var state = Pkce.CreateState();

        Assert.True(Pkce.StateMatches(state, state));
        Assert.False(Pkce.StateMatches(state, state + "x"));
        Assert.False(Pkce.StateMatches(state, null));
        Assert.False(Pkce.StateMatches(string.Empty, string.Empty));
    }

    [Fact]
    public void S513_a_redirect_with_the_right_state_yields_the_code()
    {
        var redirect = OAuthRedirect.Parse("GET /?code=4%2F0Abc-d&state=xyz&scope=email HTTP/1.1", "xyz");

        Assert.Equal(RedirectKind.Code, redirect.Kind);
        Assert.Equal("4/0Abc-d", redirect.Code);
    }

    [Fact]
    public void S513_malformed_pairs_are_skipped_not_fatal()
    {
        var redirect = OAuthRedirect.Parse("GET /?&foo&bar=1&=x&code=abc&state=xyz HTTP/1.1", "xyz");

        Assert.Equal(RedirectKind.Code, redirect.Kind);
        Assert.Equal("abc", redirect.Code);
    }

    [Fact]
    public void S513_a_wrong_or_missing_state_is_refused_even_for_an_error()
    {
        Assert.Equal(RedirectKind.StateMismatch, OAuthRedirect.Parse("GET /?code=abc&state=other HTTP/1.1", "xyz").Kind);
        Assert.Equal(RedirectKind.StateMismatch, OAuthRedirect.Parse("GET /?code=abc HTTP/1.1", "xyz").Kind);
        Assert.Equal(RedirectKind.StateMismatch, OAuthRedirect.Parse("GET /?error=access_denied&state=other HTTP/1.1", "xyz").Kind);
    }

    [Fact]
    public void S513_a_provider_refusal_is_reported()
    {
        var redirect = OAuthRedirect.Parse("GET /?error=access_denied&state=xyz HTTP/1.1", "xyz");

        Assert.Equal(RedirectKind.ProviderError, redirect.Kind);
        Assert.Contains("cancelled", redirect.Error, StringComparison.Ordinal);

        var other = OAuthRedirect.Parse("GET /?error=invalid_scope&error_description=Bad+scope&state=xyz HTTP/1.1", "xyz");
        Assert.Equal("Bad scope", other.Error);
    }

    [Theory]
    [InlineData("GET /favicon.ico HTTP/1.1")]
    [InlineData("GET / HTTP/1.1")]
    [InlineData("GET /?unrelated=1 HTTP/1.1")]
    [InlineData("")]
    [InlineData(null)]
    public void S513_a_request_that_is_not_a_redirect_keeps_the_listener_waiting(string? line)
        => Assert.Equal(RedirectKind.Unrelated, OAuthRedirect.Parse(line, "xyz").Kind);

    [Theory]
    [InlineData("authorization_pending", false, DevicePoll.Pending)]
    [InlineData("slow_down", false, DevicePoll.SlowDown)]
    [InlineData("expired_token", false, DevicePoll.Expired)]
    [InlineData("access_denied", false, DevicePoll.Denied)]
    [InlineData("authorization_declined", false, DevicePoll.Denied)]
    [InlineData("bad_verification_code", false, DevicePoll.Failed)]
    [InlineData(null, true, DevicePoll.Success)]
    [InlineData(null, false, DevicePoll.Failed)]
    public void S514_token_endpoint_answers_are_classified(string? error, bool hasToken, DevicePoll expected)
        => Assert.Equal(expected, DeviceCodeSession.Classify(error, hasToken));

    [Fact]
    public void S514_pending_waits_the_server_interval_and_slow_down_adds_five_seconds_for_good()
    {
        var session = new DeviceCodeSession("ABCD-1234", new Uri("https://github.com/login/device"), 5, 900, Now);

        var pending = session.Next(DevicePoll.Pending, Now);
        Assert.False(pending.Done);
        Assert.Equal(TimeSpan.FromSeconds(5), pending.Delay);

        var slow = session.Next(DevicePoll.SlowDown, Now);
        Assert.Equal(TimeSpan.FromSeconds(10), slow.Delay);

        // The longer interval sticks: RFC 8628 says "for all subsequent requests".
        Assert.Equal(TimeSpan.FromSeconds(10), session.Next(DevicePoll.Pending, Now).Delay);
    }

    [Fact]
    public void S514_polling_stops_when_the_code_has_expired()
    {
        var session = new DeviceCodeSession("A", new Uri("https://microsoft.com/devicelogin"), 5, 900, Now);

        var step = session.Next(DevicePoll.Pending, Now.AddSeconds(900));

        Assert.True(step.Done);
        Assert.Equal(DevicePoll.Expired, step.Poll);
    }

    [Theory]
    [InlineData(DevicePoll.Success)]
    [InlineData(DevicePoll.Expired)]
    [InlineData(DevicePoll.Denied)]
    [InlineData(DevicePoll.Failed)]
    public void S514_terminal_outcomes_end_the_flow(DevicePoll poll)
    {
        var session = new DeviceCodeSession("A", new Uri("https://x.example"), 5, 900, Now);
        var step = session.Next(poll, Now, "bad_verification_code");

        Assert.True(step.Done);
        Assert.Equal(TimeSpan.Zero, step.Delay);
        Assert.Equal(poll == DevicePoll.Success, step.Message is null);
    }

    [Theory]
    [InlineData(null, 5)]
    [InlineData(0L, 5)]
    [InlineData(100_000L, 5)]
    [InlineData(8L, 8)]
    public void S514_a_missing_or_absurd_interval_falls_back_to_five_seconds(long? interval, int expected)
        => Assert.Equal(TimeSpan.FromSeconds(expected), new DeviceCodeSession("A", new Uri("https://x.example"), interval, null, Now).Interval);

    [Fact]
    public void S509_microsoft_scopes_are_the_classic_ones()
    {
        foreach (var scope in new[] { "User.Read", "Mail.ReadWrite", "Calendars.Read", "Contacts.Read", "Tasks.ReadWrite", "Files.Read", "offline_access" })
        {
            Assert.Contains(scope, OAuthClients.Microsoft.Scope.Split(' '));
        }

        Assert.Equal("6cf22c0f-103a-4f8a-ae35-178d62575897", OAuthClients.Microsoft.ClientId);
        Assert.Contains("/common/", OAuthClients.Microsoft.DeviceCodeUrl.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void S509_google_authorize_url_carries_pkce_offline_access_and_the_loopback()
    {
        var url = OAuthClients.Google.AuthorizeUrl("http://127.0.0.1:51234", "STATE", "CHAL").AbsoluteUri;

        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", url, StringComparison.Ordinal);
        Assert.Contains("client_id=187748979097-url067h00d18pjbjgskddssdj0nrq04s.apps.googleusercontent.com", url, StringComparison.Ordinal);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A51234", url, StringComparison.Ordinal);
        Assert.Contains("code_challenge=CHAL", url, StringComparison.Ordinal);
        Assert.Contains("code_challenge_method=S256", url, StringComparison.Ordinal);
        Assert.Contains("access_type=offline", url, StringComparison.Ordinal);
        Assert.Contains("prompt=consent", url, StringComparison.Ordinal);
        Assert.Contains("state=STATE", url, StringComparison.Ordinal);
        Assert.Contains("gmail.readonly", url, StringComparison.Ordinal);
    }

    [Fact]
    public void S511_claude_authorize_url_requests_user_profile_with_pkce()
    {
        var url = OAuthClients.Claude.AuthorizeUrl("CHAL", "STATE").AbsoluteUri;

        Assert.StartsWith("https://claude.ai/oauth/authorize?code=true&", url, StringComparison.Ordinal);
        Assert.Contains("client_id=9d1c250a-e61b-44d9-88ed-5944d1962f5e", url, StringComparison.Ordinal);
        Assert.Contains("code_challenge=CHAL", url, StringComparison.Ordinal);
        Assert.Contains("code_challenge_method=S256", url, StringComparison.Ordinal);
        Assert.Contains("state=STATE", url, StringComparison.Ordinal);
        Assert.Contains("user%3Aprofile", url, StringComparison.Ordinal);
        Assert.Contains("platform.claude.com%2Foauth%2Fcode%2Fcallback", url, StringComparison.Ordinal);
    }

    [Fact]
    public void S510_github_scopes_match_the_token_path()
    {
        foreach (var scope in new[] { "repo", "read:user", "notifications" })
        {
            Assert.Contains(scope, OAuthClients.GitHub.Scope.Split(' '));
        }
    }

    [Theory]
    [InlineData(null, "https://api.github.com")]
    [InlineData("  ", "https://api.github.com")]
    [InlineData("https://ghe.corp/api/v3/", "https://ghe.corp/api/v3")]
    [InlineData("http://ghe.corp/api/v3", null)]
    [InlineData("ghe.corp", null)]
    [InlineData("https://ghe.corp/api?x=1", null)]
    public void S510_the_api_root_is_https_or_refused(string? typed, string? expected)
        => Assert.Equal(expected, OAuthClients.GitHub.NormaliseApiBase(typed));

    [Fact]
    public void S512_paste_splits_code_and_state()
    {
        var pasted = PastedCode.Parse("  abc123#xyz789  ");

        Assert.Null(pasted.Error);
        Assert.Equal("abc123", pasted.Code);
        Assert.Equal("xyz789", pasted.State);
    }

    [Fact]
    public void S512_paste_without_a_hash_is_the_code_alone()
    {
        var pasted = PastedCode.Parse("justcode");

        Assert.Equal("justcode", pasted.Code);
        Assert.Null(pasted.State);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("#state-only")]
    public void S512_an_empty_or_incomplete_paste_says_why(string? text)
        => Assert.NotNull(PastedCode.Parse(text).Error);
}
