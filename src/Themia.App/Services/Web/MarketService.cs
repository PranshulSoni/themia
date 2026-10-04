using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Services.Web;

/// <summary>One instrument's quote, already validated and reduced to what a widget draws.</summary>
/// <param name="Symbol">The symbol as asked for.</param>
/// <param name="Name">The service's own name for it, or empty.</param>
/// <param name="Price">The last price, in <paramref name="Currency"/>.</param>
/// <param name="ChangePercent">The move against the previous close, or null when there is no baseline.</param>
/// <param name="Currency">ISO code; USD for crypto, since that is what is asked for.</param>
/// <param name="PriceHint">The venue's tick precision, when it sent one.</param>
/// <param name="Closes">The sparkline series, oldest first, with every unusable sample dropped.</param>
/// <param name="Sessions">Today's trading windows, when the service sent a usable schedule.</param>
internal sealed record TickerQuote(
    string Symbol,
    string Name,
    double? Price,
    double? ChangePercent,
    string Currency,
    int? PriceHint,
    IReadOnlyList<double> Closes,
    TradingSessions Sessions);

/// <summary>
/// The two keyless quote services: Yahoo's v8 chart endpoint for anything listed, CoinGecko's
/// public v3 for coins.
/// <para>
/// <b>Neither needs a key, an account or a token</b>, which is the only reason this widget exists
/// in this port. Both are also free and shared, so every request here goes through
/// <see cref="WebCache{T}"/> and every failure goes through <see cref="Backoff"/>: a rate limit is
/// answered by asking much less often, never by trying again.
/// </para>
/// <para>
/// Yahoo's <c>v8/finance/chart</c> is used for shares, ETFs, funds, indices, currency pairs and
/// futures alike — one request path for all of them. What kind of thing the symbol names changes
/// only how it is displayed, never which service is asked, which is why
/// <see cref="MarketQuote.Classify"/> lives in Core and this file does not consult it.
/// </para>
/// </summary>
internal static class MarketService
{
    private const string Scope = "stocks";

    private const string ChartEndpoint = "https://query1.finance.yahoo.com/v8/finance/chart/";
    private const string CoinPriceEndpoint = "https://api.coingecko.com/api/v3/simple/price";
    private const string CoinChartEndpoint = "https://api.coingecko.com/api/v3/coins/";

    private static readonly WebCache<TickerQuote> Cache = new(Scope);

    public static Task<Fetched<TickerQuote>> QuoteAsync(
        string symbol,
        QuoteMarket market,
        QuoteRange range,
        TimeSpan freshFor,
        CancellationToken token)
    {
        var key = Key(symbol, market, range);

        return Cache.GetAsync(
            key,
            freshFor,
            ct => market == QuoteMarket.Crypto
                ? LoadCoinAsync(symbol, range, ct)
                : LoadListedAsync(symbol, range, ct),
            token);
    }

    public static Fetched<TickerQuote>? Peek(string symbol, QuoteMarket market, QuoteRange range)
        => Cache.Peek(Key(symbol, market, range));

    private static string Key(string symbol, QuoteMarket market, QuoteRange range)
        => $"{market}|{symbol}|{range}";

