using System.Globalization;
using Themia.Core.Model;

namespace Themia.Core.Widgets;

/// <summary>What kind of thing a symbol names. Presentation only; it never selects an endpoint.</summary>
public enum AssetClass
{
    Equity = 0,
    Crypto,
    Currency,
    Commodity,
    Index,
}

/// <summary>Which session the listing exchange is in right now.</summary>
public enum MarketPhase
{
    /// <summary>The reply carried no usable schedule. Deliberately not the same as <see cref="Closed"/>.</summary>
    Unknown = 0,

    Pre,
    Open,
    Post,
    Closed,
}

/// <summary>A validated trading window, in epoch seconds, with <c>Start &lt; End</c>.</summary>
public readonly record struct SessionWindow(long Start, long End);

/// <summary>The day's sessions, each present only when the service sent a usable one.</summary>
/// <param name="Pre">Pre-market.</param>
/// <param name="Regular">The regular session. Its absence is what makes the phase unknowable.</param>
/// <param name="Post">After hours.</param>
public sealed record TradingSessions(SessionWindow? Pre, SessionWindow? Regular, SessionWindow? Post)
{
    public static TradingSessions None { get; } = new(null, null, null);
}

/// <summary>
/// Reading a quote: what the symbol is, how many decimals its price needs, and whether its exchange
/// is open.
/// <para>
/// All of it pure and all of it here, because all of it is arithmetic on numbers that arrived over
/// the wire from a service with no contract. The alternative — doing it inside the view — is how a
/// missing field becomes an exception on a render path.
/// </para>
/// </summary>
public static class MarketQuote
{
    /// <summary>
    /// Which kind of instrument a symbol names.
    /// <para>
    /// Two strengths of evidence, in order. A live search result carries the service's own quote
    /// type and it wins; a symbol restored from the state file carries nothing, so the fallback
    /// reads Yahoo's symbol grammar — <c>^</c> for an index, <c>=X</c> for a currency pair,
    /// <c>=F</c> for a futures contract. Which service quotes it (<paramref name="market"/>)
    /// outranks both: a coin id has no Yahoo grammar to read.
    /// </para>
    /// </summary>
    public static AssetClass Classify(string? symbol, QuoteMarket market, string? quoteType = null)
    {
        if (market == QuoteMarket.Crypto)
        {
            return AssetClass.Crypto;
        }

        switch ((quoteType ?? string.Empty).Trim().ToUpperInvariant())
        {
            case "CURRENCY":
                return AssetClass.Currency;
            case "FUTURE":
                return AssetClass.Commodity;
            case "INDEX":
                return AssetClass.Index;
            case "CRYPTOCURRENCY":
                return AssetClass.Crypto;
            case "EQUITY":
            case "ETF":
            case "MUTUALFUND":
                return AssetClass.Equity;
            default:
                break;
        }

        var text = (symbol ?? string.Empty).Trim().ToUpperInvariant();

        if (text.StartsWith('^'))
        {
            return AssetClass.Index;
        }

        if (text.EndsWith("=X", StringComparison.Ordinal))
        {
            return AssetClass.Currency;
        }

        if (text.EndsWith("=F", StringComparison.Ordinal))
        {
            return AssetClass.Commodity;
        }

        return AssetClass.Equity;
    }

    /// <summary>Decimals implied by size alone: two above a unit, four below it.</summary>
    private static int MagnitudeDecimals(double price) => Math.Abs(price) >= 1 ? 2 : 4;

    /// <summary>
    /// How many decimals to print a price with.
    /// <para>
    /// The venue's own tick precision — two for a share, four for an FX pair — is the only thing
    /// that tells EUR/USD at 1.1567 apart from a stock at $1.16, and printing the pair as "1.16"
    /// throws away the entire signal. It is a remote number, so it is validated, and it may only
    /// ever <i>raise</i> precision: a sub-dollar instrument reported with a hint of 2 keeps the four
    /// places the magnitude rule already gave it.
    /// </para>
    /// </summary>
    public static int PriceDecimals(double price, int? priceHint)
    {
        var baseline = MagnitudeDecimals(price);

        if (priceHint is not { } hint || hint is < 0 or > 8)
        {
            return baseline;
        }

        return Math.Max(baseline, hint);
    }

