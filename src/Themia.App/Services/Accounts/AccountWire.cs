using System.Text.Json.Serialization;

namespace Themia.App.Services.Accounts;

/// <summary>
/// A token endpoint's answer to a refresh or a GitHub device poll, success or error. GitHub answers
/// errors with a 200 and this same shape, so the error fields are read whatever the status was. An
/// authorization-code exchange is read by Core's <c>CodeExchange</c> instead.
/// </summary>
internal sealed class TokenReply
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expires_in")]
    public long? ExpiresIn { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

/// <summary>The start of GitHub's device authorization grant (RFC 8628 §3.2).</summary>
internal sealed class DeviceCodeReply
{
    [JsonPropertyName("device_code")]
    public string? DeviceCode { get; set; }

    [JsonPropertyName("user_code")]
    public string? UserCode { get; set; }

    [JsonPropertyName("verification_uri")]
    public string? VerificationUri { get; set; }

    [JsonPropertyName("expires_in")]
    public long? ExpiresIn { get; set; }

    [JsonPropertyName("interval")]
    public long? Interval { get; set; }
}

/// <summary>Graph <c>/me</c>, just the fields that name the account.</summary>
internal sealed class GraphMe
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("mail")]
    public string? Mail { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("userPrincipalName")]
    public string? UserPrincipalName { get; set; }
}

/// <summary>Google OpenID Connect userinfo.</summary>
internal sealed class GoogleUserInfo
{
    [JsonPropertyName("sub")]
    public string? Subject { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>GitHub <c>/user</c>.</summary>
internal sealed class GitHubUser
{
    [JsonPropertyName("login")]
    public string? Login { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

/// <summary>Source-generated JSON for the sign-in endpoints. Native AOT has no reflection serializer.</summary>
[JsonSourceGenerationOptions(NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(TokenReply))]
[JsonSerializable(typeof(DeviceCodeReply))]
[JsonSerializable(typeof(GraphMe))]
[JsonSerializable(typeof(GoogleUserInfo))]
[JsonSerializable(typeof(GitHubUser))]
internal sealed partial class AccountWireContext : JsonSerializerContext;
