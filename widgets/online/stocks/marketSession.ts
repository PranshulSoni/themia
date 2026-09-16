/**
 * Trading-session logic for the stocks widget.
 *
 * Yahoo's v8 chart `meta` does NOT carry a `marketState` field (#100). The
 * widget used to test `meta.marketState === 'REGULAR'`, which is `undefined`
 * for every symbol and every range, so every stock rendered a permanent
 * "Market closed" badge — including while the exchange was trading.
 *
 * The signal that payload really carries is `meta.currentTradingPeriod`: `pre`,
 * `regular` and `post` windows whose bounds are epoch SECONDS, already resolved
 * for the exchange's own calendar and DST (holidays, half days, non-US venues).
 * Everything here therefore compares absolute instants only — there is no local
 * time, no timezone maths, and no assumption that the user sits in the
 * exchange's zone. Rendering a bound as a wall-clock string is the one place
 * that needs a timezone, and lives in `exchangeTime.ts`.
 */

/** One Yahoo trading window as received; bounds are epoch seconds. */
export interface TradingWindow {
  start?: number | null;
  end?: number | null;
}

/** `meta.currentTradingPeriod` as returned by the v8 chart endpoint. */
export interface CurrentTradingPeriod {
  pre?: TradingWindow | null;
  regular?: TradingWindow | null;
  post?: TradingWindow | null;
}

/** A validated window, epoch seconds, `start < end`. */
export interface SessionWindow {
  start: number;
  end: number;
}

/** The day's session windows, each present only when the API sent a usable one. */
export interface TradingSessions {
  pre?: SessionWindow;
  regular?: SessionWindow;
  post?: SessionWindow;
}

/**
 * Which session is running right now.
 *
 * `unknown` is deliberately distinct from `closed`: a payload without a usable
 * regular window says nothing about the schedule, and must never be rendered as
 * "Market closed" — that conflation is the #100 bug.
 */
export type MarketPhase = 'pre' | 'open' | 'post' | 'closed' | 'unknown';

/** Last extended-hours print of a session, with the move it implies. */
export interface ExtendedQuote {
  /** Last traded price inside the window. */
  price: number;
  /** Percent vs the last regular-session close (what Yahoo/Google show). */
  changePct: number;
  /** Timestamp of that print, epoch seconds. */
  at: number;
}

/** Extended-hours prints for the day, keyed by the window they fell in. */
export interface ExtendedQuotes {
  pre?: ExtendedQuote;
  post?: ExtendedQuote;
}

function isFiniteNumber(v: unknown): v is number {
  return typeof v === 'number' && Number.isFinite(v);
}

function validate(window: unknown): SessionWindow | null {
  if (!window || typeof window !== 'object') return null;
  const { start, end } = window as TradingWindow;
  if (!isFiniteNumber(start) || !isFiniteNumber(end)) return null;
  if (end <= start) return null;
  return { start, end };
}

/**
 * Narrow a raw `currentTradingPeriod` to the windows that are actually usable.
 * Windows are validated independently: a broken `post` must not cost us the
 * `regular` one.
 */
export function sessionsOf(period: CurrentTradingPeriod | undefined | null): TradingSessions {
  const out: TradingSessions = {};
  const pre = validate(period?.pre);
  const regular = validate(period?.regular);
  const post = validate(period?.post);
  if (pre) out.pre = pre;
  if (regular) out.regular = regular;
  if (post) out.post = post;
  return out;
}

/** True when `nowSec` falls in `[start, end)`. Windows are half-open so the
 *  adjacent pre/regular/post bounds can't both claim the same instant. */
function contains(window: SessionWindow | undefined | null, nowSec: number): boolean {
  return !!window && nowSec >= window.start && nowSec < window.end;
}

/**
 * Classify the current instant. `sessions` is re-validated on every call
 * because it round-trips through the persisted ticker cache, a trust boundary.
 *
 * @param nowMs Current wall clock in ms, injected so this stays pure.
 */
