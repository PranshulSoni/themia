using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Themia.Core.Accounts;
using Themia.Core.Diagnostics;
using Themia.Core.Localization;

namespace Themia.App.Services.Accounts;

/// <summary>
/// [S-527] The redirect a browser sign-in comes back to: a socket on the loopback, on a port Windows
/// picks, answering one request (RFC 8252 §7.3). Microsoft and Google both come back here.
/// <para>
/// A raw listener rather than <c>HttpListener</c>, because reserving an HTTP prefix on Windows wants
/// an administrator. [S-1784] It listens on 127.0.0.1 and, on the same port, on ::1: Microsoft's
/// redirect names <c>localhost</c>, which a browser may try on either address first. When ::1 cannot
/// be had (IPv6 off, the port taken there) it carries on with 127.0.0.1 alone — every browser falls
/// back to it. A request that is not the redirect — the browser's own favicon fetch — is answered
/// 404 and the listener keeps waiting; a redirect carrying the wrong state is refused, because
/// anything on this machine can connect to the port and only the browser this app opened knows the
/// state.
/// </para>
/// </summary>
internal sealed class Loopback : IDisposable
{
    private const string Scope = "accounts.loopback";

    private readonly TcpListener[] _listeners;

    public Loopback()
    {
        var v4 = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            v4.Start();
        }
        catch
        {
            v4.Dispose();
            throw;
        }

        Port = ((IPEndPoint)v4.LocalEndpoint).Port;

        var v6 = new TcpListener(IPAddress.IPv6Loopback, Port);
        try
        {
            v6.Start();
            _listeners = [v4, v6];
        }
        catch (SocketException ex)
        {
            Log.Info(Scope, $"listening on 127.0.0.1 only; ::1 is not available ({ex.SocketErrorCode})");
            v6.Dispose();
            _listeners = [v4];
        }
    }

    /// <summary>The port both addresses listen on; each provider builds its redirect URI from it.</summary>
    public int Port { get; }

    /// <summary>Wait for the browser to come back. Ends with a code, or throws with why not.</summary>
    public async Task<string> WaitForCodeAsync(string state, CancellationToken cancellation)
    {
        var accepts = _listeners.Select(l => l.AcceptTcpClientAsync(cancellation).AsTask()).ToArray();

        try
        {
            while (true)
            {
                var done = await Task.WhenAny(accepts).ConfigureAwait(false);
                var index = Array.IndexOf(accepts, done);
                using var client = await done.ConfigureAwait(false);
                accepts[index] = _listeners[index].AcceptTcpClientAsync(cancellation).AsTask();

                string line;
                try
                {
                    line = await ReadRequestLineAsync(client, cancellation).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException && !cancellation.IsCancellationRequested)
                {
                    // ignore: one bad or stalled connection is not the sign-in; keep listening.
                    Log.Warn(Scope, "a loopback connection could not be read", ex);
                    continue;
                }

                var redirect = OAuthRedirect.Parse(line, state);

                await AnswerAsync(client, redirect.Kind, cancellation).ConfigureAwait(false);

                switch (redirect.Kind)
                {
                    case RedirectKind.Unrelated:
                        continue;
                    case RedirectKind.Code:
                        return redirect.Code!;
                    case RedirectKind.ProviderError:
                        throw new AccountException(AccountError.Refused, redirect.Error ?? Str.Shell_Accounts_ProviderRefusedPlain.Text());
                    default:
                        throw new AccountException(AccountError.Refused,
                            Str.Shell_Accounts_ForeignRedirect.Text());
                }
            }
        }
        finally
        {
            foreach (var pending in accepts.Where(a => !a.IsCompleted))
            {
                // ignore: the accept still outstanding on the other address ends when the listener
                // stops (Dispose) or the wait is cancelled; its fault is expected and means nothing.
                _ = pending.ContinueWith(static t => _ = t.Exception, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }

    public void Dispose()
    {
        foreach (var listener in _listeners)
        {
            listener.Dispose();
        }
    }

    /// <summary>The request line and no further: everything the redirect carries is on it.</summary>
    private static async Task<string> ReadRequestLineAsync(TcpClient client, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));

        var stream = client.GetStream();
        var line = new StringBuilder();
        var buffer = new byte[1];

        while (line.Length < 8192)
        {
            var read = await stream.ReadAsync(buffer, deadline.Token).ConfigureAwait(false);
            if (read == 0 || buffer[0] == (byte)'\n')
            {
                break;
            }

            if (buffer[0] != (byte)'\r')
            {
                line.Append((char)buffer[0]);
            }
        }

        return line.ToString();
    }

    private static async Task AnswerAsync(TcpClient client, RedirectKind kind, CancellationToken cancellation)
    {
        var (status, heading, text) = kind switch
        {
            RedirectKind.Unrelated => ("404 Not Found", null, null),
            RedirectKind.Code => ("200 OK", Str.Shell_Accounts_PageSignedIn.Text(), Str.Shell_Accounts_PageSignedInBody.Text()),
            _ => ("200 OK", Str.Shell_Accounts_NotCompleted.Text(), Str.Shell_Accounts_PageFailedBody.Text()),
        };

        var body = heading is null
            ? string.Empty
            : $"<!doctype html><html lang=\"{LanguageResolver.Code(Strings.Current)}\"><head><meta charset=\"utf-8\"><title>Themia</title></head>"
                + "<body style=\"font-family:system-ui;margin:4rem auto;max-width:28rem\">"
                + $"<h1>{heading}</h1><p>{text}</p></body></html>";

        var bytes = Encoding.UTF8.GetBytes(body);
        var head = $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length.ToString(CultureInfo.InvariantCulture)}\r\nConnection: close\r\n\r\n";

        try
        {
            var stream = client.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancellation).ConfigureAwait(false);
            await stream.WriteAsync(bytes, cancellation).ConfigureAwait(false);
            await stream.FlushAsync(cancellation).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {
            // ignore: the page is a courtesy; the code has already been read.
            Log.Warn(Scope, "could not answer the browser on the loopback port", ex);
        }
    }
}