    private static async Task<Fetched<TickerQuote>> LoadListedAsync(
        string symbol,
        QuoteRange range,
        CancellationToken token)
    {
        var now = DateTimeOffset.Now;

        // Re-checked here rather than trusted from the config, because this value is about to be
        // interpolated into a URL *path* — the one place an unchecked string is not merely wrong
        // but dangerous. EscapeDataString on top, so even an accepted symbol cannot carry structure.
        if (!WebInput.IsPlausibleSymbol(symbol))
        {
            return Fetched<TickerQuote>.Failed(FetchStatus.Refused, "not a symbol", now);
        }

        var uri = new Uri(
            $"{ChartEndpoint}{Uri.EscapeDataString(symbol)}"
            + $"?range={RangeParameter(range)}&interval={IntervalParameter(range)}");

        var (status, reply, detail) = await WebFetch
            .GetJsonAsync(uri, WebJsonContext.Default.YahooChartReply, token)
            .ConfigureAwait(false);

        if (status != FetchStatus.Ok || reply is null)
        {
            return Fetched<TickerQuote>.Failed(status, detail, now);
        }

        var result = reply.Chart?.Result is { Count: > 0 } results ? results[0] : null;
        var meta = result?.Meta;

        if (result is null || meta is null)
        {
            // A symbol that does not exist answers 404, which never reaches here; a symbol that
            // exists with no data answers 200 with an empty result list.
            return Fetched<TickerQuote>.Failed(FetchStatus.Empty, "no result for that symbol", now);
        }

        var price = Sane(meta.RegularMarketPrice);
        var previous = Sane(meta.ChartPreviousClose) ?? Sane(meta.PreviousClose);
        var closes = MarketQuote.Closes(result.Indicators?.Quote is { Count: > 0 } series ? series[0].Close : null);

        // A quote with no price at all is not a quote. Falling back to the last bar is the
        // documented behaviour and covers an instrument between sessions.
        price ??= closes.Count > 0 ? closes[^1] : null;

        if (price is null)
        {
            return Fetched<TickerQuote>.Failed(FetchStatus.Empty, "the reply carried no price", now);
        }

        var quote = new TickerQuote(
            symbol,
            Name(meta),
            price,
            MarketQuote.PercentChange(price, previous),
            (meta.Currency ?? "USD").Trim().ToUpperInvariant(),
            meta.PriceHint,
            closes,
            Sessions(meta.CurrentTradingPeriod));

        return new Fetched<TickerQuote>(FetchStatus.Ok, quote, null, now);
    }

    private static async Task<Fetched<TickerQuote>> LoadCoinAsync(
        string id,
        QuoteRange range,
        CancellationToken token)
    {
        var now = DateTimeOffset.Now;

        if (!WebInput.IsPlausibleSymbol(id))
        {
            return Fetched<TickerQuote>.Failed(FetchStatus.Refused, "not a coin id", now);
        }

        var escaped = Uri.EscapeDataString(id);
        var priceUri = new Uri($"{CoinPriceEndpoint}?ids={escaped}&vs_currencies=usd&include_24hr_change=true");

        var (status, reply, detail) = await WebFetch
            .GetJsonAsync(priceUri, WebJsonContext.Default.DictionaryStringCoinPrice, token)
            .ConfigureAwait(false);

        if (status != FetchStatus.Ok || reply is null)
        {
            return Fetched<TickerQuote>.Failed(status, detail, now);
        }

        // An unknown id answers 200 with {} rather than 404, so an empty object is how "no such
        // coin" arrives and it is reported as nothing to show rather than as a broken service.
        if (!reply.TryGetValue(id, out var coin) || Sane(coin.Usd) is not { } price)
        {
            return Fetched<TickerQuote>.Failed(FetchStatus.Empty, "no price for that coin id", now);
        }

        // The chart is a second request and a nice-to-have. A sparkline that failed must not cost
        // the price that succeeded, so its failure is swallowed into an empty series — the one
        // place in these four widgets where a failure is not surfaced, because the widget has
        // something better to show and the missing chart is visible on its own.
        var closes = await CoinClosesAsync(escaped, range, token).ConfigureAwait(false);

        var quote = new TickerQuote(
            id,
            id,
            price,
            Sane(coin.Change24H),
            "USD",
            null,
            closes,

            // Crypto has no exchange calendar: it trades every minute of every day, so there is no
            // session to be outside of and no "market closed" to report.
            TradingSessions.None);

        return new Fetched<TickerQuote>(FetchStatus.Ok, quote, null, now);
    }

