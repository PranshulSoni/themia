using System.Text.Json.Serialization;
using Themia.Core.Widgets;

namespace Themia.Core.Model;

/// <summary>How a folder listing is ordered.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FolderSort>))]
public enum FolderSort
{
    Name = 0,
    Modified = 1,
    Size = 2,
}

/// <summary>[S-1320] How the folder is drawn: rows, a table, labelled tiles, or bare icons.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FolderViewMode>))]
public enum FolderViewMode
{
    List = 0,
    Details = 1,
    Grid = 2,
    Icons = 3,
}

/// <summary>[S-1320] Row density of the list and details views.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FolderItemSize>))]
public enum FolderItemSize
{
    ExtraSmall = 0,
    Small = 1,
    Medium = 2,
    Large = 3,
}

/// <summary>[S-1320] Tile size of the grid and icons views.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FolderTileSize>))]
public enum FolderTileSize
{
    Small = 0,
    Medium = 1,
    Large = 2,
}

/// <summary>[S-1320] What opening a folder does: hand it to Explorer, or show it inside the widget.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FolderNavigation>))]
public enum FolderNavigation
{
    Explorer = 0,
    Navigate = 1,
}

/// <summary>
/// [S-1320] The contents of one folder on this machine, as a small file browser: list, details,
/// grid or icons; navigation inside the widget or out to Explorer; and the file operations Explorer
/// offers, every one of which goes through the shell (and every delete to the Recycle Bin, [S-1330]).
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

    public FolderViewMode View { get; set; } = FolderViewMode.List;

    public FolderItemSize ItemSize { get; set; } = FolderItemSize.Medium;

    public FolderTileSize TileSize { get; set; } = FolderTileSize.Small;

    /// <summary>Grid and icons: rows share the height when every tile fits without scrolling.</summary>
    public bool StretchTiles { get; set; }

    /// <summary>The details view's columns after Name, in the order they are drawn.</summary>
    public List<FolderColumn> Columns { get; set; } = [.. FolderColumns.Defaults];

    public FolderNavigation Navigation { get; set; } = FolderNavigation.Explorer;

    public ItemClickMode ClickMode { get; set; } = ItemClickMode.Double;

    /// <summary>Comma-separated name keywords; an item shows when its name contains any of them.</summary>
    public string Filter { get; set; } = string.Empty;

    public FolderSort Sort { get; set; } = FolderSort.Name;

    /// <summary>List the newest or largest first, depending on the sort.</summary>
    public bool Descending { get; set; }

    /// <summary>Put folders above files, as Explorer does.</summary>
    public bool FoldersFirst { get; set; } = true;

    /// <summary>Include items Windows marks hidden or system.</summary>
    public bool ShowHidden { get; set; }

    /// <summary>[S-1321] Off by default, as in the original: "report" rather than "report.docx".</summary>
    public bool ShowExtensions { get; set; }

    /// <summary>
    /// The watcher ([S-1333]) is the refresh; this is its backstop for a share whose change
    /// notifications never arrive. Slow on purpose: enumerating a directory is disk work.
    /// </summary>
    [JsonIgnore]
    public TimeSpan TickInterval => TimeSpan.FromSeconds(30);

    public override WidgetConfig Clone() => new FolderConfig
    {
        Path = Path,
        View = View,
        ItemSize = ItemSize,
        TileSize = TileSize,
        StretchTiles = StretchTiles,
        Columns = [.. Columns ?? []],
        Navigation = Navigation,
        ClickMode = ClickMode,
        Filter = Filter,
        Sort = Sort,
        Descending = Descending,
        FoldersFirst = FoldersFirst,
        ShowHidden = ShowHidden,
        ShowExtensions = ShowExtensions,
    };

    /// <summary>[S-1320] Columns: known ones, each once, order kept; a hand-edited file is repaired loudly.</summary>
    public override IReadOnlyList<string> Repair()
    {
        var notes = new List<string>(base.Repair());

        var (clean, dropped) = FolderColumns.Clean(Columns);

        if (dropped > 0)
        {
            notes.Add($"{Kind}: {dropped} unknown or repeated column(s) dropped");
        }

        Columns = clean;
        Path ??= string.Empty;
        Filter ??= string.Empty;
        return notes;
    }
}
