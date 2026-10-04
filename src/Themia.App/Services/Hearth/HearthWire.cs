using System.Text.Json;
using System.Text.Json.Serialization;

namespace Themia.App.Services.Hearth;

// The wire shapes Themia needs from the hearth hub, per hearth's PROTOCOL.md.
//
// These are declared here rather than by referencing Hearth.Client, and the reason is Native AOT:
// the SDK serialises through `JsonSerializer.Serialize(value, value.GetType(), options)`, which is
// reflection-based and neither trim-safe nor AOT-safe, so referencing it would either fail the
// build (warnings are errors here) or throw at runtime once the types were trimmed. Speaking the
// documented protocol with a source-generated context keeps the app AOT while still leaving every
// account, token, provider and sync concern inside hearth, which is the point of using it.
//
// Only the fields this widget reads are declared. Hearth rejects unknown members on the way IN, so
// requests must stay exactly on schema; replies may carry more than this, which is ignored.

/// <summary>A request envelope. <c>id</c> correlates the reply.</summary>
internal sealed class HearthRequest<T>
{
    public required long Id { get; init; }

    public required string Method { get; init; }

    public T? Params { get; init; }
}

internal sealed class HelloParams
{
    public required string Name { get; init; }

    public required IReadOnlyList<string> Scopes { get; init; }
}

internal sealed class SubscribeParams
{
    public required IReadOnlyList<string> Types { get; init; }
}

internal sealed class EmailQueryParams
{
    /// <summary>Null means every collection the app may read.</summary>
    public string? Collection { get; init; }

    public EmailFilter? Filter { get; init; }

    public IReadOnlyList<SortSpec>? Sort { get; init; }

    public int? Limit { get; init; }
}

internal sealed class EmailFilter
{
    public bool? Unread { get; init; }
}

internal sealed class SortSpec
{
    public required string By { get; init; }

    public bool Desc { get; init; }
}

internal sealed class HelloResult
{
    public int Protocol { get; init; }

    public IReadOnlyList<HearthCollection> Collections { get; init; } = [];
}

/// <summary>One collection the hub has granted this app.</summary>
internal sealed class HearthCollection
{
    public string Id { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    /// <summary>Display name — for mail, the account address.</summary>
    public string Name { get; init; } = string.Empty;

    public string? Account { get; init; }

    public IReadOnlyList<string> Rights { get; init; } = [];
}

internal sealed class EmailQueryResult
{
    public IReadOnlyList<EmailRecord> Records { get; init; } = [];

    public string? Cursor { get; init; }

    public int Total { get; init; }
}

/// <summary>A message, cut down to what a list shows.</summary>
internal sealed class EmailRecord
{
    public string Id { get; init; } = string.Empty;

    public string Collection { get; init; } = string.Empty;

    public HearthAddress? From { get; init; }

    public string Subject { get; init; } = string.Empty;

    /// <summary>The Date header: when it was sent.</summary>
    public DateTimeOffset? Date { get; init; }

    /// <summary>When the server took delivery. Preferred for ordering when present.</summary>
    public DateTimeOffset? ReceivedAt { get; init; }

    public bool Unread { get; init; }

    public bool Flagged { get; init; }

    public bool Draft { get; init; }

    /// <summary>First 200 characters of the plain-text body, derived by hearth.</summary>
    public string Preview { get; init; } = string.Empty;

    /// <summary>The timestamp a list should sort and label by.</summary>
    [JsonIgnore]
    public DateTimeOffset When => ReceivedAt ?? Date ?? DateTimeOffset.MinValue;

    /// <summary>Who to show in the list: the sender's name, or their address if it has no name.</summary>
    [JsonIgnore]
    public string Who
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(From?.Name))
            {
                return From.Name;
            }

            return string.IsNullOrWhiteSpace(From?.Value) ? "Unknown sender" : From.Value;
        }
    }
}

internal sealed class HearthAddress
{
    public string? Name { get; init; }

    public string Value { get; init; } = string.Empty;
}

internal sealed class HearthErrorBody
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string? Field { get; init; }
}

/// <summary>Raised when the hub refuses a call. <see cref="Code"/> is hearth's own, e.g. <c>denied</c>.</summary>
internal sealed class HearthCallException(string code, string message)
    : Exception($"{code}: {message}")
{
    public string Code { get; } = code;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HearthRequest<HelloParams>))]
[JsonSerializable(typeof(HearthRequest<SubscribeParams>))]
[JsonSerializable(typeof(HearthRequest<EmailQueryParams>))]
[JsonSerializable(typeof(HelloResult))]
[JsonSerializable(typeof(EmailQueryResult))]
[JsonSerializable(typeof(HearthErrorBody))]
internal sealed partial class HearthJsonContext : JsonSerializerContext;
