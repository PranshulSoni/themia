using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Themia.Core.Diagnostics;

namespace Themia.App.Services.Hearth;

/// <summary>
/// The one hearth connection, as everything that is not mail needs to see it.
/// </summary>
/// <remarks>
/// <para>
/// Hearth identifies an app by its <b>process</b> and grants consent to that process, so a second
/// pipe would mean a second consent prompt for the same app, and a second set of grants for the
/// user to keep straight. There is therefore exactly one connection, and this is the seam the
/// contacts, calendar and task queries reach it through instead of opening one of their own.
/// </para>
/// <para>
/// <see cref="HearthMailService"/> is that connection, and it already has all four members below -
/// <c>CallAsync</c> with this exact signature. Making it implement this interface is the whole of
/// the merge; see the note on <see cref="HearthDataService.Scopes"/>.
/// </para>
/// </remarks>
internal interface IHearthConnection
{
    /// <summary>Why the widgets are showing what they are showing.</summary>
    HearthStatus Status { get; }

    /// <summary>
    /// Every collection the hub granted, of every type, in the order it listed them. Not only the
    /// mail ones: a connection that discards the rest at <c>hello</c> leaves nothing to show a
    /// calendar from.
    /// </summary>
    IReadOnlyList<HearthCollection> Collections { get; }

    /// <summary>Connect, say hello and subscribe. Safe to call repeatedly.</summary>
    Task<bool> EnsureConnectedAsync();

    /// <summary>
    /// One request, one reply. The caller passes the source-generated type info because that is
    /// what keeps this AOT-safe: there is no overload here that serialises by reflection.
    /// </summary>
    Task<JsonElement> CallAsync<T>(
        string method,
        T parameters,
        JsonTypeInfo<HearthRequest<T>> typeInfo,
        TimeSpan? timeout = null);
}

/// <summary>
/// Contacts, calendar events and tasks, read from the hearth hub.
/// <para>
/// This holds no connection of its own: it is handed the app's one
/// <see cref="IHearthConnection"/> and asks it for what a widget needs. Every method answers with
/// an empty list rather than throwing, because they all run on a render path and an unreachable
/// hub is an ordinary state, not a fault.
/// </para>
/// <para>
/// Read-only by design, like mail: the scopes are <c>contact</c>, <c>event</c> and <c>task</c>,
/// none of which can write, so the worst these widgets can do with a granted calendar is show it.
/// </para>
/// </summary>
internal sealed class HearthDataService
{
    private const string LogScope = "hearth";

    /// <summary>
    /// A widget page. Hearth caps a query at 500 whatever is asked; this is smaller because a
    /// widget renders a list someone reads, not a page someone scrolls through.
    /// </summary>
    private const int MaxRecords = 100;

    private readonly IHearthConnection _hub;

    public HearthDataService(IHearthConnection hub)
    {
        ArgumentNullException.ThrowIfNull(hub);
        _hub = hub;
    }

    /// <summary>
    /// What these queries need in <c>hello</c>, on top of the <c>email</c> the mail service asks
    /// for. They are named here rather than sent from here because the connection says hello once,
    /// for the whole app: asking for a scope later would be a second prompt, which is the thing
    /// one connection exists to avoid. The connection's <c>hello</c> should send
    /// <c>["email", .. Scopes]</c>, and its <c>subscribe</c> the same list of types.
    /// </summary>
    public static IReadOnlyList<string> Scopes { get; } = ["contact", "event", "task"];

    /// <summary>Why the widgets are showing what they are showing. The connection owns this.</summary>
    public HearthStatus Status => _hub.Status;

    /// <summary>Address books the hub has granted, in the order it listed them.</summary>
    public IReadOnlyList<HearthCollection> ContactCollections => CollectionsOf("contact");

    /// <summary>Calendars the hub has granted.</summary>
    public IReadOnlyList<HearthCollection> CalendarCollections => CollectionsOf("event");

    /// <summary>Task lists the hub has granted.</summary>
    public IReadOnlyList<HearthCollection> TaskCollections => CollectionsOf("task");

    /// <summary>
    /// Contacts in alphabetical order, or the ones matching <paramref name="search"/>.
    /// </summary>
    /// <remarks>
    /// The order is hearth's <c>displayName</c> sort and is not repeated here: the provider chose
    /// that name, and re-sorting on anything else would give a list that disagrees with its own
    /// paging. The search is hearth's <c>text</c> filter, which matches anywhere in the record -
    /// including notes and the organisation, not only the name.
    /// </remarks>
    public Task<IReadOnlyList<ContactRecord>> ContactsAsync(string? collectionId, string? search, int limit)
    {
        var query = new ContactQueryParams
        {
            Collection = Collection(collectionId),
            Filter = string.IsNullOrWhiteSpace(search) ? null : new ContactFilter { Text = search.Trim() },
            Sort = [new SortSpec { By = "displayName" }],
            Limit = Page(limit),
        };

        return QueryAsync(
            "contact.query",
            query,
            HearthRecordsJsonContext.Default.HearthRequestContactQueryParams,
            HearthRecordsJsonContext.Default.ContactQueryResult,
            result => result.Records);
    }

