namespace Themia.Core.Accounts;

/// <summary>What came back to the loopback port.</summary>
public enum RedirectKind
{
    /// <summary>Not a sign-in at all — the browser's own <c>/favicon.ico</c>, a stray probe. Keep listening.</summary>
    Unrelated,

    /// <summary>An authorization code for the state this sign-in issued.</summary>
    Code,

    /// <summary>The provider refused (the user pressed Cancel, consent was withheld).</summary>
    ProviderError,

    /// <summary>A sign-in, but not the one this app started: refused.</summary>
    StateMismatch,
}

/// <param name="Kind">What it was.</param>
/// <param name="Code">The authorization code, for <see cref="RedirectKind.Code"/>.</param>
/// <param name="Error">The provider's error and description, for <see cref="RedirectKind.ProviderError"/>.</param>
public readonly record struct OAuthRedirect(RedirectKind Kind, string? Code, string? Error)
{
    /// <summary>
    /// [S-513] Read the redirect from the first line of the request the browser sent to the loopback
    /// port — <c>GET /?code=…&amp;state=… HTTP/1.1</c>.
    /// <para>
    /// The state is checked before anything else is believed, error included: anything on this
    /// machine can connect to the port, and only the browser this app opened knows the state. A
    /// malformed pair (a stray <c>&amp;</c>, a key without a value) is skipped rather than throwing
    /// the whole redirect away.
    /// </para>
    /// </summary>
    public static OAuthRedirect Parse(string? requestLine, string expectedState)
    {
        var target = requestLine?.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: >= 2 } parts
            ? parts[1]
            : null;

        var mark = target?.IndexOf('?', StringComparison.Ordinal) ?? -1;
        if (target is null || mark < 0)
        {
            return new OAuthRedirect(RedirectKind.Unrelated, null, null);
        }

        string? code = null, state = null, error = null, description = null;

        foreach (var pair in target[(mark + 1)..].Split('&'))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                continue;
            }

            string value;
            try
            {
                value = Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));
            }
            catch (UriFormatException)
            {
                continue;
            }

            switch (pair[..equals])
            {
                case "code": code = value; break;
                case "state": state = value; break;
                case "error": error = value; break;
                case "error_description": description = value; break;
            }
        }

        if (code is null && error is null)
        {
            return new OAuthRedirect(RedirectKind.Unrelated, null, null);
        }

        if (!Pkce.StateMatches(expectedState, state))
        {
            return new OAuthRedirect(RedirectKind.StateMismatch, null, null);
        }

        if (error is not null)
        {
            return new OAuthRedirect(RedirectKind.ProviderError, null,
                error == "access_denied" ? "The sign-in was cancelled in the browser." : description ?? error);
        }

        return string.IsNullOrEmpty(code)
            ? new OAuthRedirect(RedirectKind.ProviderError, null, "The browser came back without an authorization code.")
            : new OAuthRedirect(RedirectKind.Code, code, null);
    }
}

/// <summary>How one poll of a device-code token endpoint came out.</summary>
public enum DevicePoll
{
    /// <summary>The user has not approved yet. The normal state for most of the flow.</summary>
    Pending,

    /// <summary>Polled too fast: wait longer from now on.</summary>
    SlowDown,

    /// <summary>The code ran out before the user approved it.</summary>
    Expired,

    /// <summary>The user said no.</summary>
    Denied,

    /// <summary>Anything else the endpoint said; terminal.</summary>
    Failed,

    /// <summary>A token came back.</summary>
    Success,
}

/// <summary>What to do after a poll: wait and poll again, or stop with a message.</summary>
/// <param name="Poll">The classified outcome.</param>
/// <param name="Delay">How long to wait before the next poll; zero when <see cref="Done"/>.</param>
/// <param name="Message">What to tell the user when the flow ends without a token.</param>
public readonly record struct DeviceStep(DevicePoll Poll, TimeSpan Delay, string? Message)
{
    public bool Done => Poll is not (DevicePoll.Pending or DevicePoll.SlowDown);
}

/// <summary>
/// [S-514] The device authorization grant (RFC 8628) as a state machine: no I/O, no clock of its own.
/// <para>
/// Microsoft and GitHub run the same grant, so there is one of these for both — two copies of a
/// polling loop are two places for the interval or the stop rule to drift. The waiting is
/// server-directed: never faster than the interval the server named, five seconds more after every
/// <c>slow_down</c>, and over when the code's own lifetime is.
/// </para>
/// </summary>
public sealed class DeviceCodeSession
{
    /// <summary>RFC 8628 §3.5: a client told to slow down adds five seconds to its interval for good.</summary>
    public static readonly TimeSpan SlowDownStep = TimeSpan.FromSeconds(5);

    /// <summary>RFC 8628 §3.2: what to assume when the server names no interval.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);

    /// <summary>What to assume when the server names no lifetime: GitHub's and Microsoft's own.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(15);

    public DeviceCodeSession(string userCode, Uri verificationUri, long? intervalSeconds, long? expiresInSeconds, DateTimeOffset now)
    {
        UserCode = userCode;
        VerificationUri = verificationUri;
        Interval = intervalSeconds is > 0 and <= 300 ? TimeSpan.FromSeconds(intervalSeconds.Value) : DefaultInterval;
        ExpiresAt = now + (expiresInSeconds is > 0 and <= 3600 ? TimeSpan.FromSeconds(expiresInSeconds.Value) : DefaultLifetime);
    }

    /// <summary>The code the user types on the provider's page.</summary>
    public string UserCode { get; }

    /// <summary>Where they type it.</summary>
    public Uri VerificationUri { get; }

    /// <summary>The current wait between polls; grows on <c>slow_down</c>.</summary>
    public TimeSpan Interval { get; private set; }

    public DateTimeOffset ExpiresAt { get; }

    /// <summary>Classify a token-endpoint answer from its <c>error</c> field and whether it held a token.</summary>
    public static DevicePoll Classify(string? error, bool hasAccessToken)
    {
        if (string.IsNullOrEmpty(error))
        {
            return hasAccessToken ? DevicePoll.Success : DevicePoll.Failed;
        }

        return error switch
        {
            "authorization_pending" => DevicePoll.Pending,
            "slow_down" => DevicePoll.SlowDown,
            "expired_token" or "code_expired" => DevicePoll.Expired,
            "access_denied" or "authorization_declined" => DevicePoll.Denied,
            _ => DevicePoll.Failed,
        };
    }

    /// <summary>The step after a poll that came out as <paramref name="poll"/> at <paramref name="now"/>.</summary>
    public DeviceStep Next(DevicePoll poll, DateTimeOffset now, string? providerMessage = null)
    {
        switch (poll)
        {
            case DevicePoll.Success:
                return new DeviceStep(poll, TimeSpan.Zero, null);
            case DevicePoll.Expired:
                return new DeviceStep(poll, TimeSpan.Zero, "The code expired before it was approved. Start again.");
            case DevicePoll.Denied:
                return new DeviceStep(poll, TimeSpan.Zero, "The sign-in was declined in the browser.");
            case DevicePoll.Failed:
                return new DeviceStep(poll, TimeSpan.Zero, $"Sign-in failed: {providerMessage ?? "the provider gave no reason"}.");
        }

        if (poll == DevicePoll.SlowDown)
        {
            Interval += SlowDownStep;
        }

        // Past the code's lifetime there is nothing to wait for; the endpoint would only say so.
        if (now >= ExpiresAt)
        {
            return new DeviceStep(DevicePoll.Expired, TimeSpan.Zero, "The code expired before it was approved. Start again.");
        }

        return new DeviceStep(poll, Interval, null);
    }
}
