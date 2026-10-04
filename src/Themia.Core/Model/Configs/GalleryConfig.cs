using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>How the pictures are laid out on the card.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GalleryLayout>))]
public enum GalleryLayout
{
    /// <summary>One picture at a time, filling the card, advancing on a timer.</summary>
    Slideshow = 0,

    /// <summary>As many tiles as the card has room for.</summary>
    Grid = 1,
}

/// <summary>What a picture does with a frame that is not its shape.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GalleryFit>))]
public enum GalleryFit
{
    /// <summary>Fill the frame and crop the overflow. No letterboxing, no whole picture.</summary>
    Fill = 0,

    /// <summary>Show the whole picture and leave the frame's spare space empty.</summary>
    Fit = 1,
}

/// <summary>
/// Pictures from a folder on this PC.
/// <para>
/// An empty <see cref="Path"/> means <b>no folder chosen</b>, and the widget says so. It
/// deliberately does not fall back to the Pictures library the way the folder widget falls back to
/// the profile folder: a file list showing an unasked-for folder is merely surprising, whereas a
/// photo frame that starts showing pictures nobody pointed it at, on a surface that sits on the
/// desktop in front of whoever walks past, is a different kind of mistake.
/// </para>
/// </summary>
public sealed class GalleryConfig : WidgetConfig
{
    /// <summary>The fewest seconds a slide can be held. Below this it is a flicker, not a slideshow.</summary>
    public const int MinSlideSeconds = 2;

    /// <summary>An hour. Past it the timer is indistinguishable from a picture that never changes.</summary>
    public const int MaxSlideSeconds = 3600;

    /// <summary>
    /// The most pictures one widget will hold decoded at once, and so the ceiling on the setting.
    /// A folder can hold fifty thousand photographs; this is what stops the widget from trying.
    /// </summary>
    public const int MaxImageCount = 60;

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Gallery;

    /// <summary>The folder to show. Empty means none has been chosen yet.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Include pictures in folders below this one.</summary>
    public bool Recursive { get; set; }

    public GalleryLayout Layout { get; set; } = GalleryLayout.Slideshow;

    public GalleryFit Fit { get; set; } = GalleryFit.Fill;

    /// <summary>
    /// Take the pictures in a random order rather than in name order. Re-rolled on each rescan, so a
    /// folder bigger than <see cref="MaxImages"/> eventually shows all of it.
    /// </summary>
    public bool Shuffle { get; set; } = true;

    /// <summary>How long each slide is held, in seconds.</summary>
    public int SlideshowSeconds { get; set; } = 10;

    /// <summary>How many pictures are taken from the folder.</summary>
    public int MaxImages { get; set; } = 24;

    [JsonIgnore]
    public int EffectiveMaxImages => Math.Clamp(MaxImages, 1, MaxImageCount);

    [JsonIgnore]
    public int EffectiveSlideshowSeconds => Math.Clamp(SlideshowSeconds, MinSlideSeconds, MaxSlideSeconds);

    /// <summary>How long one slide is held. The slideshow owns its own timer; this is its period.</summary>
    [JsonIgnore]
    public TimeSpan SlideInterval => TimeSpan.FromSeconds(EffectiveSlideshowSeconds);

    /// <summary>
    /// The rescan cadence. A folder changes when something writes to it, which no timer can know, so
    /// this is a slow backstop rather than the refresh: walking a directory tree is disk work and
    /// doing it often on a photo library is the widget nobody can explain the fan noise about.
    /// </summary>
    [JsonIgnore]
    public TimeSpan TickInterval => TimeSpan.FromMinutes(5);

    public override WidgetConfig Clone() => new GalleryConfig
    {
        Path = Path,
        Recursive = Recursive,
        Layout = Layout,
        Fit = Fit,
        Shuffle = Shuffle,
        SlideshowSeconds = SlideshowSeconds,
        MaxImages = MaxImages,
    };
}
