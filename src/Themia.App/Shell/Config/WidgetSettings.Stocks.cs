using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services.Web;
using Themia.App.Widgets;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Config;

/// <summary>
/// The Stocks widget's settings rows.
/// <para>
/// The symbol box is the trust boundary on this page: it is composed into a request to a quote
/// service, so it is checked before it is written and the reason it was refused is shown under the
/// box rather than swallowed. It is never repaired into something that works. [S-1307] It is also a
/// search, as the original's: typing asks Yahoo and CoinGecko together and a pick sets the symbol,
/// its market and its name in one go; a symbol typed in full and committed with Enter still works.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    /// <summary>How long typing has to pause before the search asks. Rate limiting for two free services, not timing.</summary>
    private static readonly TimeSpan SearchPause = TimeSpan.FromMilliseconds(300);

    public static void Stocks(List<FrameworkElement> rows, StocksConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Instrument"));

        var verdict = FluentLayout.Verdict();
        var search = SymbolSearch(config, save, verdict);

        rows.Add(FluentLayout.FieldRow(Glyphs.Search, "Symbol",
            "Search by name or ticker: shares, funds, indices, currencies, futures and coins", search, verdict));

        var market = FluentLayout.Choice(
            [(QuoteMarket.Listed, "Listed"), (QuoteMarket.Crypto, "Crypto")],
            config.Market,
            value => { config.Market = value; save(); });

        rows.Add(FluentLayout.Row(Glyphs.Stocks, "Market",
            "Set by a search pick. Listed covers shares, funds, indices, currencies and futures", market));

        var name = new TextBox { Text = config.DisplayName, Width = 200, PlaceholderText = "Optional" };

        name.LostFocus += (_, _) =>
        {
            var typed = name.Text.Trim();

            if (typed == config.DisplayName)
            {
                return;
            }

            config.DisplayName = typed;
            save();
        };

        rows.Add(FluentLayout.Row(Glyphs.Rename, "Name", "Shown instead of the symbol", name));

        rows.Add(FluentLayout.Row(Glyphs.Moon, "Say when the market is shut",
            "Listed instruments only — crypto trades every day",
            FluentLayout.Toggle(config.ShowMarketState, value => { config.ShowMarketState = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Sun, "Extended hours",
            "Pre-market and after-hours prices for listed instruments, on the 1 day and 5 day charts",
            FluentLayout.Toggle(config.ShowExtendedHours, value => { config.ShowExtendedHours = value; save(); })));

        rows.Add(FluentLayout.Header("Chart"));

        rows.Add(FluentLayout.Row(Glyphs.Motion, "Show chart", "The period's prices under or beside the price",
            FluentLayout.Toggle(config.ShowSparkline, value => { config.ShowSparkline = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Hours, "Period", "How much history the chart covers",
            FluentLayout.Choice(
                [
                    (QuoteRange.Day, "1 day"),
                    (QuoteRange.FiveDays, "5 days"),
                    (QuoteRange.Month, "1 month"),
                    (QuoteRange.ThreeMonths, "3 months"),
                    (QuoteRange.SixMonths, "6 months"),
                    (QuoteRange.Year, "1 year"),
                    (QuoteRange.TwoYears, "2 years"),
                    (QuoteRange.FiveYears, "5 years"),
                    (QuoteRange.TenYears, "10 years"),
                ],
                config.Range,
                value => { config.Range = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Fit, "Fill under the line", "Shade the area under the chart",
            FluentLayout.Toggle(config.ShowAreaFill, value => { config.ShowAreaFill = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "High and low", "Mark the period's highest and lowest price on the chart",
            FluentLayout.Toggle(config.ShowMinMax, value => { config.ShowMinMax = value; save(); })));

        rows.Add(FluentLayout.ColorPickerRow(Glyphs.Palette, "Rising colour", "The change and the chart when the price is up",
            config.UpColor,
            value => { config.UpColor = value; save(); },
            new ColorChoiceOptions { DefaultLabel = "Windows green", DefaultColor = () => ThemeColor("SystemFillColorSuccessBrush") }));

        rows.Add(FluentLayout.ColorPickerRow(Glyphs.Palette, "Falling colour", "The change and the chart when the price is down",
            config.DownColor,
            value => { config.DownColor = value; save(); },
            new ColorChoiceOptions { DefaultLabel = "Windows red", DefaultColor = () => ThemeColor("SystemFillColorCriticalBrush") }));

        FluentLayout.RefreshChoice(rows, config.RefreshMinutes, value => { config.RefreshMinutes = value; save(); }, fast: true);
    }

    /// <summary>
    /// [S-1307] The symbol box with search: suggestions as the user types (after a short pause, the
    /// previous search abandoned), a pick sets symbol, market and name; Enter on typed text commits
    /// it as a raw symbol under the current market, through the same check as before.
    /// </summary>
    private static AutoSuggestBox SymbolSearch(StocksConfig config, Action save, TextBlock verdict)
    {
        var box = new AutoSuggestBox
        {
            Text = config.Symbol,
            MinWidth = 260,
            PlaceholderText = config.Market == QuoteMarket.Crypto ? "bitcoin" : "AAPL, ^GSPC, EURUSD=X, GC=F",
            QueryIcon = new SymbolIcon(Symbol.Find),
        };

        ShowClass(verdict, config);

        var hits = new List<SymbolHit>();
        CancellationTokenSource? asking = null;
        var pause = DispatcherQueue.GetForCurrentThread()?.CreateTimer();

        async Task AskAsync(string query)
        {
            asking?.Cancel();
            var cts = new CancellationTokenSource();
            asking = cts;

            try
            {
                var found = await MarketService.SearchAsync(query, cts.Token).ConfigureAwait(true);

                if (!ReferenceEquals(asking, cts) || box.Text.Trim() != query)
                {
                    return;
                }

                hits = [.. found];
                box.ItemsSource = hits.Select(Describe).ToList();
            }
            catch (OperationCanceledException)
            {
                // ignore: a newer keystroke asked again.
            }
            finally
            {
                if (ReferenceEquals(asking, cts))
                {
                    asking = null;
                }

                cts.Dispose();
            }
        }

        if (pause is not null)
        {
            pause.Interval = SearchPause;
            pause.IsRepeating = false;
            pause.Tick += (_, _) => _ = AskAsync(box.Text.Trim());
        }

        box.TextChanged += (_, args) =>
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            {
                return;
            }

            pause?.Stop();

            if (!MarketSearch.IsQuery(box.Text))
            {
                hits = [];
                box.ItemsSource = null;
                return;
            }

            pause?.Start();
        };

        void Pick(SymbolHit hit)
        {
            pause?.Stop();
            asking?.Cancel();

            config.Symbol = hit.Symbol;
            config.Market = hit.Market;
            config.DisplayName = hit.Name;
            box.Text = hit.Symbol;
            box.ItemsSource = null;
            FluentLayout.Confirm(verdict, string.Empty);
            ShowClass(verdict, config, hit);
            save();
        }

        box.SuggestionChosen += (_, args) =>
        {
            var index = box.ItemsSource is List<string> shown && args.SelectedItem is string text ? shown.IndexOf(text) : -1;

            if (index >= 0 && index < hits.Count)
            {
                Pick(hits[index]);
            }
        };

        box.QuerySubmitted += (_, args) =>
        {
            if (args.ChosenSuggestion is not null)
            {
                return; // ignore: SuggestionChosen already applied the pick.
            }

            var typed = box.Text.Trim();

            if (typed == config.Symbol)
            {
                return;
            }

            // Refused, not repaired: stripping the characters that make a value unusable would
            // leave the widget quoting something other than what is written in its own settings.
            if (typed.Length > 0 && !WebInput.IsPlausibleSymbol(typed))
            {
                FluentLayout.Refuse(verdict, "Pick a search result, or type a symbol: letters, digits and . - ^ = only");
                return;
            }

            config.Symbol = typed;
            FluentLayout.Confirm(verdict, string.Empty);
            ShowClass(verdict, config);
            save();
        };

        box.Unloaded += (_, _) =>
        {
            pause?.Stop();
            asking?.Cancel();
        };

        return box;
    }

    /// <summary>What a theme brush currently paints, for a default swatch.</summary>
    private static Windows.UI.Color? ThemeColor(string key)
        => FluentLayout.Themed(key, Microsoft.UI.Colors.Gray) is Microsoft.UI.Xaml.Media.SolidColorBrush brush ? brush.Color : null;

    /// <summary>One suggestion line: "Microsoft Corporation — MSFT · NASDAQ · Shares".</summary>
    private static string Describe(SymbolHit hit)
    {
        var parts = new List<string> { hit.Symbol.ToUpperInvariant() };

        if (hit.Exchange.Length > 0)
        {
            parts.Add(hit.Exchange);
        }

        parts.Add(MarketSearch.Label(hit.Asset));
        return $"{hit.Name} — {string.Join(" · ", parts)}";
    }

    /// <summary>[S-1307] What the chosen symbol is, under the box: "Index", "Currency"…</summary>
    private static void ShowClass(TextBlock verdict, StocksConfig config, SymbolHit? hit = null)
    {
        if (config.EffectiveSymbol.Length == 0)
        {
            return;
        }

        var asset = hit?.Asset ?? MarketQuote.Classify(config.EffectiveSymbol, config.Market);
        var where = hit is { Exchange.Length: > 0 } ? $" on {hit.Exchange}" : string.Empty;
        FluentLayout.Confirm(verdict, $"{MarketSearch.Label(asset)}{where}");
    }
}
