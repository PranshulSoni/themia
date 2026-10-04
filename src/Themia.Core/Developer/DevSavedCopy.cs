using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Themia.Core.Diagnostics;

namespace Themia.Core.Developer;

/// <summary>A feed's last answer for one account, as read back from disk.</summary>
/// <param name="At">When GitHub gave this answer — not when it was saved or read.</param>
public sealed record DevSaved<T>(T Value, DateTimeOffset At)
    where T : class;

/// <summary>
/// [S-747] The developer widgets' saved copy: a feed's last good answer per account, written to the
/// local (not roaming) app data folder so a restart shows the list straight away, flagged as saved
/// until the first refresh lands.
/// <para>
/// One small file per feed and account, named by a hash of both: the feed key names repositories
/// and the account id a login, neither of which belongs in a file name. The file is an envelope —
/// schema version, the key and account it answers, when — around the payload, so a file from
/// another version, or one that a hash collision put under the wrong name, is refused rather than
/// drawn. Never the token: the payload is the model the widget draws, which has none.
/// </para>
/// </summary>
public static class DevSavedCopy
{
    private const string Scope = "developer.saved";

    /// <summary>The envelope's shape. A file with any other version is not this shape and is ignored, never read.</summary>
    public const int SchemaVersion = 1;

    /// <summary>A copy older than this is not shown at all: a week-old list is not worth the confusion.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    /// <summary>The file a feed's answer for one account lives in: a hash, so nothing readable is in the name.</summary>
    public static string FileName(string key, string accountId)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(accountId);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(accountId + "\n" + key));
        return Convert.ToHexStringLower(hash.AsSpan(0, 16)) + ".json";
    }

    /// <summary>The envelope around one answer, as text to write atomically.</summary>
    public static string Encode<T>(string key, string accountId, DateTimeOffset at, T value, JsonTypeInfo<T> contract)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(accountId);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(contract);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("key", key);
            writer.WriteString("account", accountId);
            writer.WriteString("at", at.ToString("O", CultureInfo.InvariantCulture));
            writer.WritePropertyName("value");
            JsonSerializer.Serialize(writer, value, contract);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// The answer in a saved file, or null — with the reason logged — when the file is from another
    /// version, answers another feed or account, is older than <see cref="MaxAge"/>, or does not
    /// parse. Null is "nothing to show", never half a model.
    /// </summary>
    public static DevSaved<T>? Decode<T>(string json, string key, string accountId, JsonTypeInfo<T> contract, DateTimeOffset now)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(accountId);
        ArgumentNullException.ThrowIfNull(contract);

        if (string.IsNullOrWhiteSpace(json))
        {
            Log.Warn(Scope, "a saved copy is empty; ignoring it");
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number)
            {
                Log.Warn(Scope, "a saved copy has no schema version; ignoring it");
                return null;
            }

            if (version.GetInt32() != SchemaVersion)
            {
                Log.Info(Scope, $"a saved copy is schema {version.GetInt32()}, not {SchemaVersion}; ignoring it");
                return null;
            }

            if (!root.TryGetProperty("key", out var savedKey) || savedKey.GetString() != key
                || !root.TryGetProperty("account", out var savedAccount) || savedAccount.GetString() != accountId)
            {
                Log.Warn(Scope, "a saved copy answers another feed; ignoring it");
                return null;
            }

            if (!root.TryGetProperty("at", out var atText)
                || !DateTimeOffset.TryParse(atText.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at))
            {
                Log.Warn(Scope, "a saved copy has no readable time; ignoring it");
                return null;
            }

            if (now - at > MaxAge)
            {
                Log.Info(Scope, "a saved copy is more than a week old; ignoring it");
                return null;
            }

            if (!root.TryGetProperty("value", out var payload) || payload.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                Log.Warn(Scope, "a saved copy has no value; ignoring it");
                return null;
            }

            var value = payload.Deserialize(contract);
            if (value is null)
            {
                Log.Warn(Scope, "a saved copy's value is null; ignoring it");
                return null;
            }

            // A provider clock ahead of this one must not make a saved copy look newer than now.
            return new DevSaved<T>(value, at > now ? now : at);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or NotSupportedException)
        {
            Log.Warn(Scope, "a saved copy does not parse; ignoring it", ex);
            return null;
        }
    }
}

/// <summary>[S-747] Source-generated contracts for the saved copies. Native AOT has no reflection serializer.</summary>
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(RepoActivity))]
[JsonSerializable(typeof(IReadOnlyList<SearchItem>))]
[JsonSerializable(typeof(IReadOnlyList<NotificationItem>))]
public sealed partial class DevSavedJsonContext : JsonSerializerContext;
