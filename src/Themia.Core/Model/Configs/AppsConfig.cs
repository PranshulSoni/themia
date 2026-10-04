using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>[S-1520] How the Apps widget draws the list (original S-04 / S-10).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AppsViewMode>))]
public enum AppsViewMode
{
    /// <summary>Tiles: the icon with the name under it, in folder sections. The default, as in the original.</summary>
    Grid = 0,

    /// <summary>Rows: the icon and the name, in folder sections.</summary>
    List = 1,

    /// <summary>Flat name-only rows, no sections, no icons: the most names per inch.</summary>
    Compact = 2,
}

/// <summary>
/// The applications the Start menu knows about, as a list you can launch from.
/// <para>
/// Nothing about <i>which</i> apps exist is stored here: the list is read off the disk each time,
/// so an app installed or removed while the widget is on the desktop shows up without anybody
/// editing a setting. What is stored is how the list is shown, how it is opened, and what the user
/// asked not to see ([S-1523]).
/// </para>
/// </summary>
public sealed class AppsConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Apps;

    /// <summary>[S-1520] Tiles, rows or compact rows.</summary>
    public AppsViewMode View { get; set; } = AppsViewMode.Grid;

    /// <summary>Show the filter box above the list, where the widget is tall enough for it.</summary>
    public bool ShowFilter { get; set; } = true;

    /// <summary>
    /// [S-1521] Group the tiles and rows under the Start-menu folder each app came from. Compact
    /// rows are never grouped; they show the folder as a right-hand column instead.
    /// </summary>
    public bool GroupByFolder { get; set; } = true;

    /// <summary>
    /// [S-1525] Open on a double click (the default) or a single one. Double by default, and that is
    /// the safe default rather than a taste: this surface sits under every other window, on the path
    /// the pointer takes across the desktop, and a single click launching an application is how a
    /// glanceable list becomes a hazard.
    /// </summary>
    public ItemClickMode ClickMode { get; set; } = ItemClickMode.Double;

    /// <summary>How many applications to list, 10 to 1000.</summary>
    public int MaxCount { get; set; } = 300;

    /// <summary>[S-1523] Start-menu folders whose apps are left out (original S-22). Empty is the top level.</summary>
    public List<string> HiddenCategories { get; set; } = [];

    /// <summary>[S-1523] Shortcut files left out, by full path (original S-23 / S-24).</summary>
    public List<string> HiddenApps { get; set; } = [];

    [JsonIgnore]
    public int EffectiveMaxCount => Math.Clamp(MaxCount, 10, 1000);

    /// <summary>
    /// A backstop, not the refresh. Walking two Start-menu trees is disk work, and applications are
    /// installed a few times a month rather than a few times a minute; "Refresh" on the card's menu
    /// reads again at once.
    /// </summary>
    [JsonIgnore]
    public TimeSpan TickInterval => TimeSpan.FromMinutes(10);

    public override WidgetConfig Clone() => new AppsConfig
    {
        View = View,
        ShowFilter = ShowFilter,
        GroupByFolder = GroupByFolder,
        ClickMode = ClickMode,
        MaxCount = MaxCount,
        HiddenCategories = [.. HiddenCategories],
        HiddenApps = [.. HiddenApps],
    };

    public override IReadOnlyList<string> Repair()
    {
        var repairs = new List<string>();

        if (!Enum.IsDefined(View))
        {
            repairs.Add("Apps widget had an unknown view");
            View = AppsViewMode.Grid;
        }

        if (!Enum.IsDefined(ClickMode))
        {
            repairs.Add("Apps widget had an unknown click mode");
            ClickMode = ItemClickMode.Double;
        }

        // A hand-edited null list is corrupt rather than empty; a null or doubled entry is noise.
        // The top-level folder is the empty string, so blank names stay in the folder list.
        if (Clean(HiddenCategories, keepBlank: true) is { } categories)
        {
            HiddenCategories = categories;
            repairs.Add("Apps widget had a broken hidden-folder list");
        }

        if (Clean(HiddenApps, keepBlank: false) is { } apps)
        {
            HiddenApps = apps;
            repairs.Add("Apps widget had a broken hidden-app list");
        }

        return repairs;
    }

    /// <summary>The list without nulls and duplicates (case-insensitive), or null when it was already clean.</summary>
    private static List<string>? Clean(List<string>? list, bool keepBlank)
    {
        if (list is null)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = list
            .Where(entry => entry is not null && (keepBlank || entry.Trim().Length > 0) && seen.Add(entry))
            .ToList();

        return kept.Count == list.Count ? null : kept;
    }
}
