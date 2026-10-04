namespace Themia.Core.Accounts;

/// <summary>
/// [S-500] Every kind of account Themia can hold. A closed set: a provider this build does not know
/// is dropped on load and reported, never carried as a string that every caller has to re-check.
/// </summary>
public enum AccountProvider
{
    /// <summary>A Microsoft account, personal or work: Graph, signed in by device code.</summary>
    Microsoft,

    /// <summary>A Google account: Gmail, Calendar, Tasks and People, signed in through the browser.</summary>
    Google,

    /// <summary>A mail server with a user name and password.</summary>
    Imap,

    /// <summary>A CalDAV calendar collection with a user name and password.</summary>
    CalDav,

    /// <summary>A CardDAV address book with a user name and password.</summary>
    CardDav,

    /// <summary>A GitHub (or GitHub Enterprise) token.</summary>
    GitHub,

    /// <summary>Claude subscription usage, through Themia's own OAuth grant.</summary>
    Claude,
}

/// <summary>What an account can feed. A widget asks for one of these, never for a provider name.</summary>
[Flags]
public enum AccountFeatures
{
    None = 0,
    Mail = 1,
    Calendar = 2,
    Contacts = 4,
    Tasks = 8,
    Files = 16,
    GitHub = 32,
    ClaudeUsage = 64,
}

/// <summary>How an account proves itself on every request.</summary>
public enum AccountAuth
{
    /// <summary>A short-lived bearer token renewed from a refresh token (Microsoft, Google, Claude).</summary>
    OAuthRefresh,

    /// <summary>A long-lived bearer token with nothing to renew it (GitHub).</summary>
    BearerToken,

    /// <summary>A user name and password sent with each connection (IMAP, CalDAV, CardDAV).</summary>
    Password,
}

/// <summary>The facts about each provider, in one table so no caller grows its own switch.</summary>
public static class AccountProviders
{
    /// <summary>Every provider, in the order the Add account menu lists them.</summary>
    public static IReadOnlyList<AccountProvider> All { get; } =
    [
        AccountProvider.Microsoft,
        AccountProvider.Google,
        AccountProvider.Imap,
        AccountProvider.CalDav,
        AccountProvider.CardDav,
        AccountProvider.GitHub,
        AccountProvider.Claude,
    ];

    /// <summary>
    /// [S-501] The name a provider is written under in <c>accounts.json</c>. Lower case.
    /// </summary>
    public static string WireName(this AccountProvider provider) => provider switch
    {
        AccountProvider.Microsoft => "microsoft",
        AccountProvider.Google => "google",
        AccountProvider.Imap => "imap",
        AccountProvider.CalDav => "caldav",
        AccountProvider.CardDav => "carddav",
        AccountProvider.GitHub => "github",
        AccountProvider.Claude => "claude",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "unknown provider"),
    };

    /// <summary>The provider written as <paramref name="wire"/>, or null for one this build does not know.</summary>
    public static AccountProvider? TryParse(string? wire)
    {
        foreach (var provider in All)
        {
            if (string.Equals(provider.WireName(), wire?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return provider;
            }
        }

        return null;
    }

    /// <summary>What the settings page calls the provider.</summary>
    public static string Label(this AccountProvider provider) => provider switch
    {
        AccountProvider.Microsoft => "Microsoft",
        AccountProvider.Google => "Google",
        AccountProvider.Imap => "Mail (IMAP)",
        AccountProvider.CalDav => "Calendar (CalDAV)",
        AccountProvider.CardDav => "Contacts (CardDAV)",
        AccountProvider.GitHub => "GitHub",
        AccountProvider.Claude => "Claude",
        _ => provider.ToString(),
    };

    /// <summary>[S-502] What the provider feeds. Follows from the kind, never from a list of switches.</summary>
    public static AccountFeatures Features(this AccountProvider provider) => provider switch
    {
        AccountProvider.Microsoft => AccountFeatures.Mail | AccountFeatures.Calendar | AccountFeatures.Contacts
            | AccountFeatures.Tasks | AccountFeatures.Files,
        AccountProvider.Google => AccountFeatures.Mail | AccountFeatures.Calendar | AccountFeatures.Contacts
            | AccountFeatures.Tasks,
        AccountProvider.Imap => AccountFeatures.Mail,
        AccountProvider.CalDav => AccountFeatures.Calendar,
        AccountProvider.CardDav => AccountFeatures.Contacts,
        AccountProvider.GitHub => AccountFeatures.GitHub,
        AccountProvider.Claude => AccountFeatures.ClaudeUsage,
        _ => AccountFeatures.None,
    };

    public static AccountAuth Auth(this AccountProvider provider) => provider switch
    {
        AccountProvider.Microsoft or AccountProvider.Google or AccountProvider.Claude => AccountAuth.OAuthRefresh,
        AccountProvider.GitHub => AccountAuth.BearerToken,
        _ => AccountAuth.Password,
    };
}
