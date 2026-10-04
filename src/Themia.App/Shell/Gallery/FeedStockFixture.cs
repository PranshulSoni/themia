#if DEBUG
using Themia.App.Services.Web;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-1305]/[S-1311] Offline fixtures for the feed and stock widgets: fixed answers seeded into
/// their caches as fresh, so the populated pages render the same on every run and without the
/// network. (Only the feed items' site icons are fetched, from the sites themselves.)
/// </summary>
internal static class FeedStockFixture
{
    private const string WorldFeed = "https://feeds.example-news.test/world.xml";
    private const string TechFeed = "https://tech.example-blog.test/atom.xml";

    public static RssConfig RssConfig()
    {
        var config = new RssConfig
        {
            Feeds = [new RssFeed { Url = WorldFeed }, new RssFeed { Url = TechFeed }],
        };

        var now = DateTimeOffset.Now;
        var cap = config.EffectiveMaxItems;

        FeedService.SeedGallery(WorldFeed, cap, new FeedDocument("World News",
        [
            Item("Leaders agree a framework for cross-border river management after week of talks", "https://www.bbc.co.uk/news/world-1", now.AddMinutes(-12),
                "Negotiators from six countries signed an outline that sets shared limits on extraction and a joint monitoring body, the first such agreement in two decades."),
            Item("Coastal towns brace for the strongest autumn storm in years", "https://www.bbc.co.uk/news/world-2", now.AddHours(-2),
                "Forecasters expect gusts above 120 km/h along exposed coasts tonight, with ferry services suspended and schools closing early."),
            Item("Archaeologists uncover a Roman road beneath a city car park", "https://www.bbc.co.uk/news/world-3", now.AddHours(-5),
                "The well-preserved section, found during routine survey work, suggests the settlement was larger than records indicated."),
            Item("Central bank holds rates steady and signals patience", "https://www.bbc.co.uk/news/world-4", now.AddHours(-9),
                "Policymakers said inflation was easing but wanted more evidence before cutting."),
            Item("Night train network expands with four new routes", "https://www.bbc.co.uk/news/world-5", now.AddDays(-1).AddHours(-3),
                "Operators say demand has doubled since 2021, driven by travellers avoiding short-haul flights."),
            Item("Record turnout at the city's first car-free Sunday", "https://www.bbc.co.uk/news/world-6", now.AddDays(-3),
                "Organisers counted more than 200,000 cyclists and pedestrians across the closed ring road."),
        ]));

        FeedService.SeedGallery(TechFeed, cap, new FeedDocument("The Verge",
        [
            Item("A closer look at the new low-power laptop chips", "https://www.theverge.com/tech/1", now.AddMinutes(-40),
                "Battery life claims of 20 hours hold up in our first tests, though sustained performance drops sharply on long exports."),
            Item("Open-source text editor reaches version 2.0 with a plugin overhaul", "https://www.theverge.com/tech/2", now.AddHours(-3),
                "Plugins now run in a sandbox, a change the maintainers say closes a whole class of bugs."),
            Item("Why your next monitor might be a 5K panel", "https://www.theverge.com/tech/3", now.AddHours(-7),
                "Prices have fallen by half in a year, and the sharper text is hard to give up once you have used it."),
            Item("Smart thermostats get a common standard at last", "https://www.theverge.com/tech/4", now.AddDays(-2),
                "Devices certified under the new scheme work with every major home hub out of the box."),
            Item("Review: the e-reader that finally gets colour right", "https://www.theverge.com/tech/5", now.AddDays(-9),
                "Comics and maps look good; text is a touch softer than on the monochrome model."),
        ]));

        return config;
    }

    public static StocksConfig StocksConfig()
    {
        var config = new StocksConfig
        {
            Symbol = "MSFT",
            DisplayName = "Microsoft",
            Range = QuoteRange.Day,
            ShowExtendedHours = true,
            ShowMinMax = true,
        };

        MarketService.SeedGallery(QuoteMarket.Listed, QuoteRange.Day, prePost: true, Quote(DateTimeOffset.UtcNow));
        return config;
    }

    /// <summary>
    /// A New York share mid-session: pre-market from 04:00, the regular session from 09:30 with the
    /// clock inside it, five-minute bars up to now on a fixed path, the day's facts.
    /// </summary>
    private static TickerQuote Quote(DateTimeOffset now)
    {
        var nowSeconds = now.ToUnixTimeSeconds();
        var regularStart = nowSeconds - (4 * 3600) - (nowSeconds % 300);
        var regular = new SessionWindow(regularStart, regularStart + (390 * 60));
        var pre = new SessionWindow(regularStart - (330 * 60), regularStart);
        var post = new SessionWindow(regular.End, regular.End + (240 * 60));

        var bars = new List<QuoteBar>();
        var price = 409.80;

        for (var t = pre.Start + (180 * 60); t <= nowSeconds; t += 300)
        {
            var step = bars.Count;
            price += (Math.Sin(step * 0.37) * 0.9) + (Math.Cos(step * 0.11) * 0.55) + (step % 7 == 3 ? -0.8 : 0.12);
            bars.Add(new QuoteBar(t, Math.Round(price, 2)));
        }

        var last = bars[^1].Close;

        return new TickerQuote("MSFT", "Microsoft Corporation", last, MarketQuote.PercentChange(last, 409.12), "USD", 2, bars,
            new TradingSessions(pre, regular, post))
        {
            ExchangeOffset = TimeSpan.FromHours(-4),
            ZoneName = "EDT",
            QuoteType = "EQUITY",
            HasPrePost = true,
            Stats = new QuoteStats(409.12, bars.Max(b => b.Close), bars.Min(b => b.Close), 468.35, 344.79, 18_734_520, "NasdaqGS"),
        };
    }

    private static FeedEntry Item(string title, string link, DateTimeOffset published, string summary)
        => new(title, link, published, summary);
}
#endif
