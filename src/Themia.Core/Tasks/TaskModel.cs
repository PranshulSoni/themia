using System.Text.Json.Serialization;

namespace Themia.Core.Tasks;

/// <summary>
/// [S-590] Where a Tasks widget's list comes from: a Microsoft or Google account, or a list kept on
/// this PC. A closed set, written by name, so a config can never name a third source by accident.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TaskSource>))]
public enum TaskSource
{
    /// <summary>Microsoft To Do (Graph) or Google Tasks, through one of Themia's own accounts.</summary>
    Account = 0,

    /// <summary>A named list stored in <c>tasks.json</c> beside the app state. No account, no network.</summary>
    Local = 1,
}

/// <summary>[S-602] How a row lays out its due date: under the title, or beside it on one line.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TaskViewMode>))]
public enum TaskViewMode
{
    List = 0,
    Compact = 1,
}

/// <summary>
/// One task, as both providers and the local store are reduced to it.
/// <para>
/// <see cref="DueRaw"/> stays the provider's own string. Which calendar <i>day</i> it means depends on
/// the viewer's zone for one of the two encodings in the wild ([S-593]), so it is resolved at render
/// time from one clock reading, never frozen at fetch time.
/// </para>
/// </summary>
/// <param name="Id">The provider's id, a local id, or a temporary one for a task still being created.</param>
/// <param name="ListId">
/// The list the task lives in. Under Today the rows come from many lists, so every write is addressed
/// to this rather than to the widget's selection ([S-597]).
/// </param>
/// <param name="Important">Graph's <c>importance: high</c>. Google and local tasks have no such field.</param>
public sealed record TaskItem(
    string Id,
    string ListId,
    string Title,
    bool Completed,
    bool Important = false,
    string? DueRaw = null)
{
    /// <summary>What the row shows: an untitled task is named rather than drawn as a gap.</summary>
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "(untitled task)" : Title;
}

/// <summary>A task list on an account.</summary>
/// <param name="IsDefault">
/// Graph's <c>wellknownListName: defaultList</c>; for Google the first list, which is its
/// <c>@default</c>. A widget with no list chosen shows this one.
/// </param>
public sealed record TaskListInfo(string Id, string Name, bool IsDefault);
