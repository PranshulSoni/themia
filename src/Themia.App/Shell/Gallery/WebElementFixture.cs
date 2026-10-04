#if DEBUG
using System.Net;
using System.Net.Sockets;
using System.Text;
using Themia.App.Services.Web;
using Themia.Core.Diagnostics;
using Themia.Core.Model;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-1319] The web element's gallery fixture: a page served on loopback, so the populated render is
/// offline and the same on every run (it was httpbin.org, which made the gallery depend on the
/// network and on someone else's page).
/// <para>
/// Loopback is exactly what the network guard ([S-1314]) refuses, so installing this fixture turns
/// the guard's "local network access" switch on for the rest of the gallery run. Debug builds only,
/// and the gallery never reads or writes the user's settings, so the real switch is untouched.
/// </para>
/// </summary>
internal static class WebElementFixture
{
    private const string Scope = "gallery";

    /// <summary>The pinned element: a status card of a few lines, wider than tall, like most things worth pinning.</summary>
    private const string Selector = "#status";

    // No line of this page may start with '#': in a Release build the file is a skipped #if DEBUG
    // region, where such a line is read as a preprocessor directive (CS1024). Hence div#status.
    private const string Page = """
        <!doctype html>
        <html><head><meta charset="utf-8"><title>Themia fixture</title>
        <style>
          body { margin: 0; padding: 40px; font-family: 'Segoe UI', sans-serif; background: #f3f3f3; color: #1b1b1b; }
          nav { height: 48px; background: #20232a; margin: -40px -40px 32px; }
          div#status { width: 340px; padding: 16px 20px; background: #fff; border-radius: 8px; box-shadow: 0 1px 3px rgba(0,0,0,.18); }
          div#status h2 { margin: 0 0 6px; font-size: 14px; font-weight: 600; color: #5f5f5f; }
          div#status .value { font-size: 34px; font-weight: 600; color: #0f7b0f; }
          div#status table { width: 100%; margin-top: 10px; border-collapse: collapse; font-size: 13px; }
          div#status td { padding: 3px 0; border-top: 1px solid #e5e5e5; }
          div#status td:last-child { text-align: right; font-variant-numeric: tabular-nums; }
          p { max-width: 640px; line-height: 1.5; }
        </style></head>
        <body>
          <nav></nav>
          <div id="status">
            <h2>Main build</h2>
            <div class="value">Passing</div>
            <table>
              <tr><td>Tests</td><td>1,284 / 1,284</td></tr>
              <tr><td>Duration</td><td>6 min 12 s</td></tr>
              <tr><td>Coverage</td><td>87.4 %</td></tr>
            </table>
          </div>
          <p>Everything else on this page is what the pinned element is cut out of.</p>
        </body></html>
        """;

    private static TcpListener? _listener;
    private static string? _url;

    public static WebElementConfig? Config()
    {
        if (Start() is not { } url)
        {
            return null;
        }

        // The fixture page is on loopback: let the gallery's renders through the guard.
        NetGuard.AllowPrivate = static () => true;
        return new WebElementConfig { Url = url, Selector = Selector, PickedLabel = "Main build" };
    }

    private static string? Start()
    {
        if (_url is not null)
        {
            return _url;
        }

        try
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            _listener = listener;
            _url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/status";
            _ = AcceptLoopAsync(listener);
            return _url;
        }
        catch (SocketException ex)
        {
            Log.Error(Scope, "could not start the fixture web page", ex);
            return null;
        }
    }

    private static async Task AcceptLoopAsync(TcpListener listener)
    {
        var body = Encoding.UTF8.GetBytes(Page);
        var head = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");

        while (true)
        {
            TcpClient client;

            try
            {
                client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                Log.Warn(Scope, "the fixture web page stopped accepting", ex);
                return;
            }

            _ = ServeAsync(client, head, body);
        }
    }

    private static async Task ServeAsync(TcpClient client, byte[] head, byte[] body)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
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

                // Every path gets the page; a favicon request getting HTML is harmless here.
                await stream.WriteAsync(head).ConfigureAwait(false);
                await stream.WriteAsync(body).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // ignore: the browser hung up early, which it may do on any request.
            }
            catch (SocketException)
            {
                // ignore: as above, reported by the socket rather than the stream.
            }
        }
    }
}
#endif
