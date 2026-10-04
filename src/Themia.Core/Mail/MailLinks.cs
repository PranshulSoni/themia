using Themia.Core.Accounts;

namespace Themia.Core.Mail;

/// <summary>
/// [S-547] What opening or replying to a message hands to the shell.
/// <para>
/// A Microsoft message deep-links into Outlook (<c>ms-outlook://emails/{id}</c>) when Outlook is
/// installed — it only understands Graph ids, so no other provider's id is ever sent there — and
/// otherwise opens Graph's own web link. A Gmail message opens its thread on the web. An IMAP
/// message has no web view: open is a logged no-op, never a shell call with an empty string (which
/// launches Explorer — the classic app's issue #45a). Reply falls back to <c>mailto:</c>.
/// </para>
/// <para>
/// Only three shapes ever leave this class — an https URL from the provider, an
/// <c>ms-outlook://emails/…</c> link with a percent-encoded id, and a <c>mailto:</c> with a checked
/// address — and <see cref="IsLaunchable"/> is the gate the launcher applies to them.
/// </para>
/// </summary>
public static class MailLinks
{
    public const string OutlookScheme = "ms-outlook";

    /// <summary>The link that opens a message, or null when there is nothing to open.</summary>
    public static string? Open(MailMessage message, bool outlookInstalled)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Provider == AccountProvider.Microsoft && outlookInstalled)
        {
            return $"{OutlookScheme}://emails/{MailWire.PercentEncode(message.RawId)}";
        }

        return IsHttps(message.WebLink) ? message.WebLink.Trim() : null;
    }

    /// <summary>The link that starts a reply, or null when the sender has no usable address.</summary>
    public static string? Reply(MailMessage message, bool outlookInstalled)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Provider == AccountProvider.Microsoft && outlookInstalled)
        {
            return $"{OutlookScheme}://emails/{MailWire.PercentEncode(message.RawId)}/reply";
        }

        if (!IsAddress(message.FromAddress))
        {
            return null;
        }

        var subject = message.Subject.StartsWith("re:", StringComparison.OrdinalIgnoreCase)
            ? message.Subject
            : "Re: " + message.Subject;

        return $"mailto:{message.FromAddress}?subject={MailWire.PercentEncode(subject)}";
    }

    /// <summary>
    /// Whether a link is one of the three shapes this class makes. Re-checked on the literal string
    /// at the moment of launch, so nothing built elsewhere can ride through.
    /// </summary>
    public static bool IsLaunchable(string? link)
    {
        if (string.IsNullOrWhiteSpace(link) || link.Any(char.IsControl))
        {
            return false;
        }

        if (IsHttps(link))
        {
            return true;
        }

        if (link.StartsWith(OutlookScheme + "://emails/", StringComparison.Ordinal))
        {
            // The id was percent-encoded: nothing but unreserved characters and escapes may follow.
            var rest = link[(OutlookScheme.Length + "://emails/".Length)..];
            if (rest.EndsWith("/reply", StringComparison.Ordinal))
            {
                rest = rest[..^"/reply".Length];
            }

            return rest.Length > 0 && rest.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~' or '%');
        }

        if (link.StartsWith("mailto:", StringComparison.Ordinal))
        {
            var rest = link["mailto:".Length..];
            var query = rest.IndexOf('?', StringComparison.Ordinal);
            var address = query < 0 ? rest : rest[..query];
            return IsAddress(address)
                && (query < 0 || rest[(query + 1)..].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~' or '%' or '='));
        }

        return false;
    }

    /// <summary>A plausible single address: one '@', something either side, no spaces or URL syntax.</summary>
    public static bool IsAddress(string? address)
    {
        if (string.IsNullOrEmpty(address) || address.Length > 320)
        {
            return false;
        }

        var at = address.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && at == address.LastIndexOf('@') && at < address.Length - 1
            && !address.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '?' or '&' or '#' or '/' or '\\' or '<' or '>' or '"' or ',' or ';' or ':');
    }

    private static bool IsHttps(string? link)
        => Uri.TryCreate(link?.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.Length > 0
            && string.IsNullOrEmpty(uri.UserInfo);
}
