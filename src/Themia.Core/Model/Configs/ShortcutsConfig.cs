using System.Text.Json.Serialization;
using Themia.Core.Widgets;

namespace Themia.Core.Model;

/// <summary>[S-1380] How the Shortcuts widget lays its items out (original S-22).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ShortcutViewMode>))]
public enum ShortcutViewMode
{
    /// <summary>Tiles: the icon with the name centred under it. The original's default.</summary>
    Grid = 0,

    /// <summary>Rows: icon and name.</summary>
    List = 1,

    /// <summary>A table: icon and name, then the target, then what kind of thing it is.</summary>
    Details = 2,
}

/// <summary>[S-1380] How big a tile or a row is drawn (original S-23 / S-46).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ShortcutItemSize>))]
public enum ShortcutItemSize
{
    Small = 0,
    Medium = 1,
    Large = 2,
}

/// <summary>
/// One thing the user keeps to hand: a web address, a file or folder, or a Windows location.
/// <para>
/// The <see cref="Target"/> is untrusted. It reaches here from a text box and from a JSON file in
/// the user's profile that anything running as the user can rewrite, and it ends at a shell call —
/// so it is checked by <see cref="LaunchTargets"/> every time it is used, never once on the way in.
/// A config that was edited after it was saved gets the same suspicion as one that was typed.
/// </para>
/// <para>
/// Package PD adds an icon path (for a dropped <c>.url</c>) and an app layout here; nothing in this
/// shape assumes there are only three fields.
/// </para>
/// </summary>
public sealed class ShortcutItem
{
    /// <summary>Stable id, so reordering and editing cannot swap two shortcuts' settings.</summary>
    public required string Id { get; set; }

    /// <summary>The user's own name for it, or empty to use the target's own.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Where it points. Never launched without being classified first.</summary>
    public string Target { get; set; } = string.Empty;

    public static ShortcutItem Create(string target, string label = "") => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Target = target,
        Label = label,
    };

    /// <summary>A copy with a new id, so a duplicated shortcut is a separate row.</summary>
    public ShortcutItem Clone() => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Label = Label,
        Target = Target,
    };
}

/// <summary>
/// An ordered list of shortcuts and how to draw them.
/// </summary>
public sealed class ShortcutsConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Shortcuts;

    public List<ShortcutItem> Items { get; set; } = [];

    /// <summary>[S-1380] Grid (default), list or details.</summary>
    public ShortcutViewMode View { get; set; } = ShortcutViewMode.Grid;

    /// <summary>[S-1380] Tile size in the grid (original <c>gridItemSize</c>).</summary>
    public ShortcutItemSize GridSize { get; set; } = ShortcutItemSize.Medium;

    /// <summary>[S-1380] Row size in the list and details views (original <c>itemSize</c>).</summary>
    public ShortcutItemSize ListSize { get; set; } = ShortcutItemSize.Medium;

    /// <summary>[S-1380] Grid tiles share out the widget's height instead of keeping their own (original S-47).</summary>
    public bool GridStretchHeight { get; set; }

    /// <summary>[S-1380] Grid tiles show only their icon (original S-71).</summary>
    public bool GridIconOnly { get; set; }

    /// <summary>[S-1380] A file's name keeps its extension (original S-48). A label still wins.</summary>
    public bool ShowFileExtension { get; set; }

    /// <summary>
    /// Single or double click to open. Double by default: this surface sits in the pointer's path
    /// across the desktop, and a stray click opening something is what makes a launcher a hazard.
    /// </summary>
    public ItemClickMode ClickMode { get; set; } = ItemClickMode.Double;

    /// <summary>
    /// [S-1393] A new Shortcuts widget starts with the Recycle Bin and This PC (original S-20), the
    /// two desktop icons a widget desktop most often replaces.
    /// </summary>
    public static ShortcutsConfig CreateDefault() => new()
    {
        Items =
        [
            ShortcutItem.Create(LaunchTargets.ShellTarget("RecycleBinFolder")),
            ShortcutItem.Create(LaunchTargets.ShellTarget("MyComputerFolder")),
        ],
    };

    public override IReadOnlyList<string> Repair()
    {
        var repairs = new List<string>();

        // A hand-edited "items": null, or a null entry in the list, is corrupt rather than empty.
        Items ??= [];

        if (Items.RemoveAll(item => item is null || item.Target is null || item.Label is null) > 0)
        {
            repairs.Add("Shortcuts widget had a broken item");
        }

        if (!Enum.IsDefined(View))
        {
            repairs.Add("Shortcuts widget had an unknown view");
            View = ShortcutViewMode.Grid;
        }

        if (!Enum.IsDefined(GridSize))
        {
            repairs.Add("Shortcuts widget had an unknown tile size");
            GridSize = ShortcutItemSize.Medium;
        }

        if (!Enum.IsDefined(ListSize))
        {
            repairs.Add("Shortcuts widget had an unknown row size");
            ListSize = ShortcutItemSize.Medium;
        }

        if (!Enum.IsDefined(ClickMode))
        {
            repairs.Add("Shortcuts widget had an unknown click mode");
            ClickMode = ItemClickMode.Double;
        }

        // Ids are what selection, rename and remove key on; a hand-edited file with a blank or
        // repeated one would make one click act on two rows.
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in Items)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || !seen.Add(item.Id))
            {
                item.Id = Guid.NewGuid().ToString("n");
                seen.Add(item.Id);
                repairs.Add("Shortcuts widget had a missing or repeated item id");
            }
        }

        return repairs;
    }

    public override WidgetConfig Clone()
    {
        var copy = new ShortcutsConfig
        {
            View = View,
            GridSize = GridSize,
            ListSize = ListSize,
            GridStretchHeight = GridStretchHeight,
            GridIconOnly = GridIconOnly,
            ShowFileExtension = ShowFileExtension,
            ClickMode = ClickMode,
        };

        foreach (var item in Items)
        {
            copy.Items.Add(item.Clone());
        }

        return copy;
    }
}
