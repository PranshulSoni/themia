using Microsoft.UI.Xaml;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

/// <summary>
/// Settings rows for the now-playing tile.
/// <para>
/// Nothing about accounts or players: the widget reads Windows' own media session, so there is
/// nothing to sign in to and nothing to choose between.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    /// <summary>Segoe Fluent Icons "Equalizer", for the spectrum row.</summary>
    private static readonly string SpectrumGlyph = char.ConvertFromUtf32(0xE9E9);

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
            "The seek bar, and the elapsed and total times",
            FluentLayout.Toggle(config.ShowProgress, value => { config.ShowProgress = value; save(); })));

        // [S-1470] System-wide: the bars show whatever the PC is playing, not only this player.
        rows.Add(FluentLayout.Row(SpectrumGlyph, "Show the spectrum",
            "Bars that move with the sound this PC is playing",
            FluentLayout.Toggle(config.ShowSpectrum, value => { config.ShowSpectrum = value; save(); })));

        // [S-1464] Edit mode always shows a hidden widget, so it can still be found and changed.
        rows.Add(FluentLayout.Row(Glyphs.Hide, "Hide when idle",
            "The widget disappears from the desktop; edit mode still shows it",
            FluentLayout.Choice<MusicHideWhenIdle>(
                [
                    (MusicHideWhenIdle.Never, "Never"),
                    (MusicHideWhenIdle.NoSession, "When no player is open"),
                    (MusicHideWhenIdle.NotPlaying, "When nothing is playing"),
                ],
                config.HideWhenIdle,
                value => { config.HideWhenIdle = value; save(); })));
    }
}
