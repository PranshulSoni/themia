#if DEBUG
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Themia.App.Services.Web;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Themia.App.Shell.Gallery;

/// <summary>One page of the gallery: a widget, and whether it is drawn with its fixture.</summary>
internal readonly record struct GalleryEntry(WidgetKind Kind, bool Fixture);

/// <summary>
/// Populated configs for widgets whose default config shows nothing but an empty state, so the
/// gallery can draw both: <c>--gallery-png VideoStream</c> is the empty widget,
/// <c>--gallery-png VideoStream+fixture</c> the same widget with something in it.
/// <para>
/// Debug builds only. A fixture may start a loopback server or point a widget at a public page,
/// and none of that belongs anywhere near a release.
/// </para>
/// </summary>
internal static class GalleryFixtures
{
    private const string Scope = "gallery";

    /// <summary>The suffix that asks for a widget's fixture: <c>Kind+fixture</c>.</summary>
    public const string Suffix = "+fixture";

    private static MjpegFixtureServer? _camera;

    public static bool Has(WidgetKind kind) => kind is WidgetKind.VideoStream or WidgetKind.ScreenTime
        or WidgetKind.Email
        or WidgetKind.Tasks or WidgetKind.OneDrive
        or WidgetKind.Calendar or WidgetKind.Contacts
        or WidgetKind.Power or WidgetKind.Bluetooth or WidgetKind.Battery
        or WidgetKind.Cpu or WidgetKind.Memory
        or WidgetKind.Disk or WidgetKind.Ping or WidgetKind.Network
        or WidgetKind.Mixer or WidgetKind.Volume
        or WidgetKind.Gpu
        or WidgetKind.Music or WidgetKind.Pomodoro
        or WidgetKind.Folder
        or WidgetKind.ViewSwitcher
        or WidgetKind.GitHub or WidgetKind.GitHubIssues or WidgetKind.GitHubNotifications or WidgetKind.GitHubReviews or WidgetKind.ClaudeUsage
        || LayoutFixtures.Has(kind) || ContainerFixture.Has(kind) || DeskToolFixtures.Has(kind) || AppWidgetFixtures.Has(kind);

    /// <summary>
    /// Put back the data sources a fixture swapped in, before a page is drawn without one. Screen
    /// time reads a source rather than its config, so its plain page needs the empty source back.
    /// </summary>
    public static void Reset()
    {
        ScreenTimeFixture.Install(populated: false);

        // [S-557] Mail reads a source too; its plain page is the no-account state.
        MailFixture.Install(populated: false);

        // [S-590]/[S-606] Tasks and OneDrive read services too; their plain pages are the
        // no-account state.
        TasksFixture.Install(populated: false);
        TasksFixture.InstallDrive(populated: false);

        // [S-589] Calendar and contacts read a shared source; their plain pages are "no account".
        OfficeFixture.Install(populated: false);

        // [S-637] GitHub and Claude usage read sources; their plain pages are "sign in".
        DeveloperFixture.Install(populated: false);

        // [S-1253]/[S-1258] Power and Bluetooth: the plain pages read this machine.
        PowerFixture.Install(populated: false);
        BluetoothFixture.Install(populated: false);

        // [S-1823] The meters' clock starts again with the page; every meter reads this machine.
        MeterFixture.Reset();

        // [S-1186] Battery: the plain page reads this machine (a desktop shows "No battery").
        BatteryFixture.Install(populated: false);

        // [S-1169] Disk and ping: the plain pages read this machine and probe the default host.
        DiskPingFixture.InstallDisk(populated: false);
        DiskPingFixture.InstallPing(populated: false);

        // [S-1420] Habits read the device's list pool; the plain page starts with none.
        HabitsFixture.Install(populated: false);

        // [S-1368] Notes read the device's note store; the plain page gets an empty throwaway one.
        NotesFixture.Install(populated: false);

        // [S-1443] Clipboard seeds its in-memory history from a fixture; the plain page has none.
        ClipboardFixture.Install(populated: false);

        // [S-1233] Mixer and volume: the plain pages read this machine (read-only, never written).
        AudioFixture.Install(populated: false);

        // [S-1474] Music and the pomodoro: the plain pages read the session / start fresh.
        MediaFixture.InstallMusic(populated: false);
        MediaFixture.InstallPomodoro(populated: false);

        // [S-1516] The calculator's plain page starts with no history.
        DeskToolFixtures.Reset();

        // [S-1534] Search's plain page has no recent searches.
        AppWidgetFixtures.Reset();

        // [S-1658] The view switcher's plain page has three views.
        GalleryViewsDisplay.Crowded = false;
    }

