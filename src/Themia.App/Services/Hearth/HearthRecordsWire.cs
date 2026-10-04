using System.Globalization;
using System.Text.Json.Serialization;

namespace Themia.App.Services.Hearth;

// The wire shapes for hearth's other three record types - contact, event and task - per hearth's
// PROTOCOL.md and SCHEMA.md. The mail shapes live in HearthWire.cs; the envelope, the sort spec,
// the collection and the error body are shared from there rather than declared twice.
//
// These are declared here rather than by referencing Hearth.Records for the same reason mail is:
// the SDK serialises through `JsonSerializer.Serialize(value, value.GetType(), options)`, which is
// reflection-based and neither trim-safe nor AOT-safe, so referencing it would either fail the
// build (warnings are errors here) or throw at runtime once the types were trimmed. Speaking the
// documented protocol with a source-generated context keeps the app AOT while still leaving every
// account, token, provider and sync concern inside hearth.
//
// Only the fields a widget actually shows are declared. Hearth rejects unknown members on the way
// IN, so requests must stay exactly on schema; replies carry the whole record, and what is not
// declared here is ignored on read.

// ---------------------------------------------------------------------------------------------
// Requests
// ---------------------------------------------------------------------------------------------

internal sealed class ContactQueryParams
{
    /// <summary>Null means every collection the app may read.</summary>
    public string? Collection { get; init; }

    public ContactFilter? Filter { get; init; }

    public IReadOnlyList<SortSpec>? Sort { get; init; }

    public int? Limit { get; init; }
}

/// <summary>
/// A contact filter. Hearth supports <c>text</c>, <c>email</c>, <c>phone</c> and
/// <c>updatedSince</c>; only the free-text one is declared, because that is the search a list
/// widget offers. A key hearth does not support is refused outright rather than ignored, so
/// nothing here may be invented.
/// </summary>
internal sealed class ContactFilter
{
    /// <summary>Matches anywhere in the record, notes included.</summary>
    public string? Text { get; init; }
}

internal sealed class EventQueryParams
{
    public string? Collection { get; init; }

    public EventFilter? Filter { get; init; }

    public IReadOnlyList<SortSpec>? Sort { get; init; }

    public int? Limit { get; init; }
}

/// <summary>
/// A window of the calendar. <see cref="Expand"/> turns a series into one record per occurrence,
/// and hearth refuses it without both bounds - expanding everything a calendar ever holds is not
/// a question with an answer.
/// </summary>
internal sealed class EventFilter
{
    /// <summary>RFC 3339, UTC. Events that are still running at this moment are included.</summary>
    public string? After { get; init; }

    /// <summary>RFC 3339, UTC. Exclusive.</summary>
    public string? Before { get; init; }

    public bool? Expand { get; init; }
}

internal sealed class TaskQueryParams
{
    public string? Collection { get; init; }

    public TaskFilter? Filter { get; init; }

    public IReadOnlyList<SortSpec>? Sort { get; init; }

    public int? Limit { get; init; }
}

internal sealed class TaskFilter
{
    public bool? Completed { get; init; }

    /// <summary>A date, <c>yyyy-MM-dd</c>: due dates are dates, because that is all Google Tasks holds.</summary>
    public string? DueBefore { get; init; }

    /// <summary>A date, <c>yyyy-MM-dd</c>.</summary>
    public string? DueAfter { get; init; }
}

// ---------------------------------------------------------------------------------------------
// Replies
// ---------------------------------------------------------------------------------------------

internal sealed class ContactQueryResult
{
    public IReadOnlyList<ContactRecord> Records { get; init; } = [];

    public string? Cursor { get; init; }

    public int Total { get; init; }
}

/// <summary>
/// A contact, cut down to what a list shows.
/// <para>
/// The structured <c>name</c> is deliberately absent: hearth sorts contacts by
/// <c>displayName</c>, which the provider itself chose, so rendering a name assembled from the
/// parts would let the list disagree with the order it was returned in.
/// </para>
/// </summary>
internal sealed class ContactRecord
{
    public string Id { get; init; } = string.Empty;

