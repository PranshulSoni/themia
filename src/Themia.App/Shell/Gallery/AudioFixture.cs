#if DEBUG
using Themia.App.Services;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-1233] The mixer's and the volume tile's gallery sample: three output devices, a microphone,
/// and seven apps holding sessions (three playing, a browser with three tabs, one muted), with
/// meters mid-song — so a machine with a silent or absent sound card still shows the populated
/// layout at every size.
/// <para>
/// Read only by an audio view that is a preview (<see cref="Widgets.IWidgetSurface.IsPreview"/>),
/// which also never writes ([S-1233]), so neither the fixture nor the plain gallery page can move
/// the machine's real volume. Debug builds only.
/// </para>
/// </summary>
internal static class AudioFixture
{
    private const string Speakers = @"{0.0.0.00000000}.{fixture-speakers}";

    private static bool _installed;

    private static readonly AudioDevice[] Devices =
    [
        new(Speakers, "Speakers (Realtek(R) Audio)"),
        new(@"{0.0.0.00000000}.{fixture-headphones}", "Headphones (WH-1000XM4)"),
        new(@"{0.0.0.00000000}.{fixture-monitor}", "LG HDR 4K (NVIDIA High Definition Audio)"),
    ];

    private static readonly AudioSessionInfo[] Sessions =
    [
        new("s-spotify", "c:\\fixture\\spotify.exe", "Spotify", null, 0.72, false, true, false),
        new("s-chrome-1", "c:\\fixture\\chrome.exe", "Google Chrome", null, 1.0, false, true, false),
        new("s-chrome-2", "c:\\fixture\\chrome.exe", "Google Chrome", null, 0.8, false, true, false),
        new("s-chrome-3", "c:\\fixture\\chrome.exe", "Google Chrome", null, 0.6, false, false, false),
        new("s-discord", "c:\\fixture\\discord.exe", "Discord", null, 0.85, false, true, false),
        new("s-teams", "c:\\fixture\\ms-teams.exe", "Microsoft Teams", null, 1.0, false, false, false),
        new("s-steam", "c:\\fixture\\steam.exe", "Steam", null, 0.5, true, false, false),
        new("s-vlc", "c:\\fixture\\vlc.exe", "VLC media player", null, 0.9, false, false, false),
        new("s-system", MixerRules.SystemSoundsKey, string.Empty, null, 0.6, false, false, true),
    ];

    /// <summary>Meters mid-song: what the playing apps and the devices read at one instant.</summary>
    public static AudioPeaks Peaks { get; } = new(
        0.58,
        0.22,
        AudioRules.PeaksByKey(
        [
            ("s-spotify", "c:\\fixture\\spotify.exe", 0.64),
            ("s-chrome-1", "c:\\fixture\\chrome.exe", 0.41),
            ("s-chrome-2", "c:\\fixture\\chrome.exe", 0.18),
            ("s-chrome-3", "c:\\fixture\\chrome.exe", 0),
            ("s-discord", "c:\\fixture\\discord.exe", 0.33),
        ]));

    public static void Install(bool populated) => _installed = populated;

    /// <summary>The sample a preview shows for what it wants, or null when no fixture is installed.</summary>
    public static AudioSnapshot? Snapshot(AudioWant want)
    {
        if (!_installed)
        {
            return null;
        }

        var (id, target) = AudioRules.Resolve(want.DeviceId, Devices, Speakers);
        var microphone = want.Microphone ? new AudioLevel("fixture-mic", "Microphone Array (Intel Smart Sound)", 0.8, false) : null;

        if (id is null)
        {
            return new AudioSnapshot(AudioStatus.DeviceMissing, null, [], microphone, Devices, Speakers, target);
        }

        return new AudioSnapshot(
            AudioStatus.Ready,
            new AudioLevel(id, AudioRules.DeviceName(id, Devices, null), 0.64, false),
            want.Sessions ? Sessions : [],
            microphone,
            Devices,
            Speakers,
            target);
    }

    public static MixerConfig MixerConfig() => new() { ShowSystemSounds = true };

    public static VolumeConfig VolumeConfig() => new();
}
#endif
