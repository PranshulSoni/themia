using System.Globalization;

namespace Themia.Core.Accounts;

/// <summary>
/// The OAuth clients Themia signs in with, and the addresses of their endpoints.
/// <para>
/// Every value here is public by design. A desktop app cannot keep a secret, which is why each flow
/// is one that does not need one: the device code grant (Microsoft, GitHub) and PKCE (Google, Claude).
/// The one exception, Google's "Desktop" client secret, is not here — Google insists on it even with
/// PKCE, but it stays out of the public source and is compiled in from the build environment
/// (see <c>BuildSecrets</c> in the app).
/// </para>
/// </summary>
public static class OAuthClients
{
    /// <summary>Microsoft identity platform, v2 endpoints, multi-tenant.</summary>
    public static class Microsoft
    {
        public const string ClientId = "6cf22c0f-103a-4f8a-ae35-178d62575897";

        public const string Tenant = "common";

        /// <summary>[S-509] The classic app's scopes, unchanged, so an imported refresh token still covers them.</summary>
        public const string Scope = "User.Read Mail.ReadWrite Calendars.Read Contacts.Read Tasks.ReadWrite Files.Read offline_access";

        public static readonly Uri DeviceCodeUrl = new($"https://login.microsoftonline.com/{Tenant}/oauth2/v2.0/devicecode");

        public static readonly Uri TokenUrl = new($"https://login.microsoftonline.com/{Tenant}/oauth2/v2.0/token");

        /// <summary>Who signed in: the Graph user id is the account id.</summary>
        public static readonly Uri MeUrl = new("https://graph.microsoft.com/v1.0/me?$select=id,mail,displayName,userPrincipalName");

        public static readonly Uri GraphRoot = new("https://graph.microsoft.com/");
    }

    /// <summary>Google's installed-app client: browser, loopback redirect, PKCE.</summary>
    public static class Google
    {
        public const string ClientId = "187748979097-url067h00d18pjbjgskddssdj0nrq04s.apps.googleusercontent.com";

        public const string Scope = "openid email profile "
            + "https://www.googleapis.com/auth/gmail.readonly "
            + "https://www.googleapis.com/auth/calendar.events.readonly "
            + "https://www.googleapis.com/auth/tasks "
            + "https://www.googleapis.com/auth/contacts.readonly";

        public static readonly Uri TokenUrl = new("https://oauth2.googleapis.com/token");

        public static readonly Uri UserInfoUrl = new("https://www.googleapis.com/oauth2/v3/userinfo");

        /// <summary>
        /// The consent page. <c>access_type=offline</c> and <c>prompt=consent</c> together are what make
        /// Google hand out a refresh token every time, not only on the very first consent.
        /// </summary>
        public static Uri AuthorizeUrl(string redirectUri, string state, string challenge) => Build(
            "https://accounts.google.com/o/oauth2/v2/auth",
            ("response_type", "code"),
            ("client_id", ClientId),
            ("redirect_uri", redirectUri),
            ("scope", Scope),
            ("state", state),
            ("code_challenge", challenge),
            ("code_challenge_method", "S256"),
            ("access_type", "offline"),
            ("prompt", "consent"));
    }

    /// <summary>The "Themia" GitHub OAuth app, device flow only.</summary>
    public static class GitHub
    {
        public const string ClientId = "Ov23liel8RYOXTaNaVI6";

        /// <summary>The same scopes the personal-token form asks for, so both ways in grant the same access.</summary>
        public const string Scope = "repo read:user notifications";

        public const string DefaultApiBase = "https://api.github.com";

        public static readonly Uri DeviceCodeUrl = new("https://github.com/login/device/code");

        public static readonly Uri TokenUrl = new("https://github.com/login/oauth/access_token");

        /// <summary>Where a personal access token with the right scopes is created.</summary>
        public static readonly Uri NewTokenPage = new("https://github.com/settings/tokens/new?scopes=repo,read:user,notifications&description=Themia");

