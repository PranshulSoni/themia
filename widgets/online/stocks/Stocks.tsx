import { createMemo, createSignal } from 'solid-js';
import { createPolling } from '../../../src/createPolling';
import {
  barSegments, barsOf, extendedQuotesOf, isExtendedPhase, marketPhase, sessionsOf,
  type Bar, type CurrentTradingPeriod, type ExtendedQuote, type ExtendedQuotes,
  type MarketPhase, type TradingSessions,
} from './marketSession.ts';
import { chartLabels, zoneAbbrev } from './exchangeTime.ts';
import { formatPrice } from './instrument.ts';
import { createCachedFetch } from '../../../src/createCachedFetch';
import { createManualRefresh } from '../../office/calendar/manualRefresh.ts';
import { Show, Switch, Match, For, Dynamic } from 'solid-js/web';
import { invoke, isDemo } from '../../../src/demo/invoke.ts';
import { TrendingUp, Moon, Sunrise, Sunset } from 'lucide-solid';
import { t } from '../../../src/i18n.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './Stocks.css';

interface SymbolEntry {
  symbol: string;
  type: 'stock' | 'crypto';
  name?: string;
}

interface TickerData {
  symbol: string;
  type: 'stock' | 'crypto';
  name?: string;
  currency?: string;
  price: number;
  change: number;
  sparkline: number[];
  /** Epoch seconds for each `sparkline` sample; same length, same order. */
  sparkTimes?: number[];
  /**
   * The day's pre/regular/post windows (epoch seconds) for stocks; absent for
   * crypto and for payloads that didn't carry them. Stored rather than resolved
   * to a phase at fetch time so the badge is judged against the clock at
   * render, not against whenever the response happened to arrive — [S-33].
   */
  sessions?: TradingSessions;
  /** IANA zone of the listing exchange, for rendering its clock times [S-36]. */
  exchangeTz?: string;
  /** Yahoo's `hasPrePostMarketData`: does this instrument trade extended hours? */
  hasExtendedHours?: boolean;
  /**
   * Yahoo's `meta.priceHint`: the venue's tick precision, in decimal places.
   * Absent on crypto and on tickers cached before [S-39] shipped, which fall
   * back to the magnitude rule.
   */
  priceHint?: number;
  /** Last pre/post-market print, when extended hours were requested [S-34]. */
  extended?: ExtendedQuotes;
}

interface StocksProps {
  symbols?: SymbolEntry[];
  refreshMinutes?: number;
  showSparkline?: boolean;
  sparkRange?: string;
  headerMode?: HeaderMode;
  label?: string;
  areaFill?: boolean;
  customColors?: boolean;
  upColor?: string;
  downColor?: string;
  showMinMax?: boolean;
  dimWhenClosed?: boolean;
  showExtendedHours?: boolean;
  _refresh?: number;
}

/** Ranges whose bars are intraday, the only ones extended hours can apply to
 *  (a daily or weekly candle has no inside-the-day structure to extend). */
const INTRADAY_RANGES = new Set(['1d', '5d']);

const RANGE_INTERVALS: Record<string, string> = {
  '1d': '5m',
  '5d': '15m',
  '1mo': '1d',
  '3mo': '1d',
  '6mo': '1wk',
  '1y': '1wk',
  '2y': '1wk',
  '5y': '1mo',
  '10y': '1mo',
};

const RANGE_DAYS: Record<string, number> = {
  '1d': 1,
  '5d': 5,
  '1mo': 30,
  '3mo': 90,
  '6mo': 180,
  '1y': 365,
  '2y': 730,
  '5y': 1825,
  '10y': 3650,
};

type Point = readonly [number, number];

/** Project the series onto the viewbox once, so every segment of the line
 *  shares one min/max scale instead of being normalised per run. */
function sparkPoints(data: number[], w: number, h: number): Point[] {
  if (data.length < 2) return [];
  const min = Math.min(...data);
  const max = Math.max(...data);
  const range = max - min || 1;
  const step = w / (data.length - 1);
  return data.map((v, i) => [i * step, h - ((v - min) / range) * h] as const);
}

/** Polyline over `points[from..to)`. Runs are drawn one point wider on the left
 *  so consecutive segments meet instead of leaving a gap at the handover. */
function pathOf(points: Point[], from = 0, to = points.length): string {
  const start = Math.max(0, from - (from > 0 ? 1 : 0));
  const seg = points.slice(start, to);
  if (seg.length < 2) return '';
  return seg.map(([x, y], i) => `${i === 0 ? 'M' : 'L'}${x.toFixed(1)},${y.toFixed(1)}`).join(' ');
}

