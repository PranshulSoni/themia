#if DEBUG
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Themia.App.Services;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-1474] The gallery's populated pages for now playing and the pomodoro.
/// <para>
/// Music: a made-up track half-way through, painted cover art and a frozen spectrum, read by a
/// preview instead of the media session — so the fixture page never reads, let alone drives, what
/// the machine is playing, and never starts the loopback capture. Pomodoro: a focus phase part-way
/// through with three sessions done, paused, so nothing ticks towards a flip and nothing can sound.
/// The plain pages show the real empty states. Debug builds only.
/// </para>
/// </summary>
internal static class MediaFixture
{
    internal sealed record MusicSample(NowPlaying Now, ImageSource Art, float[] Bands);

    internal sealed record PomodoroSample(PomodoroState State, int Completed);

    public static MusicSample? Music { get; private set; }

    public static PomodoroSample? Pomodoro { get; private set; }

    public static void InstallMusic(bool populated) => Music = populated
        ? new MusicSample(
            new NowPlaying(
                MusicState.Playing,
                "Midnight City",
                "M83 · Hurry Up, We're Dreaming",
                "fixture",
                new TrackTimeline(TimeSpan.FromSeconds(97), TimeSpan.FromSeconds(243)),
                DateTimeOffset.UtcNow,
                CanPlayPause: true,
                CanNext: true,
                CanPrevious: true,
                CanSeek: true,
                Artist: "M83",
                Album: "Hurry Up, We're Dreaming",
                Source: "Spotify"),
            Cover(),
            [0.62f, 0.81f, 0.9f, 0.74f, 0.66f, 0.71f, 0.58f, 0.63f, 0.52f, 0.47f, 0.55f, 0.43f,
             0.39f, 0.45f, 0.36f, 0.31f, 0.34f, 0.27f, 0.22f, 0.25f, 0.18f, 0.14f, 0.1f, 0.06f])
        : null;

    public static void InstallPomodoro(bool populated) => Pomodoro = populated
        ? new PomodoroSample(new PomodoroState(PomodoroPhase.Work, (14 * 60) + 32), 3)
        : null;

    public static MusicConfig MusicConfig() => new() { ShowSpectrum = true };

    public static PomodoroConfig PomodoroConfig() => new();

    /// <summary>A painted cover: a diagonal dusk gradient with a sun, so the art slot reads as art.</summary>
    private static WriteableBitmap Cover()
    {
        const int Size = 160;
        var pixels = new byte[Size * Size * 4];

        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var t = (x + y) / (2.0 * Size);
                var (r, g, b) = (Lerp(38, 233, t), Lerp(24, 92, t), Lerp(84, 120, t));

                var dx = x - (Size * 0.62);
                var dy = y - (Size * 0.38);
                if ((dx * dx) + (dy * dy) < Size * Size * 0.03)
                {
                    (r, g, b) = (255, 214, 140);
                }

                var at = ((y * Size) + x) * 4;
                pixels[at] = b;
                pixels[at + 1] = g;
                pixels[at + 2] = r;
                pixels[at + 3] = 255;
            }
        }

        var bitmap = new WriteableBitmap(Size, Size);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(pixels);
        }

        bitmap.Invalidate();
        return bitmap;

        static byte Lerp(double a, double b, double t) => (byte)Math.Round(a + ((b - a) * t));
    }
}
#endif
