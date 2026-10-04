using Themia.Core.Model;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>[S-1307]..[S-1311] The stock widget: search, extended hours, exchange time, markers, facts and the ladder.</summary>
public sealed class StockWidgetTests
{
    // ---- [S-1307] search ---------------------------------------------------------------------

    [Fact]
    public void S1307_OnlyChartableKindsSurvive_NamedByShortThenLongName()
    {
        var hits = MarketSearch.Listed(
        [
            new ListedCandidate("MSFT", "Microsoft Corporation", null, "EQUITY", "NASDAQ"),
            new ListedCandidate("MSFT250117C00400000", "MSFT Jan 2025 400 Call", null, "OPTION", "OPR"),
            new ListedCandidate("^GSPC", null, "S&P 500", "INDEX", "SNP"),
            new ListedCandidate("EURUSD=X", "EUR/USD", null, "CURRENCY", "CCY"),
            new ListedCandidate("bad symbol!", "Nope", null, "EQUITY", null),
        ]);

        Assert.Equal(["MSFT", "^GSPC", "EURUSD=X"], hits.Select(h => h.Symbol));
        Assert.Equal("S&P 500", hits[1].Name);
        Assert.Equal(AssetClass.Index, hits[1].Asset);
        Assert.Equal(AssetClass.Currency, hits[2].Asset);
        Assert.All(hits, h => Assert.Equal(QuoteMarket.Listed, h.Market));
    }

    [Fact]
    public void S1307_CoinsAreNamedWithTheirTicker()
    {
        var hit = Assert.Single(MarketSearch.Coins([new CoinCandidate("bitcoin", "Bitcoin", "btc")]));

        Assert.Equal("bitcoin", hit.Symbol);
        Assert.Equal("Bitcoin (BTC)", hit.Name);
        Assert.Equal(QuoteMarket.Crypto, hit.Market);
    }

    [Fact]
    public void S1307_ResultsAlternateAndStopAtTwelve()
    {
        var listed = Enumerable.Range(0, 8).Select(i => new SymbolHit($"S{i}", "s", QuoteMarket.Listed, AssetClass.Equity, "")).ToList();
        var coins = Enumerable.Range(0, 8).Select(i => new SymbolHit($"c{i}", "c", QuoteMarket.Crypto, AssetClass.Crypto, "")).ToList();

        var merged = MarketSearch.Merge(listed, coins);

        Assert.Equal(MarketSearch.MaxHits, merged.Count);
        Assert.Equal(["S0", "c0", "S1", "c1"], merged.Take(4).Select(h => h.Symbol));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("apple", true)]
    public void S1307_EmptyQueriesAreNotSent(string query, bool expected)
        => Assert.Equal(expected, MarketSearch.IsQuery(query));

    // ---- [S-1308] extended hours ---------------------------------------------------------------

    private static readonly SessionWindow Pre = new(1000, 2000);
    private static readonly SessionWindow Regular = new(2000, 5000);
    private static readonly SessionWindow Post = new(5000, 6000);

    private static IReadOnlyList<QuoteBar> Day() =>
    [
        new(1100, 99), new(1900, 101),
        new(2000, 100), new(4900, 104),
        new(5100, 103), new(5900, 102),
    ];

    [Fact]
    public void S1308_TheLastBarInEachExtendedWindowIsItsPrint()
    {
        var prints = MarketQuote.ExtendedQuotesOf(Day(), new TradingSessions(Pre, Regular, Post), regularClose: 104);

        Assert.Equal(101, prints.Pre!.Value.Price);
        Assert.Equal(102, prints.Post!.Value.Price);
        Assert.Equal((102 - 104) / 104.0 * 100, prints.Post!.Value.ChangePercent, 6);
        Assert.Equal(5900, prints.Post!.Value.At);
    }

    [Fact]
    public void S1308_NoCloseOrNoWindowMeansNoPrint()
    {
        Assert.Null(MarketQuote.ExtendedQuoteIn(Day(), Post, regularClose: null));
        Assert.Null(MarketQuote.ExtendedQuoteIn(Day(), Post, regularClose: 0));
        Assert.Null(MarketQuote.ExtendedQuoteIn(Day(), null, regularClose: 100));
        Assert.Null(MarketQuote.ExtendedQuoteIn([new(2500, 100)], Post, regularClose: 100));
    }

    [Theory]
    [InlineData(MarketPhase.Pre, 101.0)]
    [InlineData(MarketPhase.Post, 102.0)]
    [InlineData(MarketPhase.Closed, 102.0)]
    [InlineData(MarketPhase.Open, null)]
    public void S1308_ThePhaseChoosesWhichPrintShows(MarketPhase phase, double? expected)
    {
        var prints = MarketQuote.ExtendedQuotesOf(Day(), new TradingSessions(Pre, Regular, Post), 104);

        Assert.Equal(expected, MarketQuote.CurrentExtended(prints, phase)?.Price);
    }

