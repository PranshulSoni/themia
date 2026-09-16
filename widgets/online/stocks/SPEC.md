# Stocks: Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [S-01] Header shows the configured symbol's name (its `name`, else the upper-cased symbol); falls back to `label` override or "Stocks" when no symbol is set. TrendingUp icon; hidden when `hideHeader` true.
- [S-02] Shows empty prompt "Select a symbol to see your stock" when no symbol is set.
- [S-03] Shows "Loading..." on first load with no cached ticker.
- [S-04] Shows error text on fetch failure when no cached ticker is present.
- [S-06] Shows exactly one ticker: % change colored green (≥0) or red (<0) with +/- prefix, large price + currency, and full-width sparkline with start/end date labels over range. One symbol per widget; there is no multi-symbol list view.
- [S-07] Sparkline stroke colored green when change ≥ 0, else destructive red; hidden when fewer than 2 data points.
- [S-08] Price formatting: thousands-separated, with the decimal places [S-39] resolves — at minimum 2 for ≥1, 4 for <1.
- [S-39] Decimal places follow the instrument's own tick size, taken from Yahoo `meta.priceHint` (2 for a share, 4 for an FX pair). The hint is validated (integer, 0–8) and may only **raise** precision above the magnitude rule, so a sub-dollar quote reported with `priceHint: 2` keeps its 4 places. A missing/unusable hint, crypto, and tickers cached before this shipped fall back to the magnitude rule alone. Applies to both the main quote and the extended-hours print.
- [S-32] When `dimWhenClosed` (default true) and the **stock** ticker's session is `closed`, a "Market closed" badge with a Moon icon is shown. Crypto is never marked (trades 24/7).
- [S-33] Session phase comes from `meta.currentTradingPeriod` (epoch seconds, exchange-resolved): `open` inside `regular`, `pre`/`post` inside those windows, else `closed`; each window is `[start, end)`. Yahoo's v8 chart API sends no `marketState`, so it is not consulted. A missing/malformed schedule is **unknown**, not closed — no badge. Evaluated against the clock at render (one-minute tick), not at fetch time, so the badge is right on a cold launch from cache and flips at the bell regardless of `refreshMinutes`. Crypto is always `open`.
- [S-35] The badge names the phase and says whether extended trading is **active**: "Pre-market" (Sunrise) or "After hours" (Sunset) in accent color when `showExtendedHours` is on, the clock is inside that window, `meta.hasPrePostMarketData` is true **and** a print exists in the window; otherwise the muted "Market closed" (Moon). When active, the last extended print and its % move off the regular close are shown under the price. On the '1d' range, extended-hours bars are drawn dashed at 55% stroke opacity so the pre/post part of the session is visible in the sparkline; '5d' draws one solid line, because `currentTradingPeriod` covers today only and the API sends no per-day windows for multi-day ranges — every earlier day's regular bars would otherwise be mislabelled as extended.
- [S-36] Sparkline start/end labels are the first and last **plotted bar**, rendered in the listing exchange's zone (`meta.exchangeTimezoneName`) — clock times for intraday ranges, dates otherwise; the badge tooltip carries the DST-correct zone marker ("EDT"). An unusable/absent zone falls back to the viewer's local zone; a cached ticker with no bar timestamps falls back to the old now-minus-range labels. All phase logic compares absolute instants, so it is identical for every viewer regardless of their own timezone.