    /// <summary>The populated config for a kind, or null when it has no fixture.</summary>
    public static WidgetConfig? Create(WidgetKind kind)
    {
        switch (kind)
        {
            case WidgetKind.VideoStream:
                _camera ??= MjpegFixtureServer.Start();
                // [S-1312] With the status chip on, so the XL page shows it.
                return _camera is null ? null : new VideoStreamConfig { Url = _camera.Url, ShowStatus = true };

            case WidgetKind.ScreenTime:
                // [S-459] The history is not in the config, so the fixture is a data source.
                ScreenTimeFixture.Install(populated: true);

                // [S-1213] With a daily limit, so the large sizes show the limit bar.
                return new ScreenTimeConfig { GoalSeconds = 6 * 3600 };

            case WidgetKind.Gpu:
                // [S-1207] The first two adapters: how several panels share every size. On a machine
                // with one adapter the second panel says it is not there. [S-1823] Their names are
                // the machine's, their load the fixture's, with a full window of history.
                MeterFixture.Install(WidgetKind.Gpu);
                return new GpuConfig { Adapters = [0, 1] };

            case WidgetKind.Cpu:
                // [S-1823] A processor that idles with bursts, at the meter's own cadence and span.
                MeterFixture.Install(WidgetKind.Cpu);
                return new CpuConfig();

            case WidgetKind.Memory:
                MeterFixture.Install(WidgetKind.Memory);
                return new MemoryConfig();

            case WidgetKind.Email:
                // [S-557] The gallery has no accounts, so the inbox is a data source as well.
                MailFixture.Install(populated: true);
                return MailFixture.Config();

            case WidgetKind.Tasks:
                TasksFixture.Install(populated: true);
                return TasksFixture.Config();

            case WidgetKind.Folder:
                // [S-1339] A made-up project folder in the gallery's temp directory, never the user's files.
                return FolderFixture.Config();

            case WidgetKind.OneDrive:
                TasksFixture.InstallDrive(populated: true);
                return new Themia.Core.Model.OneDriveConfig();

            case WidgetKind.Calendar:
                // [S-589] Two made-up accounts with a week around today and three birthdays.
                OfficeFixture.Install(populated: true);
                return OfficeFixture.CalendarConfig();

            case WidgetKind.Contacts:
                OfficeFixture.Install(populated: true);
                return OfficeFixture.ContactsConfig();

            case WidgetKind.GitHub or WidgetKind.GitHubIssues or WidgetKind.GitHubNotifications or WidgetKind.GitHubReviews or WidgetKind.ClaudeUsage:
                // [S-637] Two made-up GitHub accounts (one Enterprise) and a Claude plan.
                DeveloperFixture.Install(populated: true);
                return DeveloperFixture.Config(kind);

            case WidgetKind.Power:
                // [S-1253] A laptop on battery with every action chosen. Buttons stay inert: gallery
                // cards are previews ([S-1251]).
                PowerFixture.Install(populated: true);
                return PowerFixture.Config();

            case WidgetKind.Bluetooth:
                // [S-1258] Fourteen paired devices, three connected with battery and signal.
                BluetoothFixture.Install(populated: true);
                return BluetoothFixture.Config();

            case WidgetKind.Battery:
                // [S-1186] A discharging laptop battery that reports every figure. Like every meter's
                // fixture, its history is a full window from the first frame ([S-1823]).
                BatteryFixture.Install(populated: true);
                return BatteryFixture.Config();

            case WidgetKind.Disk:
                // [S-1169] Three drives, the system drive busy, one removable.
                DiskPingFixture.InstallDisk(populated: true);
                return DiskPingFixture.DiskConfig();

            case WidgetKind.Ping:
                // [S-1169] A labelled router that answers in 3-40 ms and misses one probe in nine.
                DiskPingFixture.InstallPing(populated: true);
                return DiskPingFixture.PingConfig();

            case WidgetKind.Network:
                // [S-1823] Chatter with a download every 50 s, never this machine's traffic.
                MeterFixture.Install(WidgetKind.Network);
                return new Themia.Core.Model.NetworkConfig();

            case WidgetKind.Mixer:
                // [S-1233] Three devices, a microphone and seven apps; previews never write.
                AudioFixture.Install(populated: true);
                return AudioFixture.MixerConfig();

            case WidgetKind.Volume:
                AudioFixture.Install(populated: true);
                return AudioFixture.VolumeConfig();

            case WidgetKind.Music:
                // [S-1474] A made-up track with art and bars; never the machine's session.
                MediaFixture.InstallMusic(populated: true);
                return MediaFixture.MusicConfig();

            case WidgetKind.Pomodoro:
                // [S-1474] Part-way through a focus phase, three sessions done, paused.
                MediaFixture.InstallPomodoro(populated: true);
                return MediaFixture.PomodoroConfig();

            case WidgetKind.ViewSwitcher:
                // [S-1658] The views come from the pretend display, not the config: twelve of them.
                GalleryViewsDisplay.Crowded = true;
                return new ViewSwitcherConfig();

            default:
                // The ported widgets' populated states, kept in their own file.
                return ContainerFixture.Create(kind) ?? LayoutFixtures.Create(kind) ?? DeskToolFixtures.Create(kind) ?? AppWidgetFixtures.Create(kind);
        }
    }

