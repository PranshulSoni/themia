using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;

namespace Themia.App.Services.Web;

/// <summary>What one request came back as: a named status, the body if there is one, and why.</summary>
/// <param name="Status">The named outcome. <see cref="FetchStatus.Ok"/> means <paramref name="Body"/> is usable.</param>
/// <param name="Body">The response text. Empty unless the status is Ok.</param>
/// <param name="Detail">One line for the log: an HTTP status line, a socket error, a timeout.</param>
internal readonly record struct WebResponse(FetchStatus Status, string Body, string? Detail);

/// <summary>
/// The one HTTP client the whole app shares.
/// <para>
/// One, not one per widget and certainly not one per request. A fresh <see cref="HttpClient"/> per
/// call is the classic way to exhaust the machine's ephemeral ports: each one opens its own
/// connection and leaves the socket in TIME_WAIT for a couple of minutes afterwards, so a desktop
/// with four network widgets refreshing on a timer will, given a few days, stop being able to open
/// a socket at all — and the symptom is every application on the machine failing, not this one.
/// </para>
/// <para>
/// Everything a public API is entitled to expect is set here once: a real user agent rather than
/// the framework default (several of these hosts answer 403 to a blank one), compression, a
/// connect timeout well short of the request timeout, and a pooled connection lifetime so a
/// long-lived process still notices DNS moving.
/// </para>
/// </summary>
internal static class WebFetch
{
    private const string Scope = "web";

    /// <summary>
    /// Identifies the app to the services it uses. Public APIs ask for this, some of them enforce
    /// it, and a host that wants to complain about this app's traffic deserves to know who to
    /// complain to.
    /// </summary>
    private const string UserAgent = "Themia/1.0 (Windows desktop widgets; +https://themia.app)";

    /// <summary>
    /// A widget refreshes on a timer, so the honest deadline is short: a request that has not
    /// answered in this long has nothing to add that the next tick will not get sooner.
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// A ceiling on what is read into memory, applied to the stream rather than to a
    /// <c>Content-Length</c> a host is free to lie about or omit. Feeds are a few hundred kilobytes
    /// and a ten-year chart is under a megabyte; past this, the reply is not the document asked for.
    /// </summary>
    private const int MaxBytes = 4 * 1024 * 1024;

    private static readonly HttpClient Client = Create();

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,

            // A dead host should be reported as unreachable in seconds, not held open until the
            // whole-request deadline.
            ConnectTimeout = TimeSpan.FromSeconds(8),

            // A process that lives for weeks would otherwise pin a connection to an IP address
            // that DNS stopped pointing at days ago.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),