    [Fact]
    public void S1308_TheSeriesSplitsIntoRegularAndExtendedRuns()
    {
        var segments = MarketQuote.Segments(Day(), new TradingSessions(Pre, Regular, Post));

        Assert.Equal([(0, 2, true), (2, 4, false), (4, 6, true)], segments);
    }

    [Fact]
    public void S1308_WithoutAScheduleTheWholeSeriesIsOneRegularRun()
        => Assert.Equal([(0, 6, false)], MarketQuote.Segments(Day(), TradingSessions.None));

    [Fact]
    public void S1308_ExtendedBarsAreAskedForOnlyWhereTheyExist()
    {
        Assert.True(new StocksConfig { ShowExtendedHours = true, Range = QuoteRange.FiveDays }.WantsPrePost);
        Assert.False(new StocksConfig { ShowExtendedHours = true, Range = QuoteRange.Month }.WantsPrePost);
        Assert.False(new StocksConfig { ShowExtendedHours = true, Market = QuoteMarket.Crypto }.WantsPrePost);
        Assert.False(new StocksConfig { Range = QuoteRange.Day }.WantsPrePost);
    }

    // ---- [S-1309] exchange time --------------------------------------------------------------

    [Fact]
    public void S1309_IntradayLabelsAreTheExchangesClock()
    {
        // 13:30 UTC is 09:30 in New York (EDT, UTC-4), whatever the reader's own zone.
        var open = new DateTimeOffset(2026, 10, 2, 13, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        Assert.Equal("09:30", ExchangeTime.Label(open, TimeSpan.FromHours(-4), QuoteRange.Day, TimeSpan.FromHours(2)));
        Assert.Equal("15:30", ExchangeTime.Label(open, null, QuoteRange.Day, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void S1309_LongerRangesAreDated_WithTheYearFromTwoYears()
    {
        var at = new DateTimeOffset(2025, 3, 4, 20, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        Assert.Equal("Mar 4", ExchangeTime.Label(at, TimeSpan.FromHours(-5), QuoteRange.Year, TimeSpan.Zero));
        Assert.Equal("Mar 4, 2025", ExchangeTime.Label(at, TimeSpan.FromHours(-5), QuoteRange.FiveYears, TimeSpan.Zero));
        Assert.Equal("Tue 15:00", ExchangeTime.Label(at, TimeSpan.FromHours(-5), QuoteRange.FiveDays, TimeSpan.Zero));
    }

    [Fact]
    public void S1309_LabelsAreStartAndEnd_PlusTheMiddleOnAWideChart()
    {
        IReadOnlyList<QuoteBar> bars = [new(0, 1), new(3600, 2), new(7200, 3)];

        Assert.Equal(["00:00", "02:00"], ExchangeTime.Labels(bars, TimeSpan.Zero, QuoteRange.Day, TimeSpan.Zero, withMiddle: false));
        Assert.Equal(["00:00", "01:00", "02:00"], ExchangeTime.Labels(bars, TimeSpan.Zero, QuoteRange.Day, TimeSpan.Zero, withMiddle: true));
        Assert.Empty(ExchangeTime.Labels([new(0, 1)], null, QuoteRange.Day, TimeSpan.Zero, withMiddle: true));
    }

    [Theory]
    [InlineData(-14400L, true)]
    [InlineData(19800L, true)]
    [InlineData(-14401L, false)]
    [InlineData(60L * 60 * 15, false)]
    public void S1309_AnOffsetIsWholeMinutesWithinFourteenHours(long seconds, bool usable)
        => Assert.Equal(usable, ExchangeTime.Offset(seconds) is not null);

    [Theory]
    [InlineData("EDT", "EDT")]
    [InlineData("GMT+1", "GMT+1")]
    [InlineData("<b>x", "")]
    [InlineData("TOOLONGZONE", "")]
    [InlineData(null, "")]
    public void S1309_AZoneNameIsShortAndPlain(string? name, string expected)
        => Assert.Equal(expected, ExchangeTime.Zone(name));

    // ---- [S-1310] markers and colours ----------------------------------------------------------

    [Fact]
    public void S1310_ExtremesAreTheFirstLowestAndHighest()
    {
        IReadOnlyList<QuoteBar> bars = [new(1, 5), new(2, 3), new(3, 9), new(4, 3), new(5, 9)];

        Assert.Equal((1, 2), MarketQuote.Extremes(bars));
        Assert.Null(MarketQuote.Extremes([]));
    }

    [Fact]
    public void S1310_AHandEditedColourThatIsNotAColourGoesBackToTheTheme()
    {
        var config = new StocksConfig { UpColor = "#22aa44", DownColor = "red" };

        var repairs = config.Repair();

        Assert.Equal("#22AA44", config.UpColor);
        Assert.Equal(string.Empty, config.DownColor);
        Assert.Single(repairs);
    }

    // ---- [S-1311] facts and the ladder ---------------------------------------------------------

    [Theory]
    [InlineData(950L, "950")]
    [InlineData(12_345L, "12.3K")]
    [InlineData(4_560_000L, "4.56M")]
    [InlineData(1_200_000_000L, "1.2B")]
    [InlineData(null, "--")]
    public void S1311_VolumeIsCompact(long? volume, string expected)
        => Assert.Equal(expected, MarketQuote.Volume(volume));

    [Fact]
    public void S1311_FactsAreOnlyWhatTheReplyCarried()
    {
        var quote = new TickerQuote("^GSPC", "S&P 500", 5000, 0.1, "USD", 2, [], TradingSessions.None)
        {
            Stats = new QuoteStats(4990, 5010, 4980, null, 4100, null, "SNP"),
        };

        var facts = MarketQuote.Facts(quote);

        Assert.Equal(["Previous close", "Day range", "Exchange"], facts.Select(f => f.Label));
        Assert.Equal("4,980.00 – 5,010.00", facts[1].Value);
    }

    private static StockPlan Plan(double width, double height, bool chart = true, int stats = 5)
        => StockLayout.Plan(new WidgetArea(width, height), new StockInputs(6, 10, chart, false, stats, true));

    [Fact]
    public void S1311_TheSmallestTileKeepsThePriceAndChange_AndSheds()
    {
        var plan = Plan(120, 76);

        Assert.False(plan.Currency);
        Assert.False(plan.Chart);
        Assert.Equal(StatsPlacement.None, plan.Stats);
    }

    [Fact]
    public void S1311_AStripPutsTheChartBesideTheReadout()
    {
        var plan = Plan(480, 116);

        Assert.True(plan.Beside);
        Assert.True(plan.Chart);
        Assert.True(plan.ChartHeight >= 76);
    }

    [Fact]
    public void S1311_ALargeTileGrowsTheChart_NotJustThePrice()
    {
        var plan = Plan(560, 376);

        Assert.True(plan.ChartHeight > 120, $"chart {plan.ChartHeight}");
        Assert.Equal(StatsPlacement.Tiles, plan.Stats);
        Assert.True(plan.Labels && plan.MiddleLabel && plan.Axis && plan.MinMax);
        Assert.True(plan.Price.FontSize <= FluentType.TitleLarge.FontSize);
    }

    [Fact]
    public void S1311_TheLargestTileHasChipsAndAFactsColumn()
    {
        var plan = Plan(800, 516);

        Assert.True(plan.RangeChips);
        Assert.Equal(StatsPlacement.Column, plan.Stats);
        Assert.InRange(plan.StatCount, 1, 5);
    }

    [Fact]
    public void S1311_ATallColumnStacksItsFactsUnderTheChart()
    {
        var plan = Plan(200, 356);

        Assert.Equal(StatsPlacement.Stack, plan.Stats);
        Assert.InRange(plan.StatCount, 1, 4);
        Assert.True(plan.Chart);
    }

    [Fact]
    public void S1311_WithoutAChartThePriceTakesTheTile()
    {
        var plan = Plan(560, 376, chart: false);

        Assert.False(plan.Chart);
        Assert.True(plan.Price.FontSize >= FluentType.TitleLarge.FontSize);
    }

    [Theory]
    [InlineData(120, 76)]
    [InlineData(200, 106)]
    [InlineData(320, 196)]
    [InlineData(480, 116)]
    [InlineData(200, 356)]
    [InlineData(560, 376)]
    [InlineData(800, 516)]
    public void S1311_NothingOverflowsTheTile(double width, double height)
    {
        var plan = Plan(width, height);

        var used = plan.Beside
            ? Math.Max(plan.Price.LineHeight + plan.Change.LineHeight, plan.ChartHeight)
            : plan.Price.LineHeight + (plan.ExtrasUnder ? plan.Change.LineHeight : 0)
                + (plan.Chart ? plan.ChartHeight : 0)
                + (plan.Labels ? FluentType.Caption.LineHeight : 0)
                + (plan.RangeChips ? StockLayout.ChipsHeight : 0)
                + (plan.Stats == StatsPlacement.Tiles ? StockLayout.TileHeight : 0)
                + (plan.Stats == StatsPlacement.Stack ? plan.StatCount * StockLayout.StackLine : 0);

        Assert.True(used <= height + 1, $"{width}x{height}: {used}");
    }
}
