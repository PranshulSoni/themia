using System.Net.Http;
using System.Text.Json.Serialization.Metadata;
using Themia.Core.Accounts;
using Themia.Core.Diagnostics;
using Themia.Core.Localization;

namespace Themia.App.Services.Accounts;

/// <summary>A device-code sign-in in progress: what to show the user, and what to poll with.</summary>
internal sealed class DeviceSignIn
{
    internal DeviceSignIn(AccountProvider provider, DeviceCodeSession session, string deviceCode)
    {
        Provider = provider;
        Session = session;
        DeviceCode = deviceCode;
    }

    public AccountProvider Provider { get; }

    /// <summary>The code to show big, where to type it, and the polling rules.</summary>
    public DeviceCodeSession Session { get; }

    internal string DeviceCode { get; }
}

/// <summary>A Claude sign-in in progress: the page to open, and what proves the pasted code is ours.</summary>
internal sealed record ClaudeSignIn(Uri AuthorizeUrl, string State, string Verifier);

/// <summary>Signing in: one method pair per provider, each verifying before anything is saved.</summary>
internal sealed partial class AccountService
{
    /// <summary>How long a browser sign-in may take before the wait is given up.</summary>
    private static readonly TimeSpan BrowserDeadline = TimeSpan.FromMinutes(5);

    // -----------------------------------------------------------------------------------------
    // Device code: Microsoft and GitHub
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// [S-535] Ask for a device code. The caller shows <see cref="DeviceCodeSession.UserCode"/> and
    /// opens <see cref="DeviceCodeSession.VerificationUri"/>, then awaits
    /// <see cref="CompleteDeviceSignInAsync"/>.
    /// </summary>
    public async Task<DeviceSignIn> BeginDeviceSignInAsync(AccountProvider provider, CancellationToken cancellation = default)
    {
        var request = provider switch
        {
            AccountProvider.Microsoft => RequestTemplate.Form(OAuthClients.Microsoft.DeviceCodeUrl,
            [
                new("client_id", OAuthClients.Microsoft.ClientId),
                new("scope", OAuthClients.Microsoft.Scope),
            ]),
            AccountProvider.GitHub => RequestTemplate.Form(OAuthClients.GitHub.DeviceCodeUrl,
            [
                new("client_id", OAuthClients.GitHub.ClientId),
                new("scope", OAuthClients.GitHub.Scope),
            ]),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "no device-code sign-in"),
        };

        var reply = await AccountHttp.SendAsync(request, CredentialKind.None, null, cancellation).ConfigureAwait(false);
        var code = Parse(reply.Body, AccountWireContext.Default.DeviceCodeReply);

        if (!reply.IsSuccess
            || code is not { DeviceCode.Length: > 0, UserCode.Length: > 0 }
            || !Uri.TryCreate(code.VerificationUri, UriKind.Absolute, out var verify)
            || verify.Scheme != Uri.UriSchemeHttps)
        {
            throw new AccountException(AccountError.BadResponse,
                Str.Shell_Accounts_NoCode.Format(("provider", provider.Label()), ("reason", ErrorBody.Message(reply.Body) ?? $"HTTP {reply.Status}")), reply.Status);
        }

