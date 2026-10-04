using System.Security.Cryptography;
using System.Text;

namespace Themia.Core.Accounts;

/// <summary>How a stored secret string reads.</summary>
public enum EnvelopeKind
{
    /// <summary>Nothing stored.</summary>
    Absent,

    /// <summary>A tagged DPAPI blob; <see cref="SecretEnvelope.TryUnwrap"/> gives its bytes.</summary>
    Sealed,

    /// <summary>
    /// Untagged text. This app never writes one, so in its own file this is a value that must be
    /// refused: a hand edit or damage, never a format to accept.
    /// </summary>
    Plaintext,

    /// <summary>Tagged, but the payload is not base64: damaged.</summary>
    Damaged,
}

/// <summary>
/// [S-506] The text form of a sealed secret: <c>dpapi:v1:</c> followed by the base64 of the DPAPI blob.
/// <para>
/// DPAPI with the CurrentUser scope and no extra entropy, so a blob sealed for this Windows user opens
/// in this process and nowhere else.
/// </para>
/// </summary>
public static class SecretEnvelope
{
    public const string Tag = "dpapi:v1:";

    public static string Wrap(ReadOnlySpan<byte> blob) => Tag + Convert.ToBase64String(blob);

    public static EnvelopeKind TryUnwrap(string? stored, out byte[] blob)
    {
        blob = [];

        if (string.IsNullOrEmpty(stored))
        {
            return EnvelopeKind.Absent;
        }

        if (!stored.StartsWith(Tag, StringComparison.Ordinal))
        {
            return EnvelopeKind.Plaintext;
        }

        try
        {
            blob = Convert.FromBase64String(stored[Tag.Length..]);
            return blob.Length == 0 ? EnvelopeKind.Damaged : EnvelopeKind.Sealed;
        }
        catch (FormatException)
        {
            return EnvelopeKind.Damaged;
        }
    }
}

/// <summary>[S-507] When an access token is worth using and when a fresh one has to be asked for.</summary>
public static class TokenExpiry
{
    /// <summary>
    /// A token this close to expiring is treated as expired. Five minutes, as in the classic app: a
    /// request started with a token that dies in flight fails with a 401 that a refresh a moment
    /// earlier would have avoided.
    /// </summary>
    public static readonly TimeSpan Skew = TimeSpan.FromMinutes(5);

    /// <summary>What a token endpoint that does not say <c>expires_in</c> is assumed to have meant.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(1);

    /// <summary>A ceiling on what a token endpoint may claim, so a garbage value cannot pin a dead token.</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(30);

    /// <summary>Whether a token that expires at <paramref name="expiresAt"/> can still be sent.</summary>
    public static bool IsUsable(DateTimeOffset? expiresAt, DateTimeOffset now)
        => expiresAt is { } at && at - Skew > now;

    /// <summary>The expiry for a token endpoint's <c>expires_in</c>, in seconds.</summary>
    public static DateTimeOffset FromExpiresIn(long? expiresInSeconds, DateTimeOffset now)
    {
        var lifetime = expiresInSeconds is > 0
            ? TimeSpan.FromSeconds(Math.Min(expiresInSeconds.Value, (long)MaxLifetime.TotalSeconds))
            : DefaultLifetime;

        return now + lifetime;
    }
}

/// <summary>[S-530] When a refresh is due, and when its failure is the user's to mend.</summary>
public static class RefreshRules
{
    /// <summary>
    /// Whether the caller that just took the account's refresh gate must actually refresh.
    /// <para>
    /// Without a rejected token: only when the stored one is stale. With one (a server just answered
    /// 401 to it): only when the stored token is still that same one — if it differs, another caller
    /// refreshed while this one waited at the gate, and that token is used instead. That is what
    /// makes ten widgets seeing the same 401 cost one refresh, not ten.
    /// </para>
    /// </summary>
    public static bool ShouldRefresh(DateTimeOffset? expiresAt, string? storedToken, string? rejectedToken, DateTimeOffset now)
    {
        if (storedToken is null)
        {
            return true;
        }

        return rejectedToken is null
            ? !TokenExpiry.IsUsable(expiresAt, now)
            : string.Equals(storedToken, rejectedToken, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a failed refresh means the grant is gone (revoked, expired, password changed) — only
    /// the user can mend that — rather than a passing fault that the next attempt may not have. A
    /// 401 without an OAuth error code counts (Anthropic answers that way); a 400 needs the code.
    /// </summary>
    public static bool NeedsUser(int status, string? oauthError)
        => (status == 401 && oauthError is null)
            || (status is 400 or 401
                && oauthError is "invalid_grant" or "interaction_required" or "consent_required" or "login_required"
                    or "unauthorized_client" or "invalid_client");
}

/// <summary>[S-508] PKCE (RFC 7636) and the OAuth <c>state</c> value.</summary>
public static class Pkce
{
    /// <summary>
    /// A fresh verifier: 32 random bytes as base64url, 43 characters — inside RFC 7636's 43..128 and
    /// drawn only from its unreserved set.
    /// </summary>
    public static string CreateVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>The S256 challenge for a verifier: base64url of its SHA-256, without padding.</summary>
    public static string Challenge(string verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        return Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    /// <summary>An unguessable <c>state</c>, so a redirect that this app did not start is refused.</summary>
    public static string CreateState() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    /// <summary>Compare two state values without leaking how many leading characters matched.</summary>
    public static bool StateMatches(string? expected, string? actual)
    {
        if (string.IsNullOrEmpty(expected) || actual is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
    }

    public static string Base64Url(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