    /// <summary>A price with grouped thousands and the decimals <see cref="PriceDecimals"/> chose.</summary>
    public static string Price(double? price, int? priceHint = null)
    {
        if (price is not { } value || !double.IsFinite(value))
        {
            return "--";
        }

        var decimals = PriceDecimals(value, priceHint);
        return value.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A percentage move, always signed: <c>+1.24%</c>, <c>-0.30%</c>, <c>0.00%</c>.
    /// <para>
    /// The sign is carried in the text as well as in the colour, because a state a reader can only
    /// get from a colour is a state some readers do not get at all.
    /// </para>
    /// </summary>
    public static string ChangePercent(double? percent)
    {
        if (percent is not { } value || !double.IsFinite(value))
        {
            return "--";
        }

        var text = Math.Abs(value).ToString("F2", CultureInfo.InvariantCulture);
        var sign = value > 0 ? "+" : value < 0 ? "-" : string.Empty;
        return $"{sign}{text}%";
    }

    /// <summary>
    /// The percentage move from <paramref name="previousClose"/> to <paramref name="price"/>, or
    /// null when the baseline cannot carry one.
    /// </summary>
    public static double? PercentChange(double? price, double? previousClose)
    {
        if (price is not { } now || previousClose is not { } before)
        {
            return null;
        }

        if (!double.IsFinite(now) || !double.IsFinite(before) || before == 0)
        {
            return null;
        }

        var change = (now - before) / Math.Abs(before) * 100;
        return double.IsFinite(change) ? change : null;
    }

    /// <summary>
    /// A trading window, if the two numbers describe one.
    /// <para>
    /// Both bounds finite, and the end strictly after the start. Windows are validated
    /// independently by the caller so a broken after-hours window does not cost the regular one.
    /// </para>
    /// </summary>
    public static SessionWindow? Window(double? startEpochSeconds, double? endEpochSeconds)
    {
        if (startEpochSeconds is not { } start || endEpochSeconds is not { } end)
        {
            return null;
        }

        if (!double.IsFinite(start) || !double.IsFinite(end) || end <= start)
        {
            return null;
        }

        // Epoch seconds outside this range are not a schedule, they are a units mistake — a
        // millisecond timestamp, or a sentinel. Roughly 1970 to 2100.
        if (start is < 0 or > 4_102_444_800 || end is < 0 or > 4_102_444_800)
        {
            return null;
        }

        return new SessionWindow((long)start, (long)end);
    }

    /// <summary>
    /// Which session <paramref name="now"/> falls in.
    /// <para>
    /// Absolute instants only: the windows arrive already resolved for the exchange's own calendar,
    /// holidays and daylight saving, so there is no local time and no assumption that the reader
    /// sits in the exchange's zone. Windows are half-open, <c>[start, end)</c>, so the adjacent
    /// pre/regular/post bounds cannot both claim the same second.
    /// </para>
    /// <para>
    /// Without a usable regular window the answer is <see cref="MarketPhase.Unknown"/>, never
    /// <see cref="MarketPhase.Closed"/>. Saying "market closed" because the payload said nothing is
    /// the bug the original app filed: it showed a permanent closed badge on every symbol, all day,
    /// including mid-session.
    /// </para>
    /// </summary>
    public static MarketPhase Phase(TradingSessions? sessions, DateTimeOffset now)
    {
        if (sessions is null)
        {
            return MarketPhase.Unknown;
        }

        var seconds = now.ToUnixTimeSeconds();

        if (Contains(sessions.Regular, seconds))
        {
            return MarketPhase.Open;
        }

        if (Contains(sessions.Pre, seconds))
        {
            return MarketPhase.Pre;
        }

        if (Contains(sessions.Post, seconds))
        {
            return MarketPhase.Post;
        }

        // Knowing today's regular window is exactly what makes "closed" a statement rather than a
        // guess: outside it, and outside any extended window, nothing is trading.
        return sessions.Regular is not null ? MarketPhase.Closed : MarketPhase.Unknown;
    }

    /// <summary>The badge text for a phase, or empty when there is nothing worth saying.</summary>
    public static string PhaseLabel(MarketPhase phase) => phase switch
    {
        MarketPhase.Pre => "Pre-market",
        MarketPhase.Post => "After hours",
        MarketPhase.Closed => "Market closed",
        _ => string.Empty,
    };

    private static bool Contains(SessionWindow? window, long seconds)
        => window is { } w && seconds >= w.Start && seconds < w.End;

    /// <summary>
    /// Pair up the parallel timestamp and close arrays a chart endpoint returns, dropping any
    /// sample where either half is missing.
    /// <para>
    /// Both halves or neither. These services pad illiquid minutes with nulls, and dropping a close
    /// while keeping its timestamp shears the two arrays out of alignment for every later point —
    /// which draws a plausible-looking line that is wrong.
    /// </para>
    /// </summary>
    public static IReadOnlyList<double> Closes(IReadOnlyList<double?>? closes)
    {
        if (closes is null || closes.Count == 0)
        {
            return [];
        }

        var kept = new List<double>(closes.Count);

        foreach (var close in closes)
        {
            if (close is { } value && double.IsFinite(value))
            {
                kept.Add(value);
            }
        }

        return kept;
    }

    /// <summary>
    /// Where each close sits in 0..1 against the extremes of the series, for a sparkline.
    /// <para>
    /// Scaled to the series, not to zero: a share that spent the day between 181.20 and 182.40
    /// drawn against a zero baseline is a flat line, and the shape of the day is the only thing a
    /// sparkline has to say. A series with no spread draws down the middle rather than dividing by
    /// zero.
    /// </para>
    /// </summary>
    public static IReadOnlyList<double> Normalise(IReadOnlyList<double> closes)
    {
        if (closes.Count == 0)
        {
            return [];
        }

        var low = double.MaxValue;
        var high = double.MinValue;

        foreach (var close in closes)
        {
            low = Math.Min(low, close);
            high = Math.Max(high, close);
        }

        var span = high - low;
        var scaled = new double[closes.Count];

        for (var i = 0; i < closes.Count; i++)
        {
            scaled[i] = span > 0 ? (closes[i] - low) / span : 0.5;
        }

        return scaled;
    }
}