## Configuration
- [S-11] `symbols`: array of `{symbol, type: 'stock'|'crypto', name?}` holding at most one entry; empty by default. Adding a symbol replaces any existing one.
- [S-14] `sparkRange`: '1d' | '5d' | '1mo' | '3mo' | '6mo' | '1y' | '2y' | '5y' | '10y' (default '1d'); drives both API range and sparkline date labels.
- [S-15] `showSparkline` (default true): toggles inline chart.
- [S-16] `hideHeader`: toggles widget title bar.
- [S-17] `refreshMinutes`: 1 / 5 / 15 / 30 (default 5).
- [S-37] `showExtendedHours` (default false): requests pre/post-market data. Only has effect on the intraday ranges '1d' and '5d'; it is part of the cache key, so the plain and extended variants never share an entry.
- [S-18] Config tabs: Display / Symbol / Refresh.
- [S-19] Symbol tab shows the chosen symbol with an inline-editable display name and a remove button, plus a search form to pick an instrument (a pick replaces the current symbol). The chosen symbol is badged with its [S-38] class, and the symbol field's placeholder is that class's example (`AAPL`, `bitcoin`, `EURUSD=X`, `GC=F`, `^GSPC`), so the symbol grammar can be typed by hand.
- [S-38] Instruments are classified as equity / crypto / currency / commodity / index. Yahoo's `quoteType` decides when a live search result carries one (`EQUITY`/`ETF`/`MUTUALFUND`→equity, `CURRENCY`→currency, `FUTURE`→commodity, `INDEX`→index); a persisted config entry carries only a symbol, so the fallback reads Yahoo's symbol grammar (`^` prefix→index, `=X`→currency, `=F`→commodity, else equity). A `type: 'crypto'` entry is crypto regardless, its id being a CoinGecko id and not a Yahoo symbol. The class drives the search-row icon (TrendingUp / Bitcoin / Banknote / Boxes / ChartCandlestick) and the [S-19] badge; it never changes which endpoint is called.

## Context menu
- [S-20] "Refresh" action triggers immediate reload via `_refresh` bump.

## Data / backend
- [S-21] All HTTP requests go through Tauri `http_get` command (returns text, parsed as JSON).
- [S-22] Everything that is not crypto — equities, ETFs, funds, indices, currency pairs and futures alike — fetches `https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?range={range}&interval={interval}&currency=USD`; interval mapped per range (1d→5m, 5d→15m, 1mo/3mo→1d, 6mo/1y/2y→1wk, 5y/10y→1mo). There is no per-class request path; the class only affects presentation ([S-38], [S-39]).
- [S-40] Symbol search merges Yahoo `v1/finance/search` with CoinGecko `v3/search`. Yahoo hits are kept for quote types `EQUITY`, `ETF`, `MUTUALFUND`, `INDEX`, `CURRENCY` and `FUTURE` — the classes the [S-22] endpoint quotes. `OPTION` is excluded: it needs a strike and expiry the widget has nowhere to show.
- [S-23] Stocks price from `meta.regularMarketPrice` (fallback last close); change vs `meta.chartPreviousClose` or `meta.previousClose`; sparkline from `indicators.quote[0].close` zipped with `timestamp`, dropping any sample where either half is missing (Yahoo pads illiquid minutes with nulls).
- [S-34] With [S-37] on and an intraday range, the request adds `&includePrePost=true`; the extended quote is the last bar inside the `pre`/`post` window, with its % measured against `meta.regularMarketPrice` (the last regular close, which is the correct baseline in both windows). Derived from the bars because this endpoint exposes no pre/post price fields, and the endpoints that do (`v7/finance/quote`, `v10/finance/quoteSummary`) are cookie+crumb walled and answer 401.
- [S-24] Crypto: fetches CoinGecko `simple/price?ids=&vs_currencies=usd&include_24hr_change=true` for price and 24h change.
- [S-25] Crypto sparkline from `coins/{id}/market_chart?vs_currency=usd&days={days}` where days mapped per range (1d→1, 5d→5, 1mo→30, 3mo→90, 6mo→180, 1y→365, 2y→730, 5y→1825, 10y→3650); sparkline fetch failures don't fail the ticker.
- [S-26] Currency hardcoded to USD.
- [S-27] Polls every `refreshMinutes` minutes via `createPolling`.
- [S-28] Demo mode with empty symbols list injects AAPL, MSFT, NVDA, bitcoin fixture entries before fetching.

## Caching
- [S-29] Cached ticker data persists across app restarts (localStorage); cold launch shows last-known prices instantly without "Loading..." flash.
- [S-30] Stocks widgets configured with the same symbol set and sparkRange share a single cache entry: only one batch of API requests fires per refresh.
- [S-31] On fetch failure when cached tickers are present, last-known tickers remain visible (error state suppressed).
