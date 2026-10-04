using Themia.Core.Accounts;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>Reading the classic app's account file, and the IMAP login handshake's pure parts.</summary>
public class AccountImportAndImapTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The classic file's exact shape, with made-up values.</summary>
    private const string Classic = """
        {
          "6c069afa-0000-0000-0000-000000000000": {
            "provider": "microsoft", "access_token": "dpapi:v1:AQID", "refresh_token": "dpapi:v1:BAUG",
            "expires_at": 1790000000, "email": "ann@contoso.com", "display_name": "Ann", "nickname": null,
            "host": null, "port": null, "username": null, "password": null,
            "caldav_url": null, "carddav_url": null, "api_base_url": null
          },
          "github:octo@api.github.com": {
            "provider": "github", "access_token": "dpapi:v1:BwgJ", "refresh_token": null, "expires_at": 0,
            "email": "octo@users.noreply.github.com", "display_name": "Octo", "nickname": "Work",
            "host": null, "port": null, "username": "octo", "password": null,
            "caldav_url": null, "carddav_url": null, "api_base_url": "https://api.github.com"
          },
          "rss:https://blog.example/feed": {
            "provider": "rss", "access_token": "", "refresh_token": null, "expires_at": 0,
            "email": "https://blog.example/feed", "display_name": "Blog", "nickname": null,
            "host": null, "port": null, "username": null, "password": null,
            "caldav_url": null, "carddav_url": null, "api_base_url": null
          },
          "carddav:al@dav.example": {
            "provider": "carddav", "access_token": "", "refresh_token": null, "expires_at": 0,
            "email": "al", "display_name": "Al", "nickname": null,
            "host": null, "port": null, "username": "al", "password": "plain-legacy-password",
            "caldav_url": null, "carddav_url": "https://dav.example/addressbooks/al/contacts/", "api_base_url": null
          },
          "imap:broken@": {
            "provider": "imap", "access_token": "", "email": "broken@x", "display_name": "", "username": "broken"
          },
          "legacy-ms": { "access_token": "dpapi:v1:AQID", "email": "old@outlook.com", "display_name": "Old" }
        }
        """;

    [Fact]
    public void S523_classic_accounts_are_read_with_their_ids_and_secrets_untouched()
    {
        var import = ClassicAccounts.Parse(Classic, Now);

        var microsoft = Assert.Single(import.Accounts, a => a.Account.Id.StartsWith("6c069afa", StringComparison.Ordinal));
        Assert.Equal(AccountProvider.Microsoft, microsoft.Account.Provider);
        Assert.Equal("ann@contoso.com", microsoft.Account.Address);
        Assert.Equal("dpapi:v1:BAUG", microsoft.RefreshToken);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_790_000_000), microsoft.Account.ExpiresAt);
        Assert.Null(microsoft.Account.SealedRefreshToken);

        var github = Assert.Single(import.Accounts, a => a.Account.Provider == AccountProvider.GitHub);
        Assert.Equal("Work", github.Account.Nickname);
        Assert.Equal("https://api.github.com", github.Account.ApiBaseUrl);
        Assert.Null(github.Account.ExpiresAt);

        var dav = Assert.Single(import.Accounts, a => a.Account.Provider == AccountProvider.CardDav);
        Assert.Equal("https://dav.example/addressbooks/al/contacts/", dav.Account.ServerUrl);
        Assert.Equal("plain-legacy-password", dav.Password);
    }

    [Fact]
    public void S523_a_classic_entry_without_a_provider_is_microsoft()
        => Assert.Equal(AccountProvider.Microsoft, Assert.Single(ClassicAccounts.Parse(Classic, Now).Accounts, a => a.Account.Id == "legacy-ms").Account.Provider);

    [Fact]
    public void S523_feeds_and_broken_entries_are_listed_as_skipped()
    {
        var import = ClassicAccounts.Parse(Classic, Now);

        Assert.Equal(4, import.Accounts.Count);
        Assert.Equal(2, import.Skipped.Count);
        Assert.Contains(import.Skipped, s => s.Contains("RSS", StringComparison.Ordinal));
        Assert.Contains(import.Skipped, s => s.Contains("imap:broken@", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public void S523_an_unreadable_classic_file_imports_nothing_and_says_why(string json)
    {
        var import = ClassicAccounts.Parse(json, Now);

        Assert.Empty(import.Accounts);
        Assert.Single(import.Skipped);
    }

    [Theory]
    [InlineData("* OK [CAPABILITY IMAP4rev1] Dovecot ready.", ImapGreeting.Ok)]
    [InlineData("* PREAUTH ready", ImapGreeting.PreAuth)]
    [InlineData("* BYE too many connections", ImapGreeting.Bye)]
    [InlineData("220 smtp.example ESMTP", ImapGreeting.NotImap)]
    [InlineData(null, ImapGreeting.NotImap)]
    public void S521_greetings_are_recognised(string? line, ImapGreeting expected)
        => Assert.Equal(expected, ImapProtocol.ParseGreeting(line));

    [Fact]
    public void S521_the_tagged_answer_is_told_from_untagged_lines()
    {
        Assert.Null(ImapProtocol.ParseTagged("* CAPABILITY IMAP4rev1", "a1"));
        Assert.Null(ImapProtocol.ParseTagged("a10 OK done", "a1"));
        Assert.Equal(new ImapTagged(ImapStatus.Ok, "[CAPABILITY X] Logged in"), ImapProtocol.ParseTagged("a1 OK [CAPABILITY X] Logged in", "a1"));
        Assert.Equal(new ImapTagged(ImapStatus.No, "[AUTHENTICATIONFAILED] Invalid credentials"), ImapProtocol.ParseTagged("a1 NO [AUTHENTICATIONFAILED] Invalid credentials", "a1"));
        Assert.Equal(ImapStatus.Bad, ImapProtocol.ParseTagged("a1 bad syntax", "a1")?.Status);
    }

    [Theory]
    [InlineData("bob", "\"bob\"")]
    [InlineData("p\"w\\d", "\"p\\\"w\\\\d\"")]
    [InlineData("with space", "\"with space\"")]
    public void S521_values_are_quoted_so_nothing_ends_the_command_early(string value, string expected)
        => Assert.Equal(expected, ImapProtocol.Quote(value));

    [Theory]
    [InlineData("pässword")]
    [InlineData("line\r\nbreak")]
    [InlineData("nul\0")]
    public void S521_values_a_quoted_string_cannot_carry_need_a_literal(string value)
        => Assert.Null(ImapProtocol.Quote(value));

    [Fact]
    public void S521_continuation_and_literal_prefix()
    {
        Assert.True(ImapProtocol.IsContinuation("+ go ahead"));
        Assert.False(ImapProtocol.IsContinuation("* OK"));
        Assert.Equal("{9}", ImapProtocol.LiteralPrefix(9));
    }

    [Theory]
    [InlineData("me@gmail.com", "imap.gmail.com")]
    [InlineData("me@EU.ICLOUD.COM", "imap.mail.me.com")]
    [InlineData("me@notgmail.com", null)]
    [InlineData("me@outlook.com", null)]
    [InlineData("no-at-sign", null)]
    [InlineData("trailing@", null)]
    public void S522_imap_presets_match_the_domain_or_a_subdomain_only(string address, string? host)
        => Assert.Equal(host, ImapPresets.ForAddress(address)?.Host);

    [Fact]
    public void S522_dav_presets_follow_the_kind()
    {
        Assert.Equal("https://caldav.icloud.com", DavPresets.ForAddress("me@icloud.com", DavKind.Calendar));
        Assert.Equal("https://contacts.icloud.com", DavPresets.ForAddress("me@icloud.com", DavKind.Contacts));
        Assert.Null(DavPresets.ForAddress("me@example.org", DavKind.Calendar));
    }
}
