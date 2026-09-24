using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>
/// The applications the Start menu knows about, as a list you can launch from.
/// <para>
/// Nothing about <i>which</i> apps exist is stored here: the list is read off the disk each time,
/// so an app installed or removed while the widget is on the desktop shows up without anybody
/// editing a setting. What is stored is how the list is shown and how it is opened.
/// </para>
/// </summary>
public sealed class AppsConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Apps;

    /// <summary>Show the filter box above the list.</summary>
    public bool ShowFilter { get; set; } = true;

    /// <summary>Show the Start-menu folder each app came from, when the widget is wide enough.</summary>
    public bool ShowCategory { get; set; } = true;

    /// <summary>
    /// Open on a single click instead of a double one.
    /// <para>
    /// Off by default, and that is the safe default rather than a taste: this surface sits under
    /// every other window, on the path the pointer takes across the desktop, and a single click
    /// launching an application is how a glanceable list becomes a hazard.
    /// </para>
    /// </summary>
    public bool OpenOnSingleClick { get; set; }

    /// <summary>How many applications to list, 10 to 1000.</summary>
    public int MaxCount { get; set; } = 300;

    [JsonIgnore]
    public int EffectiveMaxCount => Math.Clamp(MaxCount, 10, 1000);

    /// <summary>
    /// A backstop, not the refresh. Walking two Start-menu trees is disk work, and applications are
    /// installed a few times a month rather than a few times a minute.
    /// </summary>
    [JsonIgnore]
    public TimeSpan TickInterval => TimeSpan.FromMinutes(10);

    public override WidgetConfig Clone() => new AppsConfig
    {
        ShowFilter = ShowFilter,
        ShowCategory = ShowCategory,
        OpenOnSingleClick = OpenOnSingleClick,
        MaxCount = MaxCount,
    };
}
