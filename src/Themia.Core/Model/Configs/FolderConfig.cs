using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>How a folder listing is ordered.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FolderSort>))]
public enum FolderSort
{
    Name = 0,
    Modified = 1,
    Size = 2,
}

/// <summary>
/// The contents of one folder on this machine.
/// <para>
/// Read-only, and deliberately so: a widget sitting on the desktop is glanced at, not worked in, and
/// a delete triggered by a mis-click on a surface with no undo is not a feature. Opening an item
/// hands it to the shell, which is where file operations belong.
/// </para>
/// </summary>
public sealed class FolderConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Folder;

    /// <summary>
    /// The folder to list. Empty means the user's profile folder, so a newly placed widget shows
    /// something real rather than an error.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    public FolderSort Sort { get; set; } = FolderSort.Name;

    /// <summary>List the newest or largest first, depending on the sort.</summary>
    public bool Descending { get; set; }

    /// <summary>Put folders above files, as Explorer does.</summary>
    public bool FoldersFirst { get; set; } = true;

    /// <summary>Include items Windows marks hidden or system.</summary>
    public bool ShowHidden { get; set; }

    /// <summary>Show each item's size and modified date.</summary>
    public bool ShowDetail { get; set; } = true;

    /// <summary>How many entries to list, 1 to 100.</summary>
    public int MaxCount { get; set; } = 20;

    [JsonIgnore]
    public int EffectiveMaxCount => Math.Clamp(MaxCount, 1, 100);

    /// <summary>
    /// A folder changes when something writes to it, which a timer cannot know, so this is a
    /// backstop rather than the refresh: it is slow on purpose because enumerating a directory is
    /// disk work.
    /// </summary>
    [JsonIgnore]
    public TimeSpan TickInterval => TimeSpan.FromSeconds(30);

    public override WidgetConfig Clone() => new FolderConfig
    {
        Path = Path,
        Sort = Sort,
        Descending = Descending,
        FoldersFirst = FoldersFirst,
        ShowHidden = ShowHidden,
        ShowDetail = ShowDetail,
        MaxCount = MaxCount,
    };
}
