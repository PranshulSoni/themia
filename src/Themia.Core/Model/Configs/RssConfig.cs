using System.Text.Json.Serialization;
using Themia.Core.Widgets;

namespace Themia.Core.Model;

/// <summary>
/// Headlines from one feed.
/// <para>
/// The address is the whole of the configuration and it comes from a text box and from a
/// hand-editable JSON file, so it is checked by <see cref="WebInput.Url"/> on the way in from both
/// — see <see cref="ResolvedUrl"/>. A feed is also the one thing this app reads that is written by
/// somebody else entirely: every title, link and summary that comes back is attacker-controlled
/// text, displayed and never interpreted.
/// </para>
/// </summary>
public sealed class RssConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Rss;

    /// <summary>The feed address, exactly as typed. Never repaired; refused values stay refused.</summary>
    public string FeedUrl { get; set; } = string.Empty;

    /// <summary>A name to show in the header instead of the feed's own title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// How many items to show, 5 to 50.
    /// <para>
    /// A display cap, not a fetch count. Themia asks the address for whatever it serves and then
    /// trims; a host that publishes ten items can never fill a setting of fifty, and raising the
    /// number changes nothing.
    /// </para>
    /// </summary>
    public int MaxItems { get; set; } = 20;

    /// <summary>Show the site the item came from above its title.</summary>
    public bool ShowSource { get; set; } = true;

    /// <summary>Show the one-line summary under the title.</summary>
    public bool ShowPreview { get; set; } = true;

    /// <summary>Show how long ago the item was published.</summary>
    public bool ShowDate { get; set; } = true;

    public int RefreshMinutes { get; set; } = 15;

    [JsonIgnore]
    public int EffectiveMaxItems => Math.Clamp(MaxItems, 5, 50);

    /// <summary>
    /// The checked address, or the reason it was refused. Read on every use rather than cached, so
    /// a value hand-edited into the state file goes through the same gate the settings box uses.
    /// </summary>
    [JsonIgnore]
    public WebUrlResult ResolvedUrl => WebInput.Url(FeedUrl);

    /// <summary>The cadence, clamped. Five minutes is the floor; no feed publishes faster than that.</summary>
    [JsonIgnore]
    public TimeSpan TickInterval => TimeSpan.FromMinutes(Math.Clamp(RefreshMinutes, 5, 240));

    public override WidgetConfig Clone() => new RssConfig
    {
        FeedUrl = FeedUrl,
        Title = Title,
        MaxItems = MaxItems,
        ShowSource = ShowSource,
        ShowPreview = ShowPreview,
        ShowDate = ShowDate,
        RefreshMinutes = RefreshMinutes,
    };
}