    public string Collection { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public HearthOrganization? Organization { get; init; }

    public IReadOnlyList<HearthTypedValue> Emails { get; init; } = [];

    public IReadOnlyList<HearthTypedValue> Phones { get; init; } = [];

    /// <summary>What a row is labelled with: the display name, or the first address if there is none.</summary>
    [JsonIgnore]
    public string Who
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DisplayName))
            {
                return DisplayName;
            }

            return string.IsNullOrWhiteSpace(PrimaryEmail) ? "Unnamed contact" : PrimaryEmail;
        }
    }

    /// <summary>
    /// The second line of a row: the job, then the employer, then the address. A contact with none
    /// of the three has no second line rather than an empty one.
    /// </summary>
    [JsonIgnore]
    public string Detail
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Organization?.Title))
            {
                return Organization.Title;
            }

            if (!string.IsNullOrWhiteSpace(Organization?.Name))
            {
                return Organization.Name;
            }

            return PrimaryEmail;
        }
    }

    /// <summary>The first address the provider listed, or empty. Order is the provider's own.</summary>
    [JsonIgnore]
    public string PrimaryEmail => First(Emails);

    /// <summary>The first number the provider listed, or empty.</summary>
    [JsonIgnore]
    public string PrimaryPhone => First(Phones);

    private static string First(IReadOnlyList<HearthTypedValue> values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value?.Value))
            {
                return value.Value;
            }
        }

        return string.Empty;
    }
}

/// <summary>Where someone works. Every part is optional, and a provider may fill none of them.</summary>
internal sealed class HearthOrganization
{
    public string? Name { get; init; }

    public string? Department { get; init; }

    public string? Title { get; init; }
}

/// <summary>
/// An address, number or URL with the slot it sits in. <see cref="Type"/> stays a string rather
/// than an enum: it is hearth's closed set, but a value this build has not heard of must render as
/// an unlabelled row instead of failing the whole reply.
/// </summary>
internal sealed class HearthTypedValue
{
    public string Value { get; init; } = string.Empty;

    /// <summary><c>home</c>, <c>work</c>, <c>mobile</c>, <c>fax</c> or <c>other</c>.</summary>
    public string Type { get; init; } = string.Empty;
}

internal sealed class EventQueryResult
{
    public IReadOnlyList<EventRecord> Records { get; init; } = [];

    public string? Cursor { get; init; }

    public int Total { get; init; }
}

/// <summary>
/// One event, or - when the query expanded a series - one occurrence of one, which carries its own
/// id and is otherwise an event like any other.
/// </summary>
internal sealed class EventRecord
{
    public string Id { get; init; } = string.Empty;

    public string Collection { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    /// <summary>One line of plain text. Empty when the event has none.</summary>
    public string Location { get; init; } = string.Empty;

    public HearthDateTimeZone? Start { get; init; }

    /// <summary>Exclusive: a meeting ending at 10:00 does not occupy 10:00.</summary>
    public HearthDateTimeZone? End { get; init; }

    /// <summary>When true, the two ends carry a date and the zone means nothing.</summary>
    public bool AllDay { get; init; }

    /// <summary><c>confirmed</c>, <c>tentative</c> or <c>cancelled</c>.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>When it starts, on this machine's clock. Null when the record carries no start.</summary>
    [JsonIgnore]
    public DateTimeOffset? StartsAt => HearthTime.Moment(Start);

    /// <summary>When it ends, on this machine's clock.</summary>
    [JsonIgnore]
    public DateTimeOffset? EndsAt => HearthTime.Moment(End);

    /// <summary>
    /// A cancelled occurrence is still a record, so a list that does not check this shows meetings
    /// that are not happening.
    /// </summary>
    [JsonIgnore]
    public bool Cancelled => string.Equals(Status, "cancelled", StringComparison.Ordinal);
}

/// <summary>
/// A wall clock plus the zone it is read in, as the provider expressed it.
/// <see cref="TimeZone"/> is null for an all-day event and for a floating time - one given with no
/// zone at all, which means the clock wherever it is read.
/// </summary>
internal sealed class HearthDateTimeZone
{
    /// <summary><c>yyyy-MM-ddTHH:mm:ss</c>, or <c>yyyy-MM-dd</c> when the event is all day.</summary>
    public string DateTime { get; init; } = string.Empty;

