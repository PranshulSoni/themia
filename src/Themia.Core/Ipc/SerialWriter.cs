namespace Themia.Core.Ipc;

/// <summary>
/// Lets several callers write to one line-delimited stream without corrupting it.
/// <para>
/// A <c>StreamWriter</c> is not re-entrant: a second <c>WriteAsync</c> entered while the first is
/// still in flight throws <i>"the stream is currently in use by a previous operation"</i>, and on a
/// protocol where a message is a line, a half-written line desynchronises the peer for the rest of
/// the connection — every subsequent reply is parsed against the wrong request. That is not a
/// hypothetical: it appeared the moment four widgets began sharing one hub connection, having been
/// invisible for as long as a single widget was the only caller.
/// </para>
/// <para>
/// The gate covers the <b>write</b> and nothing else. A caller waiting for its reply must not hold
/// it: replies are correlated by id and arrive independently, so holding it across a round trip
/// would make N independent requests take as long as their sum and let one slow reply stall every
/// other caller.
/// </para>
/// </summary>
public sealed class SerialWriter : IDisposable
{
    private readonly Func<string, Task> _write;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <param name="write">Writes one whole message. Called one at a time, never concurrently.</param>
    public SerialWriter(Func<string, Task> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        _write = write;
    }

    /// <summary>Write one message, waiting for any write already in flight to finish first.</summary>
    public async Task WriteAsync(string message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            await _write(message).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }
}