        /// <summary>
        /// [S-510] The REST root for a token: github.com when nothing was typed, otherwise the
        /// Enterprise URL as typed, without a trailing slash. Null when it is not an https URL — a
        /// token is never sent in the clear, and never to a URL the user did not type.
        /// </summary>
        public static string? NormaliseApiBase(string? typed)
        {
            var trimmed = typed?.Trim().TrimEnd('/');

            if (string.IsNullOrEmpty(trimmed))
            {
                return DefaultApiBase;
            }

            return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && uri.Host.Length > 0
                && string.IsNullOrEmpty(uri.Query)
                && string.IsNullOrEmpty(uri.Fragment)
                    ? trimmed
                    : null;
        }
    }

    /// <summary>
    /// [S-511] Claude subscription usage. Themia runs its own authorization-code grant with PKCE
    /// against the client the Claude CLI uses, because the usage endpoint demands the
    /// <c>user:profile</c> scope a <c>setup-token</c> token lacks. There is no loopback redirect
    /// registered for that client, so the hosted callback shows a <c>code#state</c> string the user
    /// pastes back. The endpoints are undocumented and may change without notice.
    /// </summary>
    public static class Claude
    {
        public const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

        public const string RedirectUri = "https://platform.claude.com/oauth/code/callback";

        public const string Scope = "user:profile user:inference";

        /// <summary>
        /// The usage endpoint routes any other user agent into a bucket that answers 429 nearly
        /// always, so requests mirror the CLI's.
        /// </summary>
        public const string UserAgent = "claude-code/2.1.139";

        public const string BetaHeader = "anthropic-beta";

        public const string BetaValue = "oauth-2025-04-20";

        public static readonly Uri TokenUrl = new("https://platform.claude.com/v1/oauth/token");

        public static readonly Uri UsageUrl = new("https://api.anthropic.com/api/oauth/usage");

        /// <summary>The consent page; <c>code=true</c> selects the copy-and-paste callback.</summary>
        public static Uri AuthorizeUrl(string challenge, string state) => Build(
            "https://claude.ai/oauth/authorize",
            ("code", "true"),
            ("response_type", "code"),
            ("client_id", ClientId),
            ("redirect_uri", RedirectUri),
            ("scope", Scope),
            ("code_challenge", challenge),
            ("code_challenge_method", "S256"),
            ("state", state));
    }

    private static Uri Build(string root, params (string Key, string Value)[] query)
        => new(root + "?" + string.Join('&', query.Select(pair =>
            string.Create(CultureInfo.InvariantCulture, $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"))));
}

/// <summary>What the user pasted back from Claude's callback page.</summary>
/// <param name="Code">The authorization code.</param>
/// <param name="State">The state the page echoed, when it showed one.</param>
/// <param name="Error">Why the paste is unusable; null when it is.</param>
public readonly record struct PastedCode(string Code, string? State, string? Error)
{
    /// <summary>
    /// [S-512] Split the <c>code#state</c> value. The state half is optional — some callback variants
    /// show the code alone — but when it is there it must be the one this sign-in issued, which the
    /// caller checks.
    /// </summary>
    public static PastedCode Parse(string? pasted)
    {
        var text = pasted?.Trim() ?? string.Empty;

        if (text.Length == 0)
        {
            return new PastedCode(string.Empty, null, "Paste the code shown after you sign in.");
        }

        var hash = text.IndexOf('#', StringComparison.Ordinal);
        if (hash < 0)
        {
            return new PastedCode(text, null, null);
        }

        var code = text[..hash].Trim();
        var state = text[(hash + 1)..].Trim();

        return code.Length == 0
            ? new PastedCode(string.Empty, null, "That code looks incomplete. Copy the whole value the page shows.")
            : new PastedCode(code, state.Length == 0 ? null : state, null);
    }
}
