using System.Text.Json;

namespace Themia.Core.Accounts;

/// <summary>
/// One account read from the classic app, with its secrets still exactly as that app stored them —
/// normally a <c>dpapi:v1:</c> envelope, in its oldest builds plain text. The app opens and re-seals
/// them; nothing here can.
/// </summary>
/// <param name="Account">The account, validated, with no secrets on it.</param>
public sealed record ClassicAccount(Account Account, string? AccessToken, string? RefreshToken, string? Password);

/// <summary>What could be read from the classic file, and what was left behind and why.</summary>
public sealed record ClassicImport(IReadOnlyList<ClassicAccount> Accounts, IReadOnlyList<string> Skipped);

/// <summary>
/// [S-523] Reading the Tauri app's <c>email_accounts.json</c>: one JSON object mapping each account id
/// to its fields in snake_case, secrets as <c>dpapi:v1:</c> envelopes.
/// <para>
/// Read-only by construction: this only ever parses text it is handed, and the file is never written,
/// moved or deleted — the classic app may still be installed and using it. Entries that are not
/// accounts in this sense (RSS feeds, which the classic app kept in the same file and which are a
/// widget setting here) are listed as skipped rather than silently dropped. Every entry goes through
/// the same validation as this app's own file ([S-504]).
/// </para>
/// </summary>
public static class ClassicAccounts
{
    /// <summary>The file name inside the classic app's data folder.</summary>
    public const string FileName = "email_accounts.json";

    /// <summary>The classic app's data folder name under <c>%APPDATA%</c> (its Tauri identifier).</summary>
    public const string FolderName = "com.themia.desktop";

    public static ClassicImport Parse(string json, DateTimeOffset now)
    {
        var accounts = new List<ClassicAccount>();
        var skipped = new List<string>();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            skipped.Add($"the file is not valid JSON ({ex.Message})");
            return new ClassicImport(accounts, skipped);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                skipped.Add("the file does not hold a list of accounts");
                return new ClassicImport(accounts, skipped);
            }

            foreach (var entry in document.RootElement.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object)
                {
                    skipped.Add($"'{entry.Name}': not an account");
                    continue;
                }

                var value = entry.Value;

                // The classic app defaulted a missing provider to Microsoft (its first and, for a
                // while, only provider), so an entry from that era is a Microsoft account.
                var wire = Str(value, "provider") ?? "microsoft";

                if (string.Equals(wire, "rss", StringComparison.OrdinalIgnoreCase))
                {
                    skipped.Add($"RSS feed '{Str(value, "display_name") ?? Str(value, "email") ?? entry.Name}': feeds are set on the RSS widget itself");
                    continue;
                }

                var provider = AccountProviders.TryParse(wire);
                var stored = new StoredAccount
                {
                    Id = entry.Name,
                    Provider = wire,
                    Address = Str(value, "email"),
                    DisplayName = Str(value, "display_name"),
                    Nickname = Str(value, "nickname"),
                    Host = Str(value, "host"),
                    Port = Int(value, "port"),
                    Username = Str(value, "username"),
                    ServerUrl = provider == AccountProvider.CardDav ? Str(value, "carddav_url") : Str(value, "caldav_url"),
                    ApiBaseUrl = Str(value, "api_base_url"),
                    ExpiresAt = Long(value, "expires_at") is { } seconds ? TokenExpiry.FromUnixSeconds(seconds) : null,
                    AddedAt = now,
                };

                var reasons = new List<string>();
                if (AccountRules.Validate(stored, now, reasons) is not { } account)
                {
                    skipped.Add(reasons.Count > 0 ? reasons[0] : $"'{entry.Name}': not usable");
                    continue;
                }

                accounts.Add(new ClassicAccount(
                    account,
                    Str(value, "access_token"),
                    Str(value, "refresh_token"),
                    Str(value, "password")));
            }
        }

        return new ClassicImport(accounts, skipped);
    }

    private static string? Str(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 } text
                ? text
                : null;

    private static int? Int(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static long? Long(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;
}
