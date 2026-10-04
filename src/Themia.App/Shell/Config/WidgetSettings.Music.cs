using Microsoft.UI.Xaml;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

/// <summary>
/// Settings rows for the now-playing tile.
/// <para>
/// Three switches and nothing about accounts or players: the widget reads Windows' own media
/// session, so there is nothing to sign in to and nothing to choose between.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    public static void Music(List<FrameworkElement> rows, MusicConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Now playing"));

        rows.Add(FluentLayout.Row(Glyphs.Gallery, "Show album art",
            "Only when the player offers one",
            FluentLayout.Toggle(config.ShowAlbumArt, value => { config.ShowAlbumArt = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Play, "Show the transport",
            "Previous, play or pause, next",
            FluentLayout.Toggle(config.ShowTransport, value => { config.ShowTransport = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Motion, "Show progress",
            "The bar, and the elapsed and total times",
            FluentLayout.Toggle(config.ShowProgress, value => { config.ShowProgress = value; save(); })));
    }
}
