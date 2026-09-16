// Pure helpers for the Claude usage widget. Kept framework-free so they can be
// unit-tested without a DOM (see claudeUsage.test.ts).

export interface UsageWindow {
  utilization: number;
  resets_at: string | null;
}

export interface ClaudeUsage {
  five_hour: UsageWindow;
  seven_day: UsageWindow;
  seven_day_opus: UsageWindow | null;
  seven_day_sonnet: UsageWindow | null;
}

export type Severity = 'ok' | 'warn' | 'crit';

/** A row in the widget, with its label at three widths ([S-18]). */
export interface UsageBar {
  key: string;
  label: string;
  /** Mid width: the qualifier only. */
  short: string;
  /** Narrowest: a couple of characters, still unambiguous between rows. */
  mini: string;
  win: UsageWindow;
}

/**
 * Rows to render. The two base windows are always present; a per-model row is
 * added when its toggle is on. A per-model window the endpoint reports as null
 * (Opus/Sonnet return null when there's no active limit) is shown as a 0% bar
 * rather than hidden, so a toggled-on model always renders a bar. [S-04]
 */
export function buildBars(u: ClaudeUsage, showOpus: boolean, showSonnet: boolean): UsageBar[] {
  const empty: UsageWindow = { utilization: 0, resets_at: null };
  const bars: UsageBar[] = [
    { key: 'five_hour', label: 'Session (5h)', short: 'Session', mini: '5h', win: u.five_hour },
    { key: 'seven_day', label: 'Weekly (all models)', short: 'Weekly', mini: '7d', win: u.seven_day },
  ];
  if (showOpus) {
    bars.push({
      key: 'opus', label: 'Weekly (Opus)', short: 'Opus', mini: 'Opus',
      win: u.seven_day_opus ?? empty,
    });
  }
  if (showSonnet) {
    bars.push({
      key: 'sonnet', label: 'Weekly (Sonnet)', short: 'Sonnet', mini: 'Son.',
      win: u.seven_day_sonnet ?? empty,
    });
  }
  return bars;
}

export const DEFAULT_REFRESH_MINUTES = 15;
export const MIN_REFRESH_MINUTES = 5;
export const MAX_REFRESH_MINUTES = 60;

/**
 * Clamp the endpoint's `utilization` to a 0..100 percentage.
 *
 * `utilization` is ALREADY a percentage (a live Max 5x payload reports
 * `42.5` for 42.5%, see the `SAMPLE` fixture in email/src/lib.rs). An earlier
 * version guessed: values at or below 1 were treated as a 0..1 fraction and
 * multiplied by 100, which turned a real 1% session into a full red 100% bar
 * (and 0.6% into 60%). No scaling — clamp only. [U-01]
 */
export function utilizationPct(u: number): number {
  if (!Number.isFinite(u) || u <= 0) return 0;
  return Math.min(100, u);
}

/** Severity bucket driving the bar color. */
export function usageSeverity(pct: number): Severity {
  if (pct >= 90) return 'crit';
  if (pct >= 70) return 'warn';
  return 'ok';
}

/**
 * Human "resets in 3h 20m" from an ISO-8601 timestamp. Returns '' when the
 * timestamp is null (window unused), already past, or unparseable. `nowMs` is
 * injected so the formatting is deterministic in tests.
 *
 * `short` drops the "resets in " prefix ("3h 20m"), for a widget too narrow to
 * spell it out — the line sits under its own bar, so the duration alone still
 * reads. [S-18]
 */
export function resetLabel(resetsAt: string | null, nowMs: number, short = false): string {
  if (!resetsAt) return '';
  const ts = new Date(resetsAt).getTime();
  if (Number.isNaN(ts)) return '';
  const diff = ts - nowMs;
  if (diff <= 0) return '';
  const totalMins = Math.floor(diff / 60_000);
  const days = Math.floor(totalMins / (60 * 24));
  const hours = Math.floor((totalMins % (60 * 24)) / 60);
  const mins = totalMins % 60;
  const prefix = short ? '' : 'resets in ';
  if (days > 0) return `${prefix}${days}d ${hours}h`;
  if (hours > 0) return `${prefix}${hours}h ${mins}m`;
  return `${prefix}${mins}m`;
}

/** Clamp a configured refresh interval to the allowed range. */
export function clampRefreshMinutes(m: number | undefined): number {
  if (!Number.isFinite(m as number)) return DEFAULT_REFRESH_MINUTES;
  return Math.min(MAX_REFRESH_MINUTES, Math.max(MIN_REFRESH_MINUTES, Math.round(m as number)));
}

