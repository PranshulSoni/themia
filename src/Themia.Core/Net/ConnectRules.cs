using System.Globalization;
using System.Net;
using System.Text;
using Themia.Core.Widgets;

namespace Themia.Core.Net;

/// <summary>How the system proxy says one destination is reached.</summary>
public enum ProxyRoute
{
    /// <summary>No proxy: the app connects to the destination itself, and checks the address it connects to.</summary>
    Direct = 0,

    /// <summary>Through an HTTP proxy, which resolves the destination and is asked with <c>CONNECT</c> for raw TCP.</summary>
    Http,

    /// <summary>A proxy this app cannot speak (SOCKS): refused out loud rather than silently connecting direct.</summary>
    Unsupported,
}

/// <summary>
/// [S-963] The pure half of the one connect path: which connection is to a proxy, how the system
/// proxy's answer reads, and the address question an IMAP server is put to the proxy as.
/// </summary>
public static class ConnectRules
{
    /// <summary>
    /// Whether the connection the HTTP stack is opening goes to a proxy rather than to the request's
    /// own host. The stack opens a proxied request's connection to the proxy's endpoint; any other
    /// endpoint than the request's own authority is therefore the proxy.
    /// <para>
    /// [PX-07] The proxy's address is never checked by the guard — a corporate proxy on 10.x is the
    /// overwhelmingly common case — so this is the question that decides what gets checked.
    /// </para>
    /// </summary>
    public static bool IsProxyHop(string requestHost, int requestPort, string endpointHost, int endpointPort)
        => requestPort != endpointPort || !SameHost(requestHost, endpointHost);

    /// <summary>Host names compared without case and without IPv6 brackets; IP literals by value.</summary>
    public static bool SameHost(string a, string b)
    {
        var x = Strip(a);
        var y = Strip(b);

        if (NetworkGuard.TryLiteral(x, out var ax) && NetworkGuard.TryLiteral(y, out var ay))
        {
            return ax.Equals(ay);
        }

        return string.Equals(x.TrimEnd('.'), y.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// [PX-08] The address an IMAP (or any raw TCP) service is put to the system proxy as:
    /// <c>https://host:port/</c>. A proxied raw connection is a CONNECT tunnel, which is exactly what
    /// https means to a proxy, so a PAC script gets a real host to branch on and a per-protocol manual
    /// setting contributes its https proxy — the one that can CONNECT. Null for a host no URI can hold.
    /// </summary>
    public static Uri? TunnelQuestion(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535)
        {
            return null;
        }

        var authority = Authority(host, port);
        return Uri.TryCreate("https://" + authority + "/", UriKind.Absolute, out var uri) ? uri : null;
    }

    /// <summary>
    /// How the system proxy's answer for <paramref name="destination"/> is to be read: no proxy, the
    /// destination itself, or a bypassed host is direct ([PX-03] bypass list, honoured by Windows);
    /// an http proxy is used; anything else (socks) is unsupported.
    /// </summary>
    public static ProxyRoute Classify(Uri? proxy, Uri destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (proxy is null || !proxy.IsAbsoluteUri || proxy == destination)
        {
            return ProxyRoute.Direct;
        }

        return proxy.Scheme == Uri.UriSchemeHttp ? ProxyRoute.Http : ProxyRoute.Unsupported;
    }

    /// <summary><c>host:port</c>, an IPv6 literal in brackets.</summary>
    public static string Authority(string host, int port)
    {
        var bare = Strip(host);
        var shown = bare.Contains(':') ? "[" + bare + "]" : bare;
        return shown + ":" + port.ToString(CultureInfo.InvariantCulture);
    }

    private static string Strip(string host) => (host ?? string.Empty).Trim().TrimStart('[').TrimEnd(']');
}

/// <summary>What a proxy answered to <c>CONNECT</c>.</summary>
public enum TunnelReply
{
    /// <summary>2xx: the tunnel is open and transparent from here.</summary>
    Open = 0,

    /// <summary>407: the proxy wants credentials, which Windows does not store for it.</summary>
    AuthenticationRequired,

    /// <summary>Any other status: the proxy's policy (many allow CONNECT to 443 only).</summary>
    Refused,

    /// <summary>Not an HTTP status line at all.</summary>
    Malformed,
}

/// <summary>
/// [S-964] The HTTP <c>CONNECT</c> handshake an IMAP connection makes through an HTTP proxy
/// (classic [PX-08]): the request, the end of the reply's header block, and its status line.
/// </summary>
public static class ProxyTunnel
{
    /// <summary>The most header bytes read before the reply is called malformed.</summary>
    public const int MaxHeaderBytes = 16 * 1024;

    /// <summary>The request, CRLF-terminated, with the Host header HTTP/1.1 requires.</summary>
    public static string Request(string host, int port, string userAgent)
    {
        var authority = ConnectRules.Authority(host, port);
        var builder = new StringBuilder(128)
            .Append("CONNECT ").Append(authority).Append(" HTTP/1.1\r\n")
            .Append("Host: ").Append(authority).Append("\r\n");

        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            builder.Append("User-Agent: ").Append(userAgent.Trim()).Append("\r\n");
        }

        return builder.Append("\r\n").ToString();
    }

    /// <summary>
    /// Whether the bytes read so far end the header block (a blank line). The reply is read one byte
    /// at a time up to here and no further: a larger read could swallow the first bytes the server
    /// sends through the tunnel and corrupt the TLS handshake that follows.
    /// </summary>
    public static bool IsHeaderEnd(ReadOnlySpan<byte> read)
        => read.EndsWith("\r\n\r\n"u8) || read.EndsWith("\n\n"u8);

    /// <summary>The verdict and the status line, from the header block.</summary>
    public static (TunnelReply Reply, int Status, string StatusLine) Parse(string headers)
    {
        var line = (headers ?? string.Empty).Split('\n', 2)[0].TrimEnd('\r').Trim();
        var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2
            || !parts[0].StartsWith("HTTP/", StringComparison.Ordinal)
            || parts[1].Length != 3
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var status))
        {
            return (TunnelReply.Malformed, 0, line);
        }

        var reply = status switch
        {
            >= 200 and < 300 => TunnelReply.Open,
            407 => TunnelReply.AuthenticationRequired,
            _ => TunnelReply.Refused,
        };

        return (reply, status, line);
    }
}