        return new DeviceSignIn(provider, new DeviceCodeSession(code.UserCode, verify, code.Interval, code.ExpiresIn, Now), code.DeviceCode);
    }

    /// <summary>
    /// Poll until the user approves, declines, or the code runs out — at the server's interval,
    /// slowing down when told to ([S-514]) — then save the account. Cancel to give up waiting.
    /// </summary>
    public async Task<AccountInfo> CompleteDeviceSignInAsync(DeviceSignIn signIn, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(signIn);

        var session = signIn.Session;
        var delay = session.Interval;

        while (true)
        {
            await Task.Delay(delay, cancellation).ConfigureAwait(false);

            var poll = signIn.Provider == AccountProvider.Microsoft
                ? RequestTemplate.Form(OAuthClients.Microsoft.TokenUrl,
                [
                    new("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
                    new("client_id", OAuthClients.Microsoft.ClientId),
                    new("device_code", signIn.DeviceCode),
                ])
                : RequestTemplate.Form(OAuthClients.GitHub.TokenUrl,
                [
                    new("client_id", OAuthClients.GitHub.ClientId),
                    new("device_code", signIn.DeviceCode),
                    new("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
                ]);

            var reply = await AccountHttp.SendAsync(poll, CredentialKind.None, null, cancellation).ConfigureAwait(false);
            var token = Parse(reply.Body, AccountWireContext.Default.TokenReply);

            if (token is null)
            {
                throw new AccountException(AccountError.BadResponse, $"{signIn.Provider.Label()} answered the sign-in with something unexpected (HTTP {reply.Status}).", reply.Status);
            }

            var outcome = DeviceCodeSession.Classify(token.Error, !string.IsNullOrEmpty(token.AccessToken));
            var step = session.Next(outcome, Now, token.ErrorDescription ?? token.Error);

            if (step.Poll == DevicePoll.Success)
            {
                return signIn.Provider == AccountProvider.Microsoft
                    ? await SaveMicrosoftAsync(token, cancellation).ConfigureAwait(false)
                    : await SaveGitHubAsync(token.AccessToken!, OAuthClients.GitHub.DefaultApiBase, null, cancellation).ConfigureAwait(false);
            }

            if (step.Done)
            {
                throw new AccountException(
                    step.Poll == DevicePoll.Expired ? AccountError.TimedOut : AccountError.Refused,
                    step.Message ?? Str.Shell_Accounts_NotCompleted.Text());
            }

            delay = step.Delay;
        }
    }

    private async Task<AccountInfo> SaveMicrosoftAsync(TokenReply token, CancellationToken cancellation)
    {
        var access = token.AccessToken!;
        var me = await GetAnonymousJsonAsync(OAuthClients.Microsoft.MeUrl, access, AccountWireContext.Default.GraphMe, "Microsoft", cancellation)
            .ConfigureAwait(false);

        if (me.Id is not { Length: > 0 } id)
        {
            throw new AccountException(AccountError.BadResponse, Str.Shell_Accounts_WhoUnknown.Format(("provider", "Microsoft")));
        }

        var address = me.Mail is { Length: > 0 } mail ? mail : me.UserPrincipalName ?? id;

        return await UpsertAsync(new Account
        {
            Id = AccountIds.Microsoft(id),
            Provider = AccountProvider.Microsoft,
            Address = address,
            DisplayName = me.DisplayName ?? address,
            SealedAccessToken = Dpapi.Seal(access),
            SealedRefreshToken = Dpapi.SealOrNull(token.RefreshToken),
            ExpiresAt = TokenExpiry.FromExpiresIn(token.ExpiresIn, Now),
            AddedAt = Now,
        }, cancellation).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------------------------
    // Google: browser, loopback, PKCE
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// [S-536] Why Google sign-in is unavailable in this build, or null when it is available. The
    /// Desktop client's secret is compiled in from <c>THEMIA_GOOGLE_CLIENT_SECRET</c> and never kept
    /// in source; a build without it refuses before the browser opens rather than at the exchange.
    /// </summary>
    public static string? GoogleUnavailableReason => BuildSecrets.GoogleClientSecret.Length > 0
        ? null
        : Str.Shell_Accounts_GoogleMissing.Text();

    private static string GoogleSecretOrThrow()
        => GoogleUnavailableReason is { } reason
            ? throw new AccountException(AccountError.Refused, reason)
            : BuildSecrets.GoogleClientSecret;

    /// <summary>
    /// Sign in to Google: open the consent page in the user's browser, wait for it to come back to
    /// the loopback port, exchange the code (with the PKCE verifier), and save the account.
    /// </summary>
    /// <param name="openBrowser">Opens a URL in the system browser; returns false when it could not.</param>
    public async Task<AccountInfo> SignInWithGoogleAsync(Func<Uri, bool> openBrowser, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(openBrowser);

        var secret = GoogleSecretOrThrow();

        using var loopback = new Loopback();
        var verifier = Pkce.CreateVerifier();
        var state = Pkce.CreateState();

        if (!openBrowser(OAuthClients.Google.AuthorizeUrl(loopback.RedirectUri, state, Pkce.Challenge(verifier))))
        {
            throw new AccountException(AccountError.Refused, Str.Shell_Accounts_BrowserNotOpened.Text());
        }

        string code;
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
        {
            deadline.CancelAfter(BrowserDeadline);
            try
            {
                code = await loopback.WaitForCodeAsync(state, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                throw new AccountException(AccountError.TimedOut, Str.Shell_Accounts_BrowserTimeout.Text());
            }
        }

        var reply = await AccountHttp.SendAsync(RequestTemplate.Form(OAuthClients.Google.TokenUrl,
        [
            new("grant_type", "authorization_code"),
            new("code", code),
            new("client_id", OAuthClients.Google.ClientId),
            new("client_secret", secret),
            new("code_verifier", verifier),
            new("redirect_uri", loopback.RedirectUri),
        ]), CredentialKind.None, null, cancellation).ConfigureAwait(false);

        var token = Parse(reply.Body, AccountWireContext.Default.TokenReply);
        if (!reply.IsSuccess || token?.AccessToken is not { Length: > 0 } access)
        {
            throw new AccountException(AccountError.Refused,
                Str.Shell_Accounts_ProviderRefusedReason.Format(("provider", "Google"), ("reason", token?.ErrorDescription ?? token?.Error ?? ErrorBody.Message(reply.Body) ?? $"HTTP {reply.Status}")), reply.Status);
        }

        var me = await GetAnonymousJsonAsync(OAuthClients.Google.UserInfoUrl, access, AccountWireContext.Default.GoogleUserInfo, "Google", cancellation)
            .ConfigureAwait(false);

        if (me.Subject is not { Length: > 0 } subject)
        {
            throw new AccountException(AccountError.BadResponse, Str.Shell_Accounts_WhoUnknown.Format(("provider", "Google")));
        }

        var address = me.Email ?? subject;

        return await UpsertAsync(new Account
        {
            Id = AccountIds.Google(subject),
            Provider = AccountProvider.Google,
            Address = address,
            DisplayName = me.Name ?? address,
            SealedAccessToken = Dpapi.Seal(access),
            SealedRefreshToken = Dpapi.SealOrNull(token.RefreshToken),
            ExpiresAt = TokenExpiry.FromExpiresIn(token.ExpiresIn, Now),
            AddedAt = Now,
        }, cancellation).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------------------------
    // Claude: PKCE with a pasted code
    // -----------------------------------------------------------------------------------------

    /// <summary>Start a Claude sign-in: the page to open. Nothing is stored until the code comes back.</summary>
    public static ClaudeSignIn BeginClaudeSignIn()
    {
        var verifier = Pkce.CreateVerifier();
        var state = Pkce.CreateState();
        return new ClaudeSignIn(OAuthClients.Claude.AuthorizeUrl(Pkce.Challenge(verifier), state), state, verifier);
    }

    /// <summary>
    /// [S-537] Finish a Claude sign-in with what the user pasted: check its state, exchange the code,
    /// prove the token can read usage (so a grant without <c>user:profile</c> never lands), and save.
    /// </summary>
    public async Task<AccountInfo> CompleteClaudeSignInAsync(ClaudeSignIn signIn, string pasted, string? nickname, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(signIn);

        var paste = PastedCode.Parse(pasted);
        if (paste.Error is { } error)
        {
            throw new AccountException(AccountError.Refused, error);
        }

        if (paste.State is not null && !Pkce.StateMatches(signIn.State, paste.State))
        {
            throw new AccountException(AccountError.Refused, Str.Shell_Accounts_CodeMismatch.Text());
        }

        var reply = await AccountHttp.SendAsync(RequestTemplate.Form(OAuthClients.Claude.TokenUrl,
        [
            new("grant_type", "authorization_code"),
            new("code", paste.Code),
            new("redirect_uri", OAuthClients.Claude.RedirectUri),
            new("client_id", OAuthClients.Claude.ClientId),
            new("code_verifier", signIn.Verifier),
            new("state", signIn.State),
        ]), CredentialKind.None, null, cancellation).ConfigureAwait(false);

        var token = Parse(reply.Body, AccountWireContext.Default.TokenReply);
        if (!reply.IsSuccess || token?.AccessToken is not { Length: > 0 } access)
        {
            throw new AccountException(AccountError.Refused,
                Str.Shell_Accounts_ProviderRefusedReason.Format(("provider", "Claude"), ("reason", ErrorBody.Message(reply.Body) ?? token?.Error ?? $"HTTP {reply.Status}")), reply.Status);
        }

        // Validate before saving: the fresh token must be able to read usage.
        var usage = new RequestTemplate(HttpMethod.Get, OAuthClients.Claude.UsageUrl);
        var check = await AccountHttp.SendAsync(usage, CredentialKind.Bearer, r =>
        {
            r.Headers.Authorization = new("Bearer", access);
            r.Headers.UserAgent.Clear();
            r.Headers.TryAddWithoutValidation("User-Agent", OAuthClients.Claude.UserAgent);
            r.Headers.TryAddWithoutValidation(OAuthClients.Claude.BetaHeader, OAuthClients.Claude.BetaValue);
        }, cancellation).ConfigureAwait(false);

        if (!check.IsSuccess)
        {
            var detail = ErrorBody.Message(check.Body);
            throw new AccountException(AccountError.Refused, check.Status switch
            {
                401 => Str.Shell_Accounts_ClaudeTokenRefused.Text(),
                403 => detail is null ? Str.Shell_Accounts_ClaudeScope.Text() : Str.Shell_Accounts_ClaudeScopeReason.Format(("reason", detail)),
                _ => detail is null
                    ? Str.Shell_Net_Answered.Format(("host", "Anthropic"), ("status", $"HTTP {check.Status}"))
                    : Str.Shell_Net_AnsweredReason.Format(("host", "Anthropic"), ("status", $"HTTP {check.Status}"), ("reason", detail)),
            }, check.Status);
        }

        var name = AccountRules.CleanNickname(nickname);

        return await UpsertAsync(new Account
        {
            Id = AccountIds.Claude,
            Provider = AccountProvider.Claude,
            Address = AccountRules.ClaudeAddress,
            DisplayName = name ?? AccountRules.ClaudeAddress,
            Nickname = name,
            SealedAccessToken = Dpapi.Seal(access),
            SealedRefreshToken = Dpapi.SealOrNull(token.RefreshToken),
            ExpiresAt = TokenExpiry.FromExpiresIn(token.ExpiresIn, Now),
            AddedAt = Now,
        }, cancellation).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------------------------
    // GitHub token
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Add GitHub with a personal access token — the only way in for GitHub Enterprise Server, which
    /// cannot sign in against a github.com OAuth app. <paramref name="apiBase"/> is the Enterprise
    /// REST root (<c>https://ghe.example/api/v3</c>), or empty for github.com.
    /// </summary>
    public Task<AccountInfo> AddGitHubTokenAsync(string token, string? apiBase, string? nickname, CancellationToken cancellation = default)
    {
        var trimmed = token?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new AccountException(AccountError.Refused, Str.Shell_Accounts_PasteToken.Text());
        }

        var api = OAuthClients.GitHub.NormaliseApiBase(apiBase)
            ?? throw new AccountException(AccountError.Refused, "The API address must be an https address, such as https://github.example.com/api/v3.");

        return SaveGitHubAsync(trimmed, api, nickname, cancellation);
    }

    /// <summary>
    /// Verify a GitHub token against <c>/user</c> and save it. Both ways in — device flow and token —
    /// end here, so the check, the id and the name fallbacks exist once.
    /// </summary>
    private async Task<AccountInfo> SaveGitHubAsync(string token, string apiBase, string? nickname, CancellationToken cancellation)
    {
        var api = new Uri(apiBase);
        var request = new RequestTemplate(HttpMethod.Get, new Uri(apiBase + "/user"));
        request.Headers.Add(("Accept", "application/vnd.github+json"));

        var reply = await AccountHttp.SendAsync(request, CredentialKind.Bearer, r => r.Headers.Authorization = new("Bearer", token), cancellation)
            .ConfigureAwait(false);

        if (!reply.IsSuccess)
        {
            throw new AccountException(reply.Status == 401 ? AccountError.SignInRequired : AccountException.FromStatus(reply.Status),
                Str.Shell_Accounts_GitHubTokenFailed.Format(
                    ("answer", ErrorBody.Message(reply.Body) is { } said
                        ? Str.Shell_Net_AnsweredReason.Format(("host", "GitHub"), ("status", $"HTTP {reply.Status}"), ("reason", said))
                        : Str.Shell_Net_Answered.Format(("host", "GitHub"), ("status", $"HTTP {reply.Status}")))),
                reply.Status);
        }

        var user = Parse(reply.Body, AccountWireContext.Default.GitHubUser);
        if (user?.Login is not { Length: > 0 } login)
        {
            throw new AccountException(AccountError.BadResponse, Str.Shell_Accounts_WhoUnknown.Format(("provider", "GitHub")));
        }

        return await UpsertAsync(new Account
        {
            Id = AccountIds.GitHub(login, api),
            Provider = AccountProvider.GitHub,
            Address = user.Email ?? $"{login}@users.noreply.github.com",
            DisplayName = user.Name ?? login,
            Nickname = AccountRules.CleanNickname(nickname),
            Username = login,
            ApiBaseUrl = apiBase,
            SealedAccessToken = Dpapi.Seal(token),
            AddedAt = Now,
        }, cancellation).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------------------------
    // IMAP, CalDAV, CardDAV: verified before saving
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Add a mail server after proving the login ([S-528]). <paramref name="address"/> may be empty:
    /// the user name is used when it is an address, else <c>user@host</c>, as the classic app did.
    /// </summary>
    public async Task<AccountInfo> AddImapAsync(
        string? address,
        string host,
        int port,
        string username,
        string password,
        string? nickname,
        CancellationToken cancellation = default)
    {
        var cleanHost = host?.Trim() ?? string.Empty;
        var cleanUser = username?.Trim() ?? string.Empty;

        if (cleanHost.Length == 0 || cleanHost.Contains("://", StringComparison.Ordinal) || cleanHost.Any(char.IsWhiteSpace)
            || Uri.CheckHostName(cleanHost) == UriHostNameType.Unknown)
        {
            throw new AccountException(AccountError.Refused, Str.Shell_Accounts_HostName.Text());
        }

        if (port is < 1 or > 65535)
        {
            throw new AccountException(AccountError.Refused, Str.Shell_Accounts_PortRange.Text());
        }

        if (cleanUser.Length == 0 || string.IsNullOrEmpty(password))
        {
            throw new AccountException(AccountError.Refused, Str.Shell_Accounts_EnterCredentials.Text());
        }

        await ImapLogin.VerifyAsync(new ImapCredentials(cleanHost, port, cleanUser, password), cancellation).ConfigureAwait(false);

        var email = address?.Trim() is { Length: > 0 } typed
            ? typed
            : cleanUser.Contains('@', StringComparison.Ordinal) ? cleanUser : $"{cleanUser}@{cleanHost}";

        return await UpsertAsync(new Account
        {
            Id = AccountIds.Imap(cleanUser, cleanHost),
            Provider = AccountProvider.Imap,
            Address = email,
            DisplayName = email,
            Nickname = AccountRules.CleanNickname(nickname),
            Host = cleanHost,
            Port = port,
            Username = cleanUser,
            SealedPassword = Dpapi.Seal(password),
            AddedAt = Now,
        }, cancellation).ConfigureAwait(false);
    }

    /// <summary>
    /// Add a CalDAV or CardDAV account: resolve what was typed to a collection, proving the
    /// credentials on the way ([S-517]), and save the collection's address.
    /// </summary>
    public async Task<AccountInfo> AddDavAsync(
        AccountProvider provider,
        string url,
        string username,
        string password,
        string? nickname,
        CancellationToken cancellation = default)
    {
        if (provider is not (AccountProvider.CalDav or AccountProvider.CardDav))
        {
            throw new ArgumentOutOfRangeException(nameof(provider), provider, "not a DAV provider");
        }

        var entered = DavUrls.FromTyped(url)
            ?? throw new AccountException(AccountError.Refused, "Enter the server's web address, such as https://cloud.example.com.");

        var user = username?.Trim() ?? string.Empty;
        if (user.Length == 0 || string.IsNullOrEmpty(password))
        {
            throw new AccountException(AccountError.Refused, Str.Shell_Accounts_EnterCredentials.Text());
        }

        var basic = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{user}:{password}"));
        var kind = provider == AccountProvider.CalDav ? DavKind.Calendar : DavKind.Contacts;

        var found = await DavDiscovery.DiscoverAsync(entered, kind, async (target, body, depth, token) =>
        {
            var reply = await AccountHttp.SendAsync(DavRequest("PROPFIND", target, body, depth), CredentialKind.Basic,
                r => r.Headers.Authorization = new("Basic", basic), token).ConfigureAwait(false);
            return new DavReply(reply.Status, reply.Body, reply.FinalUrl);
        }, cancellation).ConfigureAwait(false);

        if (found is not { Outcome: DavDiscoveryOutcome.Found, Collection: { } collection })
        {
            throw new AccountException(found.Outcome switch
            {
                DavDiscoveryOutcome.AuthFailed => AccountError.SignInRequired,
                DavDiscoveryOutcome.NothingFound => AccountError.NotFound,
                _ => AccountError.BadResponse,
            }, found.Message, found.Status);
        }

        return await UpsertAsync(new Account
        {
            Id = AccountIds.Dav(provider, user, collection),
            Provider = provider,
            Address = user,
            DisplayName = user,
            Nickname = AccountRules.CleanNickname(nickname),
            Username = user,
            ServerUrl = collection.AbsoluteUri,
            SealedPassword = Dpapi.Seal(password),
            AddedAt = Now,
        }, cancellation).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------------------------

    /// <summary>A GET with a token that is not (yet) an account's: the "who signed in" call.</summary>
    private static async Task<T> GetAnonymousJsonAsync<T>(Uri url, string token, JsonTypeInfo<T> contract, string provider, CancellationToken cancellation)
        where T : class
    {
        var request = new RequestTemplate(HttpMethod.Get, url);
        request.Headers.Add(("Accept", "application/json"));

        var reply = await AccountHttp.SendAsync(request, CredentialKind.Bearer, r => r.Headers.Authorization = new("Bearer", token), cancellation)
            .ConfigureAwait(false);

        return reply.IsSuccess && Parse(reply.Body, contract) is { } value
            ? value
            : throw new AccountException(AccountError.BadResponse,
                Str.Shell_Accounts_WhoUnknownReason.Format(("provider", provider), ("reason", ErrorBody.Message(reply.Body) ?? $"HTTP {reply.Status}")), reply.Status);
    }
}
