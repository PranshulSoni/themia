// Shared graph-time-span logic for the live time-series system widgets
// (battery, cpu, disk, gpu, net, ping, ram). It replaces two things each of
// those widgets used to hardcode:
//
//   1. `const MAX_PTS = 40` — a fixed sample count. The window a graph shows is
//      now a user-configured *duration* (`graphSpanSec`), and the sample count
//      derives from it: capacity = span / poll-interval, bounded.
//   2. `x = i / (n - 1) * 100` — which stretched however-many samples across the
//      full width, so a 2-sample graph spanned edge-to-edge and "un-stretched"
//      as it filled. `sampleX` instead pins the NEWEST sample to the right edge
//      and steps left by one fixed slot per sample, so the x-axis is scaled to
//      the whole window from the first frame and the trace grows in from the
//      right. No horizontal rescaling as data accumulates.
//
// Lucide-free and node-importable so vitest exercises it without a DOM.

/** Selectable graph spans, in seconds. Closed set — the config dropdown options
 *  and the values the widgets understand (CLAUDE.md: closed sets, not loose
 *  strings). A persisted value outside this set is still honoured by
 *  `graphSpanSec`/`windowCapacity` (like an imported off-scale interval), it
 *  just won't match a dropdown row. */
export const GRAPH_SPANS = [30, 60, 120, 180, 300, 600, 1200, 1800, 3600] as const;
export type GraphSpanSec = typeof GRAPH_SPANS[number];

/** The time-series widgets that own a sparkline. Closed set. */
export type GraphWidgetId =
  | 'battery' | 'cpu' | 'disk' | 'gpu' | 'net' | 'ping' | 'ram';

/**
 * Default span per widget (seconds). Each is the preset nearest that widget's
 * historical fixed window — 40 samples at its default poll interval — so
 * upgrading keeps roughly the same amount of history on screen:
 *   cpu/ram/net/gpu  40 x  2s =  80s -> 1m
 *   disk             40 x  5s = 200s -> 3m
 *   battery          40 x 10s = 400s -> 5m
 *   ping             40 x 30s = 1200s -> 20m (exact)
 * Change a widget's default interval or window and update this together.
 */
export const GRAPH_SPAN_DEFAULTS: Record<GraphWidgetId, number> = {
  battery: 300,
  cpu: 60,
  disk: 180,
  gpu: 60,
  net: 60,
  ping: 1200,
  ram: 60,
};

/** Upper bound on samples held/drawn for one series, so an extreme
 *  span-over-interval (e.g. 1h @ 1s) can't build a multi-thousand-point path
 *  every poll. At the cap the visible window is `MAX_CAPACITY x interval`
 *  rather than the full configured span. */
export const MAX_CAPACITY = 600;

/**
 * Resolve the effective span (seconds) from a persisted config value. Absent
 * and corrupt both fall back to the widget default, so junk from the JSON trust
 * boundary can never reach `windowCapacity` as NaN. A finite positive number
 * passes through unchanged (an imported off-scale span is kept, not clamped).
 */
export function graphSpanSec(raw: unknown, fallback: number): number {
  return typeof raw === 'number' && Number.isFinite(raw) && raw > 0 ? raw : fallback;
}

/**
 * Samples a `spanSec` window holds when polled every `intervalSec`. At least 2
 * (a line needs two points) and at most `MAX_CAPACITY`. Non-finite / non-positive
 * inputs fall back (span -> 60s, interval -> 1s) so the poller math stays sane.
 */
export function windowCapacity(spanSec: number, intervalSec: number): number {
  const span = Number.isFinite(spanSec) && spanSec > 0 ? spanSec : 60;
  const iv   = Number.isFinite(intervalSec) && intervalSec > 0 ? intervalSec : 1;
  return Math.min(MAX_CAPACITY, Math.max(2, Math.round(span / iv)));
}

/**
 * X coordinate in [0, 100] for the sample at array index `i` of `n` currently
 * held, within a `capacity`-slot window. The newest sample (i = n - 1) sits at
 * 100 (right edge); each older sample steps left by one slot (100 / (capacity -
 * 1)). While n < capacity the trace occupies only the right portion and grows
 * leftward as samples arrive; at n === capacity the oldest sits at 0 and it
 * fills the width. Never rescales horizontally as `n` changes — that is the
 * whole point. Clamped to [0, 100] so a transient over-long buffer can't emit a
 * negative x.
 */
export function sampleX(i: number, n: number, capacity: number): number {
  const cap = Math.max(2, capacity);
  const step = 100 / (cap - 1);
  const x = 100 - ((n - 1) - i) * step;
  return +Math.max(0, Math.min(100, x)).toFixed(2);
}

/** Human label for a span in seconds: `30s`, `1m`, `20m`, `1h`. */
export function formatSpan(sec: number): string {
  if (sec < 60) return `${sec}s`;
  if (sec % 3600 === 0) return `${sec / 3600}h`;
  return `${Math.round(sec / 60)}m`;
}

/** Dropdown options for the config panels (value is the span in seconds, as a
 *  string, since Dropdown deals in string values). */
export function spanOptions(): { label: string; value: string }[] {
  return GRAPH_SPANS.map((s) => ({ label: formatSpan(s), value: String(s) }));
}
