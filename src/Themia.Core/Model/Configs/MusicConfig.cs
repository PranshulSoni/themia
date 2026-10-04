using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>
/// Whatever Windows is playing, from Windows' own media session.
/// <para>
/// There is nothing to configure about <i>where</i> it comes from, and that is the point: the
/// session is the same one the volume flyout and the media keys drive, so the widget works for
/// every player on the machine with no account, no token and no per-app setting.
/// </para>
/// </summary>
public sealed class MusicConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Music;

    /// <summary>Show the album art the session hands over, when it hands one over.</summary>
    public bool ShowAlbumArt { get; set; } = true;

    /// <summary>Show the play/pause, next and previous buttons.</summary>
    public bool ShowTransport { get; set; } = true;

    /// <summary>Show the progress bar and the elapsed/total labels.</summary>
    public bool ShowProgress { get; set; } = true;

    public override WidgetConfig Clone() => new MusicConfig
    {
        ShowAlbumArt = ShowAlbumArt,
        ShowTransport = ShowTransport,
        ShowProgress = ShowProgress,
    };
}
