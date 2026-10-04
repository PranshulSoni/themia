namespace Themia.Core.Widgets;

/// <summary>
/// The last reading of a rate that is computed as a delta between two samples, kept so that
/// several widgets asking in the same moment all get the same answer.
/// <para>
/// [S-643] CPU load and network throughput are each a difference between the counters now and the
/// counters at the previous read, and the previous read is shared by the whole process. With two
/// CPU widgets on the desktop the first one's read moved the baseline and the second, a millisecond
/// later, divided almost nothing by almost nothing: one tile said 31% and the other 0%, every
/// second, for ever. A read that lands within <see cref="MinimumWindowMs"/> of the last real one
/// is not a new measurement, so it is answered from here and the baseline is left alone.
/// </para>
/// </summary>
/// <typeparam name="T">The reading handed back to every caller.</typeparam>
public sealed class SampleCache<T>
    where T : struct
{
    /// <summary>
    /// The shortest interval that is still a measurement. Well under the fastest meter cadence
    /// (one second), well over the spread of one tick reaching every widget on the desktop.
    /// </summary>
    public const long MinimumWindowMs = 250;

    private T _value;
    private long _takenAt;
    private bool _has;

    /// <summary>
    /// The cached reading, when the last real one was taken less than
    /// <see cref="MinimumWindowMs"/> before <paramref name="nowMs"/>. A clock that reads earlier
    /// than the stored time is not trusted to say "recent", so it gets a fresh reading.
    /// </summary>
    public bool TryReuse(long nowMs, out T value)
    {
        var elapsed = nowMs - _takenAt;

        if (_has && elapsed >= 0 && elapsed < MinimumWindowMs)
        {
            value = _value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Remember a reading that was really measured at <paramref name="nowMs"/>.</summary>
    public void Store(long nowMs, T value)
    {
        _value = value;
        _takenAt = nowMs;
        _has = true;
    }

    /// <summary>Forget the reading, for a source whose baseline was thrown away.</summary>
    public void Clear() => _has = false;
}