    /// <summary>
    /// A camera on loopback: a real <c>multipart/x-mixed-replace</c> stream of JPEG test-card
    /// frames, so the gallery exercises the actual socket, part reader and decoder rather than a
    /// stub. A raw socket rather than <c>HttpListener</c>: no URL reservation, no admin, and a
    /// camera's own HTTP is about this sophisticated anyway.
    /// </summary>
    private sealed class MjpegFixtureServer
    {
        private const string Boundary = "themiafixture";
        private readonly TcpListener _listener;
        private readonly List<byte[]> _frames;

        private MjpegFixtureServer(TcpListener listener, List<byte[]> frames)
        {
            _listener = listener;
            _frames = frames;
            Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/?action=stream";
        }

        public string Url { get; }

        public static MjpegFixtureServer? Start()
        {
            try
            {
                // Off the UI thread: the encoder's continuations would otherwise wait for the very
                // thread this blocks.
                var frames = Task.Run(() => TestCard.RenderFramesAsync(12)).GetAwaiter().GetResult();
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();

                var server = new MjpegFixtureServer(listener, frames);
                _ = server.AcceptLoopAsync();
                return server;
            }
            catch (Exception ex) when (ex is SocketException or System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
                Log.Error(Scope, "could not start the fixture camera", ex);
                return null;
            }
        }

        private async Task AcceptLoopAsync()
        {
            while (true)
            {
                TcpClient client;

                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    Log.Warn(Scope, "the fixture camera stopped accepting", ex);
                    return;
                }

                _ = ServeAsync(client);
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    await SkipRequestAsync(stream).ConfigureAwait(false);

                    var head = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\n" +
                        $"Content-Type: multipart/x-mixed-replace; boundary={Boundary}\r\n" +
                        "Cache-Control: no-cache\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(head).ConfigureAwait(false);

                    while (true)
                    {
                        // Indexed by the clock, not per connection, so every widget on the page
                        // shows the same moment and a frozen one stands out against the rest.
                        var frame = _frames[(int)(Environment.TickCount64 / 100 % _frames.Count)];
                        var part = Encoding.ASCII.GetBytes(
                            $"--{Boundary}\r\nContent-Type: image/jpeg\r\nContent-Length: {frame.Length}\r\n\r\n");
                        await stream.WriteAsync(part).ConfigureAwait(false);
                        await stream.WriteAsync(frame).ConfigureAwait(false);
                        await stream.WriteAsync("\r\n"u8.ToArray()).ConfigureAwait(false);

                        // A camera's own cadence, ten frames a second.
                        await Task.Delay(100).ConfigureAwait(false);
                    }
                }
                catch (IOException)
                {
                    // ignore: the widget hung up, which is how every stream to it ends.
                }
                catch (SocketException)
                {
                    // ignore: as above, reported by the socket rather than the stream.
                }
            }
        }

        private static async Task SkipRequestAsync(NetworkStream stream)
        {
            var buffer = new byte[4096];
            var seen = new StringBuilder();

            while (!seen.ToString().Contains("\r\n\r\n", StringComparison.Ordinal) && seen.Length < 16 * 1024)
            {
                var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                seen.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }
        }
    }

    /// <summary>A broadcast test card — colour bars over a grey ramp, with a block that moves per frame.</summary>
    private static class TestCard
    {
        private const int Width = 640;
        private const int Height = 360;

        private static readonly (byte R, byte G, byte B)[] Bars =
        [
            (192, 192, 192), (192, 192, 0), (0, 192, 192), (0, 192, 0), (192, 0, 192), (192, 0, 0), (0, 0, 192),
        ];

        public static async Task<List<byte[]>> RenderFramesAsync(int count)
        {
            var frames = new List<byte[]>(count);

            for (var i = 0; i < count; i++)
            {
                frames.Add(await EncodeAsync(Draw(i, count)).ConfigureAwait(false));
            }

            return frames;
        }

        private static byte[] Draw(int index, int count)
        {
            var pixels = new byte[Width * Height * 4];
            var barsBottom = Height * 2 / 3;
            var block = Width / count;

            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    byte r, g, b;

                    if (y < barsBottom)
                    {
                        (r, g, b) = Bars[Math.Min(Bars.Length - 1, x * Bars.Length / Width)];
                    }
                    else if (x / block == index && y > barsBottom + 20 && y < Height - 20)
                    {
                        (r, g, b) = (240, 240, 240);
                    }
                    else
                    {
                        var v = (byte)(16 + (x * 200 / Width));
                        (r, g, b) = (v, v, v);
                    }

                    var at = ((y * Width) + x) * 4;
                    pixels[at] = b;
                    pixels[at + 1] = g;
                    pixels[at + 2] = r;
                    pixels[at + 3] = 255;
                }
            }

            return pixels;
        }

        private static async Task<byte[]> EncodeAsync(byte[] pixels)
        {
            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, Width, Height, 96, 96, pixels);
            await encoder.FlushAsync();

            var bytes = new byte[stream.Size];
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);
            return bytes;
        }
    }
}
#endif
