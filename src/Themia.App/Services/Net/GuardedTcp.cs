using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using Themia.App.Services.Web;
using Themia.Core.Net;
using Themia.Core.Widgets;

namespace Themia.App.Services.Net;

/// <summary>A proxy that would not, or could not, open a tunnel. The message is the sentence to show.</summary>
internal sealed class ProxyTunnelException : IOException
{
    public ProxyTunnelException(string message)
        : base(message)
    {
    }

    public ProxyTunnelException()
        : this("The proxy did not open a tunnel.")
    {
    }

    public ProxyTunnelException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// [S-960]/[S-964] The one way this app opens a raw TLS connection (IMAP, for adding an account and
/// for every sync). Same guard as HTTP: direct connections go through
/// <see cref="NetGuard.OpenSocketAsync"/>, so the address the server name resolved to is checked
/// at connect, every connect — not once when the account was added. Behind an HTTP proxy the
/// connection is a <c>CONNECT</c> tunnel (classic PX-08), the proxy is not judged and the
/// destination is (NG-07).
/// </summary>
internal static class GuardedTcp
{
    /// <summary>
    /// Connect, tunnel if Windows says so, and set up TLS with the app's floor. Throws
    /// <see cref="NetRefusedException"/> for a guard refusal, <see cref="ProxyTunnelException"/> for a
    /// proxy that would not tunnel, and the usual socket / IO / authentication exceptions otherwise.
    /// </summary>
    public static async Task<SslStream> OpenTlsAsync(string host, int port, CancellationToken token)
    {
        var stream = await OpenAsync(host, port, token).ConfigureAwait(false);
        var tls = new SslStream(stream, leaveInnerStreamOpen: false);

        try
        {
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = GuardedHttp.TlsFloor,
            }, token).ConfigureAwait(false);

            return tls;
        }
        catch (Exception)
        {
            // The stream is ours until it is handed back; a failed handshake must not leak the socket.
            await tls.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<Stream> OpenAsync(string host, int port, CancellationToken token)
    {
        var question = ConnectRules.TunnelQuestion(host, port)
            ?? throw new NetRefusedException(host, $"{host}:{port} is not a server address");

        var proxy = NetGuard.ProxyFor(question);

        switch (ConnectRules.Classify(proxy, question))
        {
            case ProxyRoute.Direct:
                var direct = await NetGuard.OpenSocketAsync(host, port, NetReach.Internet, token).ConfigureAwait(false);
                return new NetworkStream(direct, ownsSocket: true);

            case ProxyRoute.Unsupported:
                // Said out loud rather than silently connecting direct past the user's proxy.
                throw new ProxyTunnelException($"The system proxy ({proxy!.Scheme}://{proxy.Authority}) cannot carry mail; only an HTTP proxy can.");
        }

        var verdict = await NetGuard.CheckDestinationAsync(question, proxied: true, token).ConfigureAwait(false);
        if (!verdict.Allowed)
        {
            throw new NetRefusedException(host, verdict.Reason ?? $"{host} was refused");
        }

        var socket = await NetGuard.OpenProxySocketAsync(proxy!.IdnHost, proxy.Port, token).ConfigureAwait(false);
        var stream = new NetworkStream(socket, ownsSocket: true);

        try
        {
            await TunnelAsync(stream, question.IdnHost, port, proxy, token).ConfigureAwait(false);
            return stream;
        }
        catch (Exception)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Ask the proxy for a tunnel and read its answer one byte at a time to the blank line — no
    /// further, because anything past it is the server's first TLS bytes. The connection's deadline
    /// is the caller's token; nothing here outlives the handshake.
    /// </summary>
    private static async Task TunnelAsync(NetworkStream stream, string host, int port, Uri proxy, CancellationToken token)
    {
        var request = Encoding.ASCII.GetBytes(ProxyTunnel.Request(host, port, GuardedHttp.UserAgent));
        await stream.WriteAsync(request, token).ConfigureAwait(false);

        var read = new byte[ProxyTunnel.MaxHeaderBytes];
        var count = 0;

        while (!ProxyTunnel.IsHeaderEnd(read.AsSpan(0, count)))
        {
            if (count == read.Length)
            {
                throw new ProxyTunnelException($"The proxy at {proxy.Authority} sent an answer too long to be a tunnel reply.");
            }

            if (await stream.ReadAsync(read.AsMemory(count, 1), token).ConfigureAwait(false) == 0)
            {
                throw new ProxyTunnelException($"The proxy at {proxy.Authority} closed the connection instead of opening a tunnel to {host}:{port}.");
            }

            count++;
        }

        var (reply, _, line) = ProxyTunnel.Parse(Encoding.ASCII.GetString(read, 0, count));

        switch (reply)
        {
            case TunnelReply.Open:
                return;

            case TunnelReply.AuthenticationRequired:
                throw new ProxyTunnelException($"The proxy at {proxy.Authority} requires authentication, which Themia cannot provide for mail.");

            case TunnelReply.Refused:
                throw new ProxyTunnelException($"The proxy at {proxy.Authority} would not open a tunnel to {host}:{port} ({line}).");

            default:
                throw new ProxyTunnelException($"The proxy at {proxy.Authority} did not answer as an HTTP proxy.");
        }
    }
}