export function marketPhase(sessions: TradingSessions | undefined | null, nowMs: number): MarketPhase {
  const pre = validate(sessions?.pre);
  const regular = validate(sessions?.regular);
  const post = validate(sessions?.post);
  const nowSec = nowMs / 1000;

  if (contains(regular, nowSec)) return 'open';
  if (contains(pre, nowSec)) return 'pre';
  if (contains(post, nowSec)) return 'post';
  // Knowing today's regular window is what lets us say "closed" rather than
  // "no idea": outside it, and outside any extended window, nothing trades.
  if (regular) return 'closed';
  return 'unknown';
}

/** The extended-hours phases, for callers that branch on them. */
export function isExtendedPhase(phase: MarketPhase): phase is 'pre' | 'post' {
  return phase === 'pre' || phase === 'post';
}

/** One chart sample: epoch seconds + close. Nulls are dropped upstream. */
export interface Bar {
  t: number;
  c: number;
}

/**
 * Zip Yahoo's parallel `timestamp` / `close` arrays into bars, dropping any
 * sample whose either half is missing. Yahoo pads illiquid extended-hours
 * minutes with nulls, and dropping a close without dropping its timestamp
 * would shear the two arrays out of alignment.
 */
export function barsOf(timestamps: unknown, closes: unknown): Bar[] {
  const ts = Array.isArray(timestamps) ? timestamps : [];
  const cs = Array.isArray(closes) ? closes : [];
  const out: Bar[] = [];
  for (let i = 0; i < cs.length; i++) {
    const t = ts[i];
    const c = cs[i];
    if (isFiniteNumber(t) && isFiniteNumber(c)) out.push({ t, c });
  }
  return out;
}

/**
 * Last print inside `window`, expressed against `regularClose`.
 *
 * `regularClose` is `meta.regularMarketPrice`, which is the correct baseline in
 * both directions: during post-market it is today's closing print, and during
 * the next pre-market it is still that same close (no regular trade has
 * happened yet), which is exactly what an extended-hours quote is measured
 * against.
 */
export function extendedQuoteIn(
  bars: Bar[],
  window: SessionWindow | undefined,
  regularClose: number | undefined,
): ExtendedQuote | undefined {
  const w = validate(window);
  if (!w || !isFiniteNumber(regularClose) || regularClose <= 0) return undefined;
  for (let i = bars.length - 1; i >= 0; i--) {
    const bar = bars[i];
    if (bar.t < w.start || bar.t >= w.end) continue;
    return {
      price: bar.c,
      changePct: ((bar.c - regularClose) / regularClose) * 100,
      at: bar.t,
    };
  }
  return undefined;
}

/** Both extended-hours quotes for the day, from one pass of the same bars. */
export function extendedQuotesOf(
  bars: Bar[],
  sessions: TradingSessions,
  regularClose: number | undefined,
): ExtendedQuotes {
  const out: ExtendedQuotes = {};
  const pre = extendedQuoteIn(bars, sessions.pre, regularClose);
  const post = extendedQuoteIn(bars, sessions.post, regularClose);
  if (pre) out.pre = pre;
  if (post) out.post = post;
  return out;
}

/** A contiguous run of bars that are all regular-session or all extended. */
export interface BarSegment {
  /** First bar index, inclusive. */
  from: number;
  /** Last bar index, exclusive. */
  to: number;
  /** True for pre-market and after-hours bars. */
  extended: boolean;
}

/**
 * Split bars into runs so the sparkline can draw extended hours differently
 * from the regular session — the visual half of "is after-hours trading
 * happening" [S-35]. A 1D chart with extended hours on is typically three runs
 * (pre, regular, post); without a usable regular window everything is one
 * regular run, since we can't claim any bar is extended.
 */
export function barSegments(bars: Bar[], sessions: TradingSessions | undefined): BarSegment[] {
  if (!bars.length) return [];
  const regular = validate(sessions?.regular);
  if (!regular) return [{ from: 0, to: bars.length, extended: false }];

  const out: BarSegment[] = [];
  let from = 0;
  let current = !contains(regular, bars[0].t);
  for (let i = 1; i < bars.length; i++) {
    const extended = !contains(regular, bars[i].t);
    if (extended !== current) {
      out.push({ from, to: i, extended: current });
      from = i;
      current = extended;
    }
  }
  out.push({ from, to: bars.length, extended: current });
  return out;
}
