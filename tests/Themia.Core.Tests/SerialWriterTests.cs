using Themia.Core.Ipc;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// Regression for the hub write collision: once mail, calendar, tasks and contacts shared one
/// connection, concurrent queries entered the same <c>StreamWriter</c> and it threw
/// <i>"the stream is currently in use by a previous operation on the stream"</i>. It was invisible
/// for as long as a single widget was the only caller.
/// </summary>
public sealed class SerialWriterTests
{
    /// <summary>
    /// A stand-in for the underlying stream that refuses to be entered twice, exactly as a
    /// <c>StreamWriter</c> does, and takes long enough that an unguarded caller would overlap.
    /// </summary>
    private sealed class ReentrancyDetectingSink
    {
        private int _inFlight;

        public int Collisions { get; private set; }

        public List<string> Written { get; } = [];

        public async Task WriteAsync(string message)
        {
            if (Interlocked.Increment(ref _inFlight) != 1)
            {
                Collisions++;
            }

            try
            {
                // Two awaits, so an overlapping caller has somewhere to interleave.
                await Task.Yield();
                await Task.Delay(1).ConfigureAwait(false);

                lock (Written)
                {
                    Written.Add(message);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }

    /// <summary>
    /// Proves the test above is actually testing something: driven concurrently <b>without</b> the
    /// writer, the same sink collides. Otherwise a guard that had quietly stopped working would go
    /// on passing.
    /// </summary>
    [Fact]
    public async Task S98_TheSinkDoesCollideWhenNothingSerialisesIt()
    {
        var sink = new ReentrancyDetectingSink();

        await Task.WhenAll(Enumerable.Range(0, 64).Select(i => sink.WriteAsync($"line-{i}\n")));

        Assert.True(sink.Collisions > 0, "the sink must collide unguarded, or the guard proves nothing");
    }

    [Fact]
    public async Task S98_ConcurrentWritesNeverOverlap()
    {
        var sink = new ReentrancyDetectingSink();
        using var writer = new SerialWriter(sink.WriteAsync);

        await Task.WhenAll(Enumerable.Range(0, 64).Select(i => writer.WriteAsync($"line-{i}\n")));

        Assert.Equal(0, sink.Collisions);
        Assert.Equal(64, sink.Written.Count);
    }

    /// <summary>Serialising must not lose or merge anything: every line arrives, once, whole.</summary>
    [Fact]
    public async Task S98_EveryMessageArrivesExactlyOnceAndIntact()
    {
        var sink = new ReentrancyDetectingSink();
        using var writer = new SerialWriter(sink.WriteAsync);

        var expected = Enumerable.Range(0, 40).Select(i => $"{{\"id\":{i}}}\n").ToArray();
        await Task.WhenAll(expected.Select(writer.WriteAsync));

        Assert.Equal(expected.Order(), sink.Written.Order());
    }

    /// <summary>
    /// A failing write must release the gate, or one broken request wedges the connection for every
    /// later caller.
    /// </summary>
    [Fact]
    public async Task S98_AFailedWriteDoesNotWedgeTheGate()
    {
        var calls = 0;
        using var writer = new SerialWriter(_ =>
        {
            calls++;
            return calls == 1 ? Task.FromException(new IOException("pipe broken")) : Task.CompletedTask;
        });

        await Assert.ThrowsAsync<IOException>(() => writer.WriteAsync("first"));

        // Would hang rather than fail if the gate had leaked.
        await writer.WriteAsync("second").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task S98_WritingAfterDisposeIsRefusedRatherThanIgnored()
    {
        var writer = new SerialWriter(_ => Task.CompletedTask);
        writer.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => writer.WriteAsync("after"));
    }

    [Fact]
    public void S98_DisposeIsIdempotent()
    {
        var writer = new SerialWriter(_ => Task.CompletedTask);
        writer.Dispose();
        writer.Dispose();
    }
}