function buildAreaPath(points: Point[], w: number, h: number): string {
  const line = pathOf(points);
  if (!line) return '';
  return `${line} L${w.toFixed(1)},${h.toFixed(1)} L0,${h.toFixed(1)} Z`;
}

function minMaxIndices(data: number[]): { min: number; max: number } | null {
  if (data.length < 2) return null;
  let minI = 0, maxI = 0;
  for (let i = 1; i < data.length; i++) {
    if (data[i] < data[minI]) minI = i;
    if (data[i] > data[maxI]) maxI = i;
  }
  return { min: minI, max: maxI };
}

async function httpGet<T>(url: string): Promise<T> {
  const text = await invoke('http_get', { url });
  return JSON.parse(text) as T;
}

interface YahooMeta {
  regularMarketPrice?: number;
  chartPreviousClose?: number;
  previousClose?: number;
  currentTradingPeriod?: CurrentTradingPeriod;
  exchangeTimezoneName?: string;
  hasPrePostMarketData?: boolean;
  currency?: string;
  priceHint?: number;
}

interface YahooResult {
  meta: YahooMeta;
  timestamp?: (number | null)[];
  indicators?: { quote?: { close?: (number | null)[] }[] };
}

interface YahooChartResponse {
  chart?: { result?: YahooResult[] };
}

async function fetchStock(symbol: string, currency: string, range: string, extendedHours: boolean): Promise<TickerData> {
  const interval = RANGE_INTERVALS[range] ?? '5m';
  // [S-34] `includePrePost` widens the returned bars to the pre/post windows.
  // Only asked for on intraday ranges — a daily or weekly candle has no
  // extended-hours structure, and the flag would just be ignored.
  const wantExtended = extendedHours && INTRADAY_RANGES.has(range);
  const prePost = wantExtended ? '&includePrePost=true' : '';
  const json = await httpGet<YahooChartResponse>(`https://query1.finance.yahoo.com/v8/finance/chart/${encodeURIComponent(symbol)}?range=${range}&interval=${interval}&currency=${currency}${prePost}`);
  const result = json.chart?.result?.[0];
  if (!result) throw new Error('No data');
  const meta = result.meta;
  const bars = barsOf(result.timestamp, result.indicators?.quote?.[0]?.close);
  const price = meta.regularMarketPrice ?? bars[bars.length - 1]?.c ?? 0;
  const prev = meta.chartPreviousClose ?? meta.previousClose ?? price;
  const change = prev ? ((price - prev) / prev) * 100 : 0;
  const sessions = sessionsOf(meta.currentTradingPeriod);
  const ticker: TickerData = {
    symbol,
    type: 'stock',
    currency: meta.currency ?? 'USD',
    price,
    change,
    sparkline: bars.map(b => b.c),
    sparkTimes: bars.map(b => b.t),
    sessions,
    hasExtendedHours: meta.hasPrePostMarketData === true,
  };
  if (typeof meta.priceHint === 'number') ticker.priceHint = meta.priceHint;
  if (meta.exchangeTimezoneName) ticker.exchangeTz = meta.exchangeTimezoneName;
  // The extended quote is derived from the same bars: Yahoo exposes no
  // pre/postMarketPrice on this endpoint, and the endpoints that do
  // (v7/quote, v10/quoteSummary) are cookie+crumb walled — both 401 without.
  if (wantExtended) ticker.extended = extendedQuotesOf(bars, sessions, meta.regularMarketPrice);
  return ticker;
}

async function fetchCrypto(id: string, currency: string, range: string): Promise<TickerData> {
  const cur = currency.toLowerCase();
  const days = RANGE_DAYS[range] ?? 1;
  const [priceJson, chartJson] = await Promise.all([
    httpGet<Record<string, Record<string, number>>>(`https://api.coingecko.com/api/v3/simple/price?ids=${encodeURIComponent(id)}&vs_currencies=${cur}&include_24hr_change=true`),
    httpGet<{ prices?: [number, number][] }>(`https://api.coingecko.com/api/v3/coins/${encodeURIComponent(id)}/market_chart?vs_currency=${cur}&days=${days}`).catch(() => null),
  ]);
  const data = priceJson[id];
  if (!data) throw new Error('Unknown coin');
  const price = data[cur] ?? 0;
  const change = data[`${cur}_24h_change`] ?? 0;
  // CoinGecko timestamps are ms; the widget works in epoch seconds throughout.
  const points = (chartJson?.prices ?? []).filter(
    (p): p is [number, number] => Array.isArray(p) && Number.isFinite(p[0]) && Number.isFinite(p[1]),
  );
  // No session windows: crypto trades 24/7, is never badged as closed, and has
  // no listing exchange whose zone the chart labels should follow.
  return {
    symbol: id,
    type: 'crypto',
    currency: currency.toUpperCase(),
    price,
    change,
    sparkline: points.map(p => p[1]),
    sparkTimes: points.map(p => Math.round(p[0] / 1000)),
  };
}