    /// <summary>
    /// What is on next: every event touching the window that starts at <paramref name="from"/> and
    /// runs for <paramref name="window"/>, earliest first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window is asked for rather than read from the clock here, so a refresh renders one
    /// consistent list and a test can ask for a Tuesday.
    /// </para>
    /// <para>
    /// A recurring meeting is one record in the store, so the query expands it: with a range,
    /// hearth returns one record per occurrence, each with its own id and its override applied.
    /// Without that, a weekly stand-up would appear once, on the date the series began, which is a
    /// calendar that is wrong every week but the first. Hearth refuses <c>expand</c> without both
    /// bounds, which is why the two are always sent together.
    /// </para>
    /// <para>
    /// Hearth's range asks which events <i>touch</i> it, so a meeting that started before
    /// <paramref name="from"/> and has not ended is in the answer. That is deliberate: "on now" is
    /// the one thing a glanceable calendar must not drop.
    /// </para>
    /// </remarks>
    public Task<IReadOnlyList<EventRecord>> UpcomingAsync(
        string? collectionId,
        DateTimeOffset from,
        TimeSpan window,
        int limit)
    {
        // A window that is zero or negative has no events in it by definition, and hearth would be
        // within its rights to say so with an error. Ask a day instead of asking nonsense.
        var span = window > TimeSpan.Zero ? window : TimeSpan.FromDays(1);

        var query = new EventQueryParams
        {
            Collection = Collection(collectionId),
            Filter = new EventFilter
            {
                After = Rfc3339(from),
                Before = Rfc3339(from + span),
                Expand = true,
            },
            Sort = [new SortSpec { By = "start" }],
            Limit = Page(limit),
        };

        return QueryAsync(
            "event.query",
            query,
            HearthRecordsJsonContext.Default.HearthRequestEventQueryParams,
            HearthRecordsJsonContext.Default.EventQueryResult,
            result => result.Records);
    }

    /// <summary>
    /// Open tasks by due date, soonest first, with undated ones after them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hearth sorts on the due column, and a task with no due date sorts <i>first</i> there, so the
    /// order is put right here rather than shown as the store happens to hold it. The reordering is
    /// within the page hearth returned, so a list long enough to be truncated can still be
    /// truncated at an undated task.
    /// </para>
    /// <para>
    /// <paramref name="dueBefore"/> is the way to ask for dated work only, and it is worth knowing
    /// why: a comparison against a task with no due date is never true, so a bound excludes the
    /// undated ones entirely rather than keeping them at the end.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<TaskRecord>> OpenTasksAsync(
        string? collectionId,
        DateOnly? dueBefore,
        int limit)
    {
        var query = new TaskQueryParams
        {
            Collection = Collection(collectionId),
            Filter = new TaskFilter
            {
                Completed = false,
                DueBefore = dueBefore is { } bound
                    ? bound.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : null,
            },
            Sort = [new SortSpec { By = "due" }],
            Limit = Page(limit),
        };

        var tasks = await QueryAsync(
            "task.query",
            query,
            HearthRecordsJsonContext.Default.HearthRequestTaskQueryParams,
            HearthRecordsJsonContext.Default.TaskQueryResult,
            result => result.Records).ConfigureAwait(true);

        return [.. tasks.OrderBy(t => t.DueDate ?? DateOnly.MaxValue)];
    }

    // -----------------------------------------------------------------------------------------
    // Plumbing
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The one place a query is made and the one place its failures are handled. Both catches log
    /// and answer empty: a widget that throws on the render path takes the surface with it, and a
    /// hub that is not there is not an exceptional thing for a desktop to find.
    /// </summary>
    private async Task<IReadOnlyList<TRecord>> QueryAsync<TParams, TResult, TRecord>(
        string method,
        TParams parameters,
        JsonTypeInfo<HearthRequest<TParams>> requestInfo,
        JsonTypeInfo<TResult> resultInfo,
        Func<TResult, IReadOnlyList<TRecord>> records)
        where TResult : class
    {
        if (!await _hub.EnsureConnectedAsync().ConfigureAwait(true))
        {
            return [];
        }

        try
        {
            var raw = await _hub.CallAsync(method, parameters, requestInfo).ConfigureAwait(true);
            var result = raw.Deserialize(resultInfo);

            // A reply that deserialises to nothing is a hub answering something this build cannot
            // read, which is a fact worth having in the log rather than an empty list with no
            // explanation.
            if (result is null)
            {
                Log.Warn(LogScope, $"{method} returned a result this build could not read");
                return [];
            }

            return records(result);
        }
        catch (HearthCallException ex)
        {
            // The connection owns Status and moves it itself when the pipe drops; a refusal of one
            // query is not a reason to declare the whole hub unreachable, so this only reports.
            Log.Warn(LogScope, $"{method} refused: {ex.Message}");
            return [];
        }
        catch (Exception ex)
        {
            Log.Error(LogScope, $"{method} failed", ex);
            return [];
        }
    }

    private IReadOnlyList<HearthCollection> CollectionsOf(string type) =>
        [.. _hub.Collections.Where(c => string.Equals(c.Type, type, StringComparison.Ordinal))];

    /// <summary>Blank means "every collection of this type I may see", which hearth spells as absent.</summary>
    private static string? Collection(string? collectionId) =>
        string.IsNullOrWhiteSpace(collectionId) ? null : collectionId;

    private static int Page(int limit) => Math.Clamp(limit, 1, MaxRecords);

    /// <summary>
    /// A timestamp in the shape hearth parses: RFC 3339, UTC, with the trailing <c>Z</c>. Sending
    /// an offset form would be read correctly, but a filter that reads the same everywhere is one
    /// less thing to reason about when a range comes back empty.
    /// </summary>
    private static string Rfc3339(DateTimeOffset moment) =>
        moment.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
