namespace Themia.Core.Diagnostics;

/// <summary>
/// [S-987] The records of this session, newest last, bounded: what the Logs page lists.
/// <para>
/// In memory rather than read back from the files, so the page costs no disk read and no parsing,
/// and shows exactly what was recorded (the same floor as the file). Older sessions are in the
/// files, which the page opens rather than re-exports.
/// </para>
/// </summary>
public sealed class LogBuffer
{
    /// <summary>Kept per session by default: plenty for "what just happened", small in memory.</summary>
    public const int DefaultCapacity = 500;

    private readonly LogRecord[] _ring;
    private readonly Lock _gate = new();
    private int _start;
    private int _count;
    private long _total;

    public LogBuffer(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _ring = new LogRecord[capacity];
    }

    /// <summary>
    /// A record was added or the buffer was cleared. Raised on the thread that logged, outside the
    /// lock; a listener marshals to its own thread.
    /// </summary>
    public event Action? Changed;

    public int Capacity => _ring.Length;

    /// <summary>Every record ever added, including the ones that have since rolled off.</summary>
    public long Total
    {
        get
        {
            lock (_gate)
            {
                return _total;
            }
        }
    }

    public void Add(LogRecord record)
    {
        lock (_gate)
        {
            var slot = (_start + _count) % _ring.Length;
            _ring[slot] = record;

            if (_count < _ring.Length)
            {
                _count++;
            }
            else
            {
                // Full: the oldest makes room.
                _start = (_start + 1) % _ring.Length;
            }

            _total++;
        }

        Changed?.Invoke();
    }

    /// <summary>The records held now, oldest first.</summary>
    public IReadOnlyList<LogRecord> Snapshot()
    {
        lock (_gate)
        {
            var copy = new LogRecord[_count];
            for (var i = 0; i < _count; i++)
            {
                copy[i] = _ring[(_start + i) % _ring.Length];
            }

            return copy;
        }
    }

    /// <summary>Empty the list. The files are not touched.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_ring);
            _start = 0;
            _count = 0;
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// [S-988] The Logs page's filter: at or above <paramref name="minimum"/>, from
    /// <paramref name="scope"/> when one is chosen, newest first, at most <paramref name="limit"/>.
    /// </summary>
    public static IReadOnlyList<LogRecord> Filter(IReadOnlyList<LogRecord> records, LogLevel minimum, string? scope, int limit)
    {
        ArgumentNullException.ThrowIfNull(records);

        var result = new List<LogRecord>(Math.Min(records.Count, Math.Max(0, limit)));
        for (var i = records.Count - 1; i >= 0 && result.Count < limit; i--)
        {
            var record = records[i];
            if (record.Level >= minimum && (string.IsNullOrEmpty(scope) || string.Equals(record.Scope, scope, StringComparison.Ordinal)))
            {
                result.Add(record);
            }
        }

        return result;
    }
}