export default function Stocks(props: StocksProps) {
  // At most one symbol per widget; legacy configs with several collapse to the first.
  const symbols = createMemo<SymbolEntry[]>(() => {
    const raw = (props.symbols ?? []) as SymbolEntry[];
    if (!raw.length && isDemo()) {
      return [{ symbol: 'AAPL', type: 'stock', name: 'Apple' }];
    }
    return raw.slice(0, 1);
  });

  const currency = () => 'USD';
  const sparkRange = () => props.sparkRange ?? '1d';
  const showSparkline = () => props.showSparkline ?? true;
  const extendedHours = () => !!props.showExtendedHours;
  const refreshMs = createMemo(() => Math.max(1, props.refreshMinutes ?? 5) * 60_000);

  const strokeFor = (up: boolean) => {
    if (props.customColors) return up ? (props.upColor ?? '#22c55e') : (props.downColor ?? '#ef4444');
    return up ? '#22c55e' : 'var(--destructive)';
  };

  const cache = createCachedFetch<TickerData[]>({
    namespace: 'stocks',
    key: () => {
      const syms = symbols();
      if (!syms.length) return null;
      // Cosmetic display name (s.name) is deliberately excluded: it does not
      // affect what is fetched, so renaming must not re-key the cache (which
      // would blank the widget and refire a request per keystroke) — S-30.
      const symKeys = syms.map(s => `${s.type}:${s.symbol}`).join(',');
      // Extended hours IS part of the key: it changes the request URL and the
      // bars that come back, so the two variants must not share a cache entry.
      return `${symKeys}::${sparkRange()}::${currency()}::ext${extendedHours() ? 1 : 0}`;
    },
    staleMs: refreshMs,
    fetcher: async (_key, signal) => {
      const syms = symbols();
      const range = sparkRange();
      const ext = extendedHours();
      const results = await Promise.allSettled(
        syms.map(async s => {
          const ticker = s.type === 'crypto'
            ? await fetchCrypto(s.symbol, currency(), range)
            : await fetchStock(s.symbol, currency(), range, ext);
          if (s.name) ticker.name = s.name;
          return ticker;
        })
      );
      // A Tauri invoke can't be server-cancelled; if this fetch was superseded
      // while the requests were in flight, drop the result so it doesn't write
      // into the (now stale) shared state.
      if (signal.aborted) throw new DOMException('aborted', 'AbortError');
      const data = results
        .filter((r): r is PromiseFulfilledResult<TickerData> => r.status === 'fulfilled')
        .map(r => r.value);
      if (!data.length) throw new Error('Failed to fetch any data');
      return data;
    },
  });

  createPolling(() => { void cache.refresh(true); }, refreshMs, 'stocks');

  // [S-20] Widget-level "Refresh" context action bumps the `_refresh` config
  // token; force a re-fetch when it changes. The mount run is skipped
  // (createCachedFetch's own effect covers first load), so a `_refresh` token
  // persisted in config from a previous session can't force a redundant network
  // fetch on app start.
  createManualRefresh(() => props._refresh, () => { void cache.refresh(true); });

  // [S-33] The badge is judged against the wall clock, not against the moment
  // the response landed: a cold launch renders cached tickers from a previous
  // session, and a widget on a 30-minute refresh must still flip at the closing
  // bell rather than up to half an hour later. A one-minute tick (paused while
  // the app is inactive) is enough resolution for an open/close boundary.
  const [nowMs, setNowMs] = createSignal(Date.now());
  createPolling(() => setNowMs(Date.now()), () => 60_000);

  const tickers = createMemo<TickerData[]>(() => cache.data() ?? []);

  const singleTicker = createMemo<TickerData | null>(() => tickers()[0] ?? null);

  // Header shows the configured symbol's name (immediately, before any fetch),
  // falling back to the label override or "Stocks".
  const headerTitle = createMemo<string>(() => {
    const s = symbols()[0];
    if (s) return s.name || s.symbol.toUpperCase();
    return props.label || t('widget.stocks.label');
  });

  /** Bars of the plotted series, rebuilt from the two parallel arrays. */
  const bars = createMemo<Bar[]>(() => {
    const tk = singleTicker();
    if (!tk) return [];
    return barsOf(tk.sparkTimes, tk.sparkline);
  });

  /**
   * [S-36] Axis labels come from the plotted bars and are rendered in the
   * listing exchange's zone, so a viewer anywhere reads the session's own
   * clock. Falls back to the old now-minus-range guess only when the cached
   * ticker predates `sparkTimes`.
   */
  function rangeDates(): [string, string] {
    const range = sparkRange();
    const days = RANGE_DAYS[range] ?? 1;
    const labels = chartLabels(bars(), {
      tz: singleTicker()?.exchangeTz,
      intraday: INTRADAY_RANGES.has(range),
      withYear: days > 365,
    });
    if (labels) return labels;
    const now = new Date();
    const start = new Date(now.getTime() - days * 86400000);
    if (INTRADAY_RANGES.has(range)) {
      const fmt = (d: Date) => d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
      return [fmt(start), fmt(now)];
    }
    const fmt = (d: Date) => d.toLocaleDateString([], { month: 'short', day: 'numeric', year: days > 365 ? 'numeric' : undefined });
    return [fmt(start), fmt(now)];
  }

  /** [S-39] Quote precision follows the instrument's own tick size. */
  const quote = (price: number) => formatPrice(price, singleTicker()?.priceHint);

  /**
   * Which session the exchange is in right now. Re-evaluated on the minute
   * tick, so the phase is judged against the clock rather than against whenever
   * the response landed (#100 / [S-33]). Crypto has no sessions and is always
   * 'open'; an unusable payload stays 'unknown' and is never called closed.
   */
  const phase = createMemo<MarketPhase>(() => {
    const tk = singleTicker();
    if (!tk) return 'unknown';
    if (tk.type === 'crypto') return 'open';
    return marketPhase(tk.sessions, nowMs());
  });

  /**
   * [S-35] The live extended-hours print, when one is actually running: the
   * toggle is on, the clock is inside a pre/post window, the instrument trades
   * extended hours at all, and we have a print from that window. Anything
   * missing means after-hours trading is NOT active for this ticker, and the
   * plain "Market closed" badge is shown instead.
   */
  const extendedNow = createMemo<ExtendedQuote | null>(() => {
    const tk = singleTicker();
    const p = phase();
    if (!tk || !extendedHours() || !isExtendedPhase(p)) return null;
    if (!tk.hasExtendedHours) return null;
    return tk.extended?.[p] ?? null;
  });

  /**
   * What the badge says. `null` hides it: during regular trading, for crypto,
   * for an unknown schedule (#100), and when the indicator is switched off.
   */
  const badge = createMemo<{ kind: 'pre' | 'post' | 'closed'; label: string } | null>(() => {
    if (!(props.dimWhenClosed ?? true)) return null;
    const p = phase();
    if (p === 'open' || p === 'unknown') return null;
    // An extended window is only worth naming when something trades in it;
    // for an index or a venue without pre/post it is simply closed.
    if (extendedNow()) {
      return p === 'pre'
        ? { kind: 'pre', label: t('stocks.preMarket') }
        : { kind: 'post', label: t('stocks.afterHours') };
    }
    return { kind: 'closed', label: t('stocks.marketClosed') };
  });

  const badgeIcon = (kind: 'pre' | 'post' | 'closed') =>
    kind === 'pre' ? Sunrise : kind === 'post' ? Sunset : Moon;

  /** Zone marker ("EDT") for the times shown, empty when they are already local. */
  const tzLabel = createMemo<string>(() => {
    const tk = singleTicker();
    if (!tk?.exchangeTz) return '';
    return zoneAbbrev(nowMs() / 1000, tk.exchangeTz);
  });

  return (
    <div class="stocks-widget">
      <WidgetHeader
        mode={props.headerMode ?? 'normal'}
        icon={TrendingUp}
        title={headerTitle()}
      />
      <div class="widget-body">
      <Switch>
        <Match when={!symbols().length}>
          <div class="stocks-widget__msg"><span innerHTML={t('stocks.selectSymbols')} /></div>
        </Match>
        <Match when={cache.loading() && !tickers().length}>
          <div class="stocks-widget__msg">{t('widgetUi.loading')}</div>
        </Match>
        <Match when={cache.error() && !tickers().length}>
          <div class="stocks-widget__msg stocks-widget__msg--error">{cache.error()}</div>
        </Match>
        <Match when={singleTicker()}>
          {(() => {
            const tk = singleTicker();
            if (!tk) return null;
            const mm = minMaxIndices(tk.sparkline);
            return (
              <div class="stocks-single">
                <div class="stocks-single__top">
                  <span class="stocks-single__change" classList={{ up: tk.change >= 0, down: tk.change < 0 }}>
                    {tk.change >= 0 ? '+' : ''}{tk.change.toFixed(2)}%
                  </span>
                </div>
                <span class="stocks-single__price">{quote(tk.price)} <span class="stocks-single__currency">{tk.currency}</span></span>
                <Show when={badge()}>
                  {(b) => (
                    <span
                      class="stocks-widget__closed-badge"
                      classList={{ 'stocks-widget__closed-badge--active': b().kind !== 'closed' }}
                      title={tzLabel() ? `${b().label} · ${tzLabel()}` : b().label}
                    >
                      <Dynamic component={badgeIcon(b().kind)} size={10} strokeWidth={1.5} />{b().label}
                    </span>
                  )}
                </Show>
                {/* [S-35] The extended print, so "after hours" is a number and
                    not just a word: last trade plus its move off the close. */}
                <Show when={extendedNow()}>
                  {(ext) => (
                    <span class="stocks-single__extended">
                      <span class="stocks-single__extended-price">{quote(ext().price)}</span>
                      <span
                        class="stocks-single__extended-change"
                        classList={{ up: ext().changePct >= 0, down: ext().changePct < 0 }}
                      >
                        {ext().changePct >= 0 ? '+' : ''}{ext().changePct.toFixed(2)}%
                      </span>
                    </span>
                  )}
                </Show>
                <Show when={showSparkline() && tk.sparkline.length >= 2}>
                  {(() => {
                    const [startDate, endDate] = rangeDates();
                    const stroke = strokeFor(tk.change >= 0);
                    const points = sparkPoints(tk.sparkline, 200, 60);
                    // [S-35] Extended-hours bars are drawn dashed and faded, so
                    // the pre/post part of the session is visible as such at a
                    // glance. Only on '1d': `currentTradingPeriod` describes
                    // TODAY only and Yahoo sends no per-day `tradingPeriods` on
                    // multi-day ranges, so on '5d' every earlier day's regular
                    // bars would fall outside it and be mislabelled extended.
                    // Without a schedule we can prove, the line stays one run.
                    const segments = barSegments(bars(), sparkRange() === '1d' ? tk.sessions : undefined);
                    return (
                      <div class="stocks-single__chart-wrap">
                        <svg class="stocks-single__chart" viewBox="0 0 200 60" preserveAspectRatio="none">
                          <Show when={props.areaFill}>
                            <path
                              d={buildAreaPath(points, 200, 60)}
                              fill={stroke}
                              fill-opacity="0.15"
                              stroke="none"
                            />
                          </Show>
                          <For each={segments}>
                            {(seg) => (
                              <path
                                d={pathOf(points, seg.from, seg.to)}
                                fill="none"
                                stroke={stroke}
                                stroke-width="2"
                                stroke-dasharray={seg.extended ? '3 2' : undefined}
                                stroke-opacity={seg.extended ? 0.55 : undefined}
                              />
                            )}
                          </For>
                          <Show when={props.showMinMax && mm}>
                            {(() => {
                              const data = tk.sparkline;
                              const w = 200, h = 60;
                              const minV = Math.min(...data);
                              const maxV = Math.max(...data);
                              const range = maxV - minV || 1;
                              const step = w / (data.length - 1);
                              const pt = (i: number) => [i * step, h - ((data[i] - minV) / range) * h] as const;
                              const [lx, ly] = pt(mm!.min);
                              const [hx, hy] = pt(mm!.max);
                              return <>
                                <circle cx={lx} cy={ly} r="2.5" class="stocks-widget__minmax-dot" />
                                <circle cx={hx} cy={hy} r="2.5" class="stocks-widget__minmax-dot" />
                              </>;
                            })()}
                          </Show>
                        </svg>
                        <span class="stocks-single__date stocks-single__date--start">{startDate}</span>
                        <span class="stocks-single__date stocks-single__date--end">{endDate}</span>
                      </div>
                    );
                  })()}
                </Show>
              </div>
            );
          })()}
        </Match>
      </Switch>
      </div>
    </div>
  );
}
