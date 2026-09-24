using System.Text.Json.Serialization;
using Themia.Core.Widgets;

namespace Themia.Core.Model;

/// <summary>
/// One thing the user keeps to hand: a web address, a file or folder, or a Windows location.
/// <para>
/// The <see cref="Target"/> is untrusted. It reaches here from a text box and from a JSON file in
/// the user's profile that anything running as the user can rewrite, and it ends at a shell call —
/// so it is checked by <see cref="LaunchTargets"/> every time it is used, never once on the way in.
/// A config that was edited after it was saved gets the same suspicion as one that was typed.
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
/// An ordered list of shortcuts.
/// <para>
/// The original app also let a shortcut hold an "app layout" — a tree of panes that opens several
/// applications and snaps each onto a region of the display. That is a window manager wearing a
/// shortcut's clothes, and it is deliberately not ported: this widget opens one target per row.
/// </para>
/// </summary>
public sealed class ShortcutsConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Shortcuts;

    public List<ShortcutItem> Items { get; set; } = [];

    /// <summary>
    /// Open on a single click instead of a double one. Off by default: see
    /// <see cref="AppsConfig.OpenOnSingleClick"/> for why a launcher on the desktop layer defaults
    /// to the deliberate gesture.
    /// </summary>
    public bool OpenOnSingleClick { get; set; }

    /// <summary>Show each shortcut's target under its name, when the widget is wide enough.</summary>
    public bool ShowTarget { get; set; }

    public override WidgetConfig Clone()
    {
        var copy = new ShortcutsConfig
        {
            OpenOnSingleClick = OpenOnSingleClick,
            ShowTarget = ShowTarget,
        };

        foreach (var item in Items)
        {
            copy.Items.Add(item.Clone());
        }

        return copy;
    }
}
