using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Windows.System;

namespace Themia.App.Shell.Config;

/// <summary>
/// The Stocks widget's settings rows.
/// <para>
/// The symbol box is the trust boundary on this page: it is composed into a request to a quote
/// service, so it is checked before it is written and the reason it was refused is shown under the
/// box rather than swallowed. It is never repaired into something that works.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    public static void Stocks(List<FrameworkElement> rows, StocksConfig config, Action save)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(save);

        rows.Add(FluentLayout.Header("Instrument"));

        var market = FluentLayout.Choice(
            [(QuoteMarket.Listed, "Listed"), (QuoteMarket.Crypto, "Crypto")],
            config.Market,
            value => { config.Market = value; save(); });

        rows.Add(FluentLayout.Row(Glyphs.Stocks, "Market",
            "Listed covers shares, funds, indices, currencies and futures", market));

        var symbol = new TextBox
        {
            Text = config.Symbol,
            Width = 200,
            MaxLength = WebInput.MaxSymbolLength,
            PlaceholderText = config.Market == QuoteMarket.Crypto ? "bitcoin" : "AAPL",
        };

        var verdict = FluentLayout.Verdict();

        void Commit()
        {
            var typed = symbol.Text.Trim();

            if (typed == config.Symbol)
            {
                return;
            }

            // Refused, not repaired. Stripping the characters that make a value unusable would
            // leave the widget quoting something other than what is written in its own settings,
            // and the user could not see the difference.
            if (typed.Length > 0 && !WebInput.IsPlausibleSymbol(typed))
            {
                FluentLayout.Refuse(verdict, "Letters, digits and . - ^ = only");
                return;
            }

            FluentLayout.Confirm(verdict, string.Empty);
            config.Symbol = typed;
            save();
        }

        // On commit, not per keystroke: every character typed into a symbol box would otherwise be
        // a saved config and a quote request for a symbol that does not exist yet.
        symbol.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                args.Handled = true;
                Commit();
            }
        };

        symbol.LostFocus += (_, _) => Commit();

        rows.Add(FluentLayout.FieldRow(Glyphs.Search, "Symbol",
            "A ticker like AAPL or ^GSPC, or a coin id like bitcoin", symbol, verdict));

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

        rows.Add(FluentLayout.Header("Chart"));

        rows.Add(FluentLayout.Row(Glyphs.Motion, "Show chart", "A sparkline of the period below the price",
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

        FluentLayout.RefreshChoice(rows, config.RefreshMinutes, value => { config.RefreshMinutes = value; save(); }, fast: true);
    }
}