    /// <summary>An IANA zone name, e.g. <c>Europe/Zurich</c>.</summary>
    public string? TimeZone { get; init; }
}

internal sealed class TaskQueryResult
{
    public IReadOnlyList<TaskRecord> Records { get; init; } = [];

    public string? Cursor { get; init; }

    public int Total { get; init; }
}

/// <summary>A task, cut down to what a list shows.</summary>
internal sealed class TaskRecord
{
    public string Id { get; init; } = string.Empty;

    public string Collection { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    /// <summary>A date, <c>yyyy-MM-dd</c>, or null. Never a time: Google Tasks holds no time.</summary>
    public string? Due { get; init; }

    public bool Completed { get; init; }

    /// <summary><c>low</c>, <c>normal</c> or <c>high</c>.</summary>
    public string Priority { get; init; } = string.Empty;

    /// <summary>The due date, or null when the task has none or the provider sent something unparsable.</summary>
    [JsonIgnore]
    public DateOnly? DueDate =>
        DateOnly.TryParse(Due, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    /// <summary>
    /// Whether this was due before <paramref name="today"/>. The day is passed in rather than read
    /// from the clock here, so what a list renders is decided once, by the caller, rather than
    /// drifting row by row across midnight.
    /// </summary>
    public bool IsOverdue(DateOnly today) => DueDate is { } due && due < today && !Completed;

    /// <summary>Whether this is worth marking in a list. Hearth's high is the only one that is.</summary>
    [JsonIgnore]
    public bool Important => string.Equals(Priority, "high", StringComparison.Ordinal);
}

/// <summary>
/// Hearth's date shapes, read onto this machine's clock.
/// </summary>
/// <remarks>
/// Hearth stores a wall clock plus an IANA zone and never converts one for an app, so this is
/// where a calendar row's actual moment is worked out. The app builds with
/// <c>InvariantGlobalization</c>, which leaves .NET with no IANA-to-Windows zone table, so a
/// foreign zone name usually cannot be resolved at all. What happens then is deliberate: the wall
/// clock is read as-is, i.e. on the local clock, which is exactly right for the common case of an
/// event in the user's own zone and honestly wrong - by the zone difference, and no more - for one
/// abroad. Inventing an offset the hub never named would be worse.
/// </remarks>
internal static class HearthTime
{
    public static DateTimeOffset? Moment(HearthDateTimeZone? when)
    {
        if (when is null || string.IsNullOrWhiteSpace(when.DateTime))
        {
            return null;
        }

        var text = when.DateTime;

        // An all-day end is a date, and so is an all-day start; both read as midnight locally.
        if (text.Length == 10)
        {
            return DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? Local(day.ToDateTime(TimeOnly.MinValue))
                : null;
        }

        if (!System.DateTime.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var clock))
        {
            return null;
        }

        // Unspecified, always: the string carries no offset, and the zone beside it is what says
        // which clock it is on.
        clock = System.DateTime.SpecifyKind(clock, DateTimeKind.Unspecified);

        if (string.IsNullOrWhiteSpace(when.TimeZone))
        {
            return Local(clock); // floating, or all-day: the clock wherever it is read
        }

        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(when.TimeZone);

            // GetUtcOffset rather than ConvertTimeToUtc: a clock that falls in a daylight-saving
            // gap is a real thing a provider can send, and it must render as a row rather than
            // throw on a render path.
            return new DateTimeOffset(clock, zone.GetUtcOffset(clock));
        }
        catch (TimeZoneNotFoundException)
        {
            return Local(clock); // see the remarks: invariant globalization has no IANA table
        }
        catch (InvalidTimeZoneException)
        {
            return Local(clock);
        }
    }

    private static DateTimeOffset Local(DateTime clock) =>
        new(clock, TimeZoneInfo.Local.GetUtcOffset(clock));
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HearthRequest<ContactQueryParams>))]
[JsonSerializable(typeof(HearthRequest<EventQueryParams>))]
[JsonSerializable(typeof(HearthRequest<TaskQueryParams>))]
[JsonSerializable(typeof(ContactQueryResult))]
[JsonSerializable(typeof(EventQueryResult))]
[JsonSerializable(typeof(TaskQueryResult))]
internal sealed partial class HearthRecordsJsonContext : JsonSerializerContext;