    private static async Task<IReadOnlyList<double>> CoinClosesAsync(
        string escapedId,
        QuoteRange range,
        CancellationToken token)
    {
        var uri = new Uri($"{CoinChartEndpoint}{escapedId}/market_chart?vs_currency=usd&days={CoinDays(range)}");

        var (status, reply, detail) = await WebFetch
            .GetJsonAsync(uri, WebJsonContext.Default.CoinChartReply, token)
            .ConfigureAwait(false);

        if (status != FetchStatus.Ok || reply?.Prices is null)
        {
            // Logged, not surfaced: the price is on screen and a missing chart is visible
            // without being told. This is the one swallowed failure in the four widgets.
            Log.Debug(Scope, $"no sparkline for {escapedId}: {FetchState.Message(status)} {detail}".TrimEnd());
            return [];
        }

        var closes = new List<double>(reply.Prices.Count);

        foreach (var pair in reply.Prices)
        {
            // Each entry is [epochMilliseconds, price]. A pair that is not a pair is dropped.
            if (pair.Count >= 2 && double.IsFinite(pair[1]) && pair[1] > 0)
            {
                closes.Add(pair[1]);
            }
        }

        return closes;
    }

    private static TradingSessions Sessions(YahooTradingPeriod? period)
    {
        if (period is null)
        {
            return TradingSessions.None;
        }

        // Each window independently: a broken post-market window must not cost the regular one,
        // which is the window that decides whether the phase is knowable at all.
        return new TradingSessions(
            MarketQuote.Window(period.Pre?.Start, period.Pre?.End),
            MarketQuote.Window(period.Regular?.Start, period.Regular?.End),
            MarketQuote.Window(period.Post?.Start, period.Post?.End));
    }

    private static string Name(YahooMeta meta)
    {
        if (!string.IsNullOrWhiteSpace(meta.ShortName))
        {
            return meta.ShortName.Trim();
        }

        return !string.IsNullOrWhiteSpace(meta.LongName) ? meta.LongName.Trim() : string.Empty;
    }

    /// <summary>The range as the chart endpoint spells it.</summary>
    private static string RangeParameter(QuoteRange range) => range switch
    {
        QuoteRange.Day => "1d",
        QuoteRange.FiveDays => "5d",
        QuoteRange.Month => "1mo",
        QuoteRange.ThreeMonths => "3mo",
        QuoteRange.SixMonths => "6mo",
        QuoteRange.Year => "1y",
        QuoteRange.TwoYears => "2y",
        QuoteRange.FiveYears => "5y",
        QuoteRange.TenYears => "10y",
        _ => "1d",
    };

    /// <summary>
    /// The bar size to pair with a range, so the series is a few hundred points rather than tens of
    /// thousands. A ten-year chart at daily granularity is 2,500 points drawn into 300 pixels.
    /// </summary>
    private static string IntervalParameter(QuoteRange range) => range switch
    {
        QuoteRange.Day => "5m",
        QuoteRange.FiveDays => "15m",
        QuoteRange.Month or QuoteRange.ThreeMonths => "1d",
        QuoteRange.SixMonths or QuoteRange.Year or QuoteRange.TwoYears => "1wk",
        QuoteRange.FiveYears or QuoteRange.TenYears => "1mo",
        _ => "5m",
    };

    /// <summary>CoinGecko takes a day count rather than a named range.</summary>
    private static string CoinDays(QuoteRange range) => range switch
    {
        QuoteRange.Day => "1",
        QuoteRange.FiveDays => "5",
        QuoteRange.Month => "30",
        QuoteRange.ThreeMonths => "90",
        QuoteRange.SixMonths => "180",
        QuoteRange.Year => "365",
        QuoteRange.TwoYears => "730",
        QuoteRange.FiveYears => "1825",
        QuoteRange.TenYears => "3650",
        _ => "1",
    };

    private static double? Sane(double? value)
        => value is { } number && double.IsFinite(number) ? number : null;
}
