
using Themia.Core.Localization;

namespace Themia.Core.Widgets;

/// <summary>[S-1170] What the last probe said about the host.</summary>
public enum PingStatus
{
    /// <summary>No probe has come back yet.</summary>
    Checking = 0,

    /// <summary>The last probe was answered.</summary>
    Online = 1,

    /// <summary>The last probe timed out, was refused or could not be resolved.</summary>
    Offline = 2,
}

/// <summary>
/// [S-1171] The figures behind a run of probes. Every figure is null until it means something:
/// <see cref="Loss"/> until a probe has come back at all, the others until one has been answered.
/// </summary>
/// <param name="Samples">Probes in the history, answered or not.</param>
/// <param name="Answered">Of those, how many were answered.</param>
/// <param name="Loss">Unanswered share, 0..1.</param>
/// <param name="Jitter">Mean absolute difference between consecutive answers, in ms.</param>
/// <param name="Min">Fastest answer, in ms.</param>
/// <param name="Average">Mean answer, in ms.</param>
/// <param name="Max">Slowest answer, in ms.</param>
public readonly record struct PingSummary(
    int Samples,
    int Answered,
    double? Loss,
    double? Jitter,
    double? Min,
    double? Average,
    double? Max);

/// <summary>
/// [S-1170]..[S-1172] The ping meter's history and the statistics the original drew under its
/// latency. Pure, so the edge cases (gaps, a host that never answered, a single answer) are tested.
/// </summary>
public static class PingStats
{
    /// <summary>
    /// [S-1171] Summarise a history, oldest first, where null is a probe that was not answered.
    /// <para>
    /// Jitter compares consecutive <i>answers</i>: an unanswered probe drops out rather than breaking
    /// the series, as the original did, so two answers either side of a gap are compared. One answer
    /// has a jitter of 0 — nothing has varied yet — rather than none.
    /// </para>
    /// <para>
    /// Loss is the unanswered share of the whole history. A host that has <b>never</b> answered is
    /// 100% loss: the original short-circuited that case to 0%, which read as a perfect line to a
    /// host that was not there.
    /// </para>
    /// </summary>
    public static PingSummary Summarise(IEnumerable<double?> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var count = 0;
        var answered = 0;
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        var sum = 0.0;
        var swing = 0.0;
        double? previous = null;

        foreach (var sample in samples)
        {
            count++;

            if (sample is not { } ms || !double.IsFinite(ms) || ms < 0)
            {
                continue;
            }

            answered++;
            sum += ms;
            min = Math.Min(min, ms);
            max = Math.Max(max, ms);

            if (previous is { } last)
            {
                swing += Math.Abs(ms - last);
            }

            previous = ms;
        }

        if (count == 0)
        {
            return new PingSummary(0, 0, null, null, null, null, null);
        }

        var loss = (count - answered) / (double)count;

        if (answered == 0)
        {
            return new PingSummary(count, 0, loss, null, null, null, null);
        }

        var jitter = answered > 1 ? swing / (answered - 1) : 0;
        return new PingSummary(count, answered, loss, jitter, min, sum / answered, max);
    }

    /// <summary>[S-1170] The status the last probe gives: checking before any, then online or offline.</summary>
    public static PingStatus Status(bool anyProbe, bool lastAnswered)
        => !anyProbe ? PingStatus.Checking : lastAnswered ? PingStatus.Online : PingStatus.Offline;

    /// <summary>[S-1170] The word under the reading.</summary>
    public static string StatusText(PingStatus status) => StatusText(Strings.Current, status);

    /// <summary><see cref="StatusText(PingStatus)"/> in <paramref name="locale"/>.</summary>
    public static string StatusText(Locale locale, PingStatus status) => Strings.Get(locale, status switch
    {
        PingStatus.Online => Str.WidgetsB_Ping_Online,
        PingStatus.Offline => Str.WidgetsB_Ping_Offline,
        _ => Str.WidgetsB_Ping_Checking,
    });

    /// <summary>The longest <see cref="StatusText(PingStatus)"/> in the current language, so the caption is placed for its worst case.</summary>
    public static int StatusCharacters
        => Math.Max(StatusText(PingStatus.Online).Length, Math.Max(StatusText(PingStatus.Offline).Length, StatusText(PingStatus.Checking).Length));

    /// <summary>[S-1172] What the meter's colour says: success while answering, critical while not.</summary>
    public static MeterPressure Pressure(PingStatus status) => status switch
    {
        PingStatus.Online => MeterPressure.Healthy,
        PingStatus.Offline => MeterPressure.Critical,
        _ => MeterPressure.Normal,
    };

    /// <summary>[S-1171] "38 of 40 answered".</summary>
    public static string AnsweredText(PingSummary summary) => AnsweredText(Strings.Current, summary);

    /// <summary><see cref="AnsweredText(PingSummary)"/> in <paramref name="locale"/>.</summary>
    public static string AnsweredText(Locale locale, PingSummary summary)
        => Strings.Format(locale, Str.WidgetsB_Ping_Answered, ("answered", summary.Answered), ("samples", summary.Samples));

    /// <summary>[S-1171] "12 · 15 · 30 ms": fastest, mean, slowest; null before any answer.</summary>
    public static string? RangeText(PingSummary summary) => RangeText(Strings.Current, summary);

    /// <summary><see cref="RangeText(PingSummary)"/> in <paramref name="locale"/>.</summary>
    public static string? RangeText(Locale locale, PingSummary summary)
    {
        if (summary is not { Min: { } min, Average: { } average, Max: { } max })
        {
            return null;
        }

        // One unit at the end when all three are in milliseconds: three "ms" in a row is noise.
        return max < 1000
            ? $"{Whole(locale, min)} · {Whole(locale, average)} · {Formatting.Latency(locale, max)}"
            : $"{Formatting.Latency(locale, min)} · {Formatting.Latency(locale, average)} · {Formatting.Latency(locale, max)}";
    }

    private static string Whole(Locale locale, double ms) => LocaleFormat.Number(locale, Math.Round(ms), 0);
}

/// <summary>
/// [S-1171] The probes behind the statistics: answered latencies and unanswered gaps, oldest
/// first, as many as the chart spans. One per ping widget; touched only on the UI thread.
/// </summary>
public sealed class PingHistory
{
    private readonly Queue<double?> _samples = new();
    private int _capacity;

    public PingHistory(int capacity) => _capacity = Math.Max(1, capacity);

    /// <summary>How many probes are kept. Shrinking drops the oldest.</summary>
    public int Capacity
    {
        get => _capacity;
        set
        {
            _capacity = Math.Max(1, value);
            Trim();
        }
    }

    public int Count => _samples.Count;

    /// <summary>Whether any probe has come back since the history last started.</summary>
    public bool Any => _samples.Count > 0;

    /// <summary>The newest probe: its latency, or null when it was not answered (or there is none).</summary>
    public double? Last { get; private set; }

    public IEnumerable<double?> Samples => _samples;

    /// <summary>Record one probe: a latency in ms, or null for no answer.</summary>
    public void Add(double? latency)
    {
        var value = latency is { } ms && double.IsFinite(ms) && ms >= 0 ? ms : (double?)null;
        _samples.Enqueue(value);
        Last = value;
        Trim();
    }

    public void Clear()
    {
        _samples.Clear();
        Last = null;
    }

    public PingSummary Summarise() => PingStats.Summarise(_samples);

    private void Trim()
    {
        while (_samples.Count > _capacity)
        {
            _samples.Dequeue();
        }
    }
}