            // A feed that redirects is normal; a feed that redirects nine times is a loop.
            MaxAutomaticRedirections = 5,
            AllowAutoRedirect = true,
        };

        var client = new HttpClient(handler) { Timeout = RequestTimeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }

    /// <summary>
    /// Fetch a URL as text, mapping every way it can go wrong onto a named
    /// <see cref="FetchStatus"/>.
    /// <para>
    /// This never throws for a network reason. A widget's refresh path has nowhere to put an
    /// exception except a crash, and "the wifi is off" is a state of a laptop rather than a fault.
    /// Cancellation is the one thing that does propagate, because a cancelled request is one whose
    /// answer nobody is waiting for.
    /// </para>
    /// </summary>
    public static async Task<WebResponse> GetAsync(Uri uri, string accept, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(uri);

        // The last line of defence for the trust boundary. Every caller builds its URI from
        // checked pieces, but this is the single place every request goes through, and a scheme
        // check here cannot be forgotten by a future caller.
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            Log.Warn(Scope, $"refused a non-http request to '{uri.Scheme}:'");
            return new WebResponse(FetchStatus.Refused, string.Empty, $"scheme {uri.Scheme}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        if (!string.IsNullOrEmpty(accept))
        {
            request.Headers.Accept.ParseAdd(accept);
        }

        try
        {
            using var response = await Client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                // Its own status, because it is the one failure whose correct response is to stop
                // asking for a long time rather than to try again on the next tick.
                return new WebResponse(FetchStatus.RateLimited, string.Empty, "HTTP 429 Too Many Requests");
            }

            if (!response.IsSuccessStatusCode)
            {
                // The status line, not the body: a 403 challenge page or a 404 HTML document would
                // otherwise be handed to the parser and reported as "not a feed", which sends the
                // reader looking in the wrong place.
                var line = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd();
                return new WebResponse(FetchStatus.BadResponse, string.Empty, line);
            }

            var body = await ReadAsync(response, token).ConfigureAwait(false);

            return body is null
                ? new WebResponse(FetchStatus.BadResponse, string.Empty, $"reply larger than {MaxBytes} bytes")
                : new WebResponse(FetchStatus.Ok, body, null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The caller moved on — a config change, a torn-down widget. Not a network state.
            throw;
        }
        catch (OperationCanceledException)
        {
            // Cancelled without the caller asking is HttpClient's own deadline.
            return new WebResponse(FetchStatus.TimedOut, string.Empty, $"no reply within {RequestTimeout.TotalSeconds:F0}s");
        }
        catch (HttpRequestException ex)
        {
            return new WebResponse(Classify(ex), string.Empty, Describe(ex));
        }
        catch (IOException ex)
        {
            // A connection dropped mid-body.
            return new WebResponse(FetchStatus.Unreachable, string.Empty, ex.Message);
        }
    }

    /// <summary>
    /// Fetch and deserialize through a source-generated contract.
    /// <para>
    /// The <see cref="JsonTypeInfo{T}"/> parameter is what keeps this AOT-safe: there is no
    /// overload here that takes a <see cref="Type"/> and looks it up by reflection, because that
    /// overload is trimmed away in the published build and fails at runtime rather than at build.
    /// </para>
    /// </summary>
    public static async Task<(FetchStatus Status, T? Value, string? Detail)> GetJsonAsync<T>(
        Uri uri,
        JsonTypeInfo<T> contract,
        CancellationToken token)
        where T : class
    {
        var response = await GetAsync(uri, "application/json", token).ConfigureAwait(false);

        if (response.Status != FetchStatus.Ok)
        {
            return (response.Status, null, response.Detail);
        }

        try
        {
            var value = JsonSerializer.Deserialize(response.Body, contract);

            // "null" is valid JSON and a perfectly good reply; it is not a payload.
            return value is null
                ? (FetchStatus.BadResponse, null, "the reply held no object")
                : (FetchStatus.Ok, value, null);
        }
        catch (JsonException ex)
        {
            // The service answered with something that is not its own schema — an error document,
            // an HTML interstitial, a truncated body. Named separately from "did not answer".
            return (FetchStatus.BadResponse, null, ex.Message);
        }
    }

    /// <summary>
    /// Read the body, refusing to grow past <see cref="MaxBytes"/>.
    /// <para>
    /// Bounded while reading rather than after: a host that streams forever would otherwise be
    /// stopped only by the request deadline, having already put whatever it sent into this
    /// process's memory.
    /// </para>
    /// </summary>
    private static async Task<string?> ReadAsync(HttpResponseMessage response, CancellationToken token)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream();

        var chunk = new byte[16 * 1024];

        while (true)
        {
            var read = await stream.ReadAsync(chunk, token).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > MaxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        // UTF-8 with replacement, not with an exception: a feed declaring one encoding and serving
        // another is common, the app builds with InvariantGlobalization so most legacy code pages
        // are not available to decode it with anyway, and a replacement character in one headline
        // is a far better outcome than the whole widget reporting a failure.
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
            .GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>
    /// Tell "this machine has no network" apart from "that host did not answer".
    /// <para>
    /// Worth the extra call because they are different facts and the reader can act on one of them.
    /// Offline is the laptop's state and the widget is blameless; unreachable means the network is
    /// up and the service specifically is not, which is the one that is worth looking into.
    /// </para>
    /// </summary>
    private static FetchStatus Classify(HttpRequestException ex)
    {
        if (!NetworkInterface.GetIsNetworkAvailable())
        {
            return FetchStatus.Offline;
        }

        if (ex.InnerException is SocketException socket)
        {
            return socket.SocketErrorCode switch
            {
                SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => FetchStatus.Unreachable,
                SocketError.NetworkDown or SocketError.NetworkUnreachable => FetchStatus.Offline,
                SocketError.TimedOut => FetchStatus.TimedOut,
                _ => FetchStatus.Unreachable,
            };
        }

        return FetchStatus.Unreachable;
    }

    private static string Describe(HttpRequestException ex)
        => ex.InnerException is SocketException socket
            ? $"{socket.SocketErrorCode}: {socket.Message}"
            : ex.Message;
}