export type UsageView = 'empty' | 'loading' | 'error' | 'loaded';

/**
 * Decide which state the widget renders, framework-free so it's testable.
 *
 * A CONNECTED account (or demo) is never 'empty': with no data and no error yet
 * a fetch is in flight, so we show 'loading'. We deliberately do NOT consult a
 * per-instance loading flag here — when two widget instances share a cache key,
 * the second instance's refresh returns early on the shared `inflight` promise
 * without flipping its own loading signal, which used to make it fall through to
 * the no-account empty state ("Connect a Claude account") while connected.
 */
export function usageView(state: {
  hasAccount: boolean;
  hasData: boolean;
  hasError: boolean;
}): UsageView {
  if (!state.hasAccount) return 'empty';
  if (state.hasData) return 'loaded';
  if (state.hasError) return 'error';
  return 'loading';
}

/**
 * How much of a row is drawn. The widget never scrolls: it drops the parts a
 * short box can't hold, densest-that-fits wins. [S-17]
 *
 * - `full`    label + percentage, bar, "resets in …" line
 * - `compact` label + percentage, bar
 * - `tight`   label · bar · percentage on ONE line
 * - `micro`   the same line, smaller, rows sharing whatever height is left
 */
export type Density = 'full' | 'compact' | 'tight' | 'micro';

/**
 * Per-row height budget for each density, in CSS px, split so the text part can
 * follow the global font scale. MUST stay in sync with ClaudeUsage.css:
 * `text` = the row's text lines at scale 1, `fixed` = the bar and the row's own
 * inner gaps, `gap` = the gap BETWEEN rows.
 */
const DENSITY_BUDGET = {
  full: { text: 30, fixed: 14, gap: 12 },
  compact: { text: 17, fixed: 10, gap: 8 },
  tight: { text: 16, fixed: 1, gap: 6 },
} as const;

/** Densities that have a height floor, loosest first. `micro` is the fallback. */
const MEASURED_DENSITIES = ['full', 'compact', 'tight'] as const;

function safeScale(fontScale: number): number {
  return Number.isFinite(fontScale) && fontScale > 0 ? fontScale : 1;
}

/**
 * Pick the densest layout whose rows fit `bodyH` (the measured content height
 * available to the rows, padding already excluded).
 *
 * An UNMEASURED box (`NaN`, before the first measurement) or a widget with no
 * rows yet returns `full` — the loosest layout, which is what a freshly placed
 * default-sized widget wants. A MEASURED zero is the opposite case: a widget so
 * short its header and padding already used the box up, which must land on
 * `micro`, the only layout with no height floor. [S-17]
 */
export function usageDensity(bodyH: number, rows: number, fontScale = 1): Density {
  if (!Number.isFinite(bodyH) || rows <= 0) return 'full';
  if (bodyH <= 0) return 'micro';
  const fs = safeScale(fontScale);
  for (const d of MEASURED_DENSITIES) {
    const b = DENSITY_BUDGET[d];
    if (bodyH >= rows * (b.text * fs + b.fixed) + (rows - 1) * b.gap) return d;
  }
  return 'micro';
}

/** Which variant of a bar's label is shown. [S-18] */
export type LabelSize = 'full' | 'short' | 'mini';

/** Width the row spends on things that are not the label, in CSS px. */
const STACKED_RESERVE = 38; // percentage column + its gap
const INLINE_RESERVE = 66; // percentage column + minimum bar + two gaps

/**
 * Pick the label variant from the width actually left for it. A `tight`/`micro`
 * row puts the bar and the percentage on the SAME line as the label, so it has
 * far less to spend than a stacked one of the same widget width. [S-18]
 */
export function labelSize(bodyW: number, density: Density, fontScale = 1): LabelSize {
  // Unmeasured (`NaN`) spells the label out; a measured zero-width box falls
  // through the arithmetic to `mini`, same reasoning as usageDensity.
  if (!Number.isFinite(bodyW)) return 'full';
  const fs = safeScale(fontScale);
  const stacked = density === 'full' || density === 'compact';
  const avail = bodyW - (stacked ? STACKED_RESERVE : INLINE_RESERVE);
  if (avail >= 110 * fs) return 'full';
  if (avail >= 40 * fs) return 'short';
  return 'mini';
}

/** The bar's label at the chosen width. [S-18] */
export function barLabel(bar: UsageBar, size: LabelSize): string {
  if (size === 'mini') return bar.mini;
  if (size === 'short') return bar.short;
  return bar.label;
}
