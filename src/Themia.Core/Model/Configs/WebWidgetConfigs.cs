using System.Text.Json.Serialization;
using Themia.Core.Widgets;

namespace Themia.Core.Model;

/// <summary>[S-466] How a video picture fills the widget. Named for what it does to the picture.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StreamFit>))]
public enum StreamFit
{
    /// <summary>The whole picture, letterboxed. The default: a camera view that crops is a camera view that hides things.</summary>
    Contain = 0,

    /// <summary>Fill the widget and crop what does not fit.</summary>
    Cover = 1,

    /// <summary>Fill both axes and distort.</summary>
    Stretch = 2,
}

/// <summary>[S-468] Clockwise rotation for a camera mounted sideways or upside down. A closed set.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StreamRotation>))]
public enum StreamRotation
{
    None = 0,
    Quarter = 90,
    Half = 180,
    ThreeQuarter = 270,
}

/// <summary>
/// A live camera picture: an MJPEG stream, a snapshot endpoint polled on an interval, or a video
/// stream Windows can play itself.
/// <para>
/// The address comes from a text box and from the hand-editable state file and is handed to a
/// socket, so it is checked by <see cref="WebInput.Url"/> on every read — see
/// <see cref="ResolvedUrl"/>. Nothing else in the widget re-checks the scheme.
/// </para>
/// </summary>
public sealed class VideoStreamConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.VideoStream;

    /// <summary>The stream address, exactly as typed. Never repaired; never normalised, because cameras route on the query.</summary>
    public string Url { get; set; } = string.Empty;

    public StreamFit Fit { get; set; } = StreamFit.Contain;

    public StreamRotation Rotation { get; set; } = StreamRotation.None;

    /// <summary>[S-469] Stopped by the user. Persisted, so a parked camera stays parked across restarts.</summary>
    public bool Paused { get; set; }

    /// <summary>[S-470] 0 holds the stream open; anything else grabs one frame that often and hangs up.</summary>
    public int RefreshSeconds { get; set; }

    /// <summary>The checked address, or why it was refused. Read on every use, never cached.</summary>
    [JsonIgnore]
    public WebUrlResult ResolvedUrl => WebInput.Url(Url);

    [JsonIgnore]
    public int EffectiveRefreshSeconds => VideoStreamRules.ClampRefreshSeconds(RefreshSeconds);

    /// <summary>A rotation the file names but the enum does not is read as no rotation, never as 45 degrees.</summary>
    [JsonIgnore]
    public StreamRotation EffectiveRotation => Enum.IsDefined(Rotation) ? Rotation : StreamRotation.None;

    [JsonIgnore]
    public StreamFit EffectiveFit => Enum.IsDefined(Fit) ? Fit : StreamFit.Contain;

    public override WidgetConfig Clone() => new VideoStreamConfig
    {
        Url = Url,
        Fit = Fit,
        Rotation = Rotation,
        Paused = Paused,
        RefreshSeconds = RefreshSeconds,
    };
}

/// <summary>
/// One element of a web page, pinned by a CSS selector and re-rendered on an interval.
/// <para>
/// The page is rendered by an isolated, off-screen WebView2 and only a <i>picture</i> of the element
/// comes back into the app, so no remote markup is ever parsed or shown here. The address and the
/// selector are still a trust boundary — they decide what that browser loads and runs — and are
/// checked on every read.
/// </para>
/// </summary>
public sealed class WebElementConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.WebElement;

    /// <summary>[S-473] The page to render.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>[S-474] CSS selector of the pinned element, normally produced by the picker.</summary>
    public string Selector { get; set; } = string.Empty;

    /// <summary>A friendly description of what was picked, for the settings page only. The widget never reads it.</summary>
    public string PickedLabel { get; set; } = string.Empty;

    /// <summary>[S-475] Minutes between renders, clamped to 5..120.</summary>
    public int RefreshMinutes { get; set; } = WebElementRules.DefaultRefreshMinutes;

    [JsonIgnore]
    public WebUrlResult ResolvedUrl => WebInput.Url(Url);

    /// <summary>The selector if it is one this widget will hand to the browser, else empty.</summary>
    [JsonIgnore]
    public string EffectiveSelector => WebElementRules.IsPlausibleSelector(Selector) ? Selector.Trim() : string.Empty;

    [JsonIgnore]
    public TimeSpan RefreshInterval => TimeSpan.FromMinutes(WebElementRules.ClampRefreshMinutes(RefreshMinutes));

    public override WidgetConfig Clone() => new WebElementConfig
    {
        Url = Url,
        Selector = Selector,
        PickedLabel = PickedLabel,
        RefreshMinutes = RefreshMinutes,
    };
}
