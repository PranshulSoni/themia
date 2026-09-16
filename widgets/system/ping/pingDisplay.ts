// Pure display helpers for the Ping widget (SPEC.md [S-01], [S-03]..[S-11]).
// Lucide-free and node-importable so vitest runs them without a DOM.

import { sampleX } from '../graphSpan.ts';

export interface PingHost {
  label: string;
  address: string;
}

/// [S-01] first non-empty of: header override, host label, host address, "Ping".
export function pingTitle(label: string | undefined, host: PingHost | undefined): string {
  return label || host?.label || host?.address || 'Ping';
}

/// [S-03] `null` = no result yet, i.e. still checking.
export function statusText(ok: boolean | null): string {
  return ok === true ? 'Online' : ok === false ? 'Offline' : 'Checking…';
}

/// [S-04] one color for the dot, the status text, the latency and the spark.
export function statusColor(ok: boolean | null): string {
  return ok === true
    ? 'var(--success)'
    : ok === false
      ? 'var(--destructive)'
      : 'var(--subtle-foreground)';
}

/// [S-05] `—` when unmeasured, `Nms` below a second, `N.Ns` at/above one.
export function fmtMs(ms: number | null | undefined): string {
  if (ms == null) return '—';
  if (ms < 1000) return ms + 'ms';
  return (ms / 1000).toFixed(1) + 's';
}

/// [S-07] sparkline paths over the latency history. Needs >= 2 samples and >= 1
/// success, else empty paths (an all-null history has no scale to draw against).
/// Failed samples plot on the baseline rather than breaking the line. The x-axis
/// is the fixed `capacity`-slot window ([S-28]): the newest sample pins to the
/// right edge and the trace grows in from the right, never rescaling.
export function buildSpark(hist: (number | null)[], capacity: number): { line: string; area: string } {
  const pts = hist.filter((v): v is number => v != null);
  if (hist.length < 2 || !pts.length) return { line: '', area: '' };
  const max = Math.max(...pts, 1);
  const n = hist.length;
  const coords = hist.map((v, i) => [
    sampleX(i, n, capacity),
    v == null ? 100 : +(100 - (v / max) * 94).toFixed(2),
  ]);
  const line = 'M ' + coords.map(([x, y]) => `${x} ${y}`).join(' L ');
  const area = `M ${coords[0][0]} 100 ` +
               coords.map(([x, y]) => `L ${x} ${y}`).join(' ') +
               ` L ${coords.at(-1)![0]} 100 Z`;
  return { line, area };
}

export interface PingStats {
  min: number | null;
  avg: number | null;
  max: number | null;
  jitter: number | null;
  loss: number;
}

/// [S-09]..[S-11] aggregates over the history. min/avg/max/jitter cover the
/// SUCCESSFUL samples only and are null until the first success; loss is the
/// share of failed samples across the whole history.
///
/// KNOWN DEFECT [S-10]: a history with no success at all short-circuits to
/// loss 0, so a host that never answered reads "Loss 0%" rather than 100%.
/// Pinned by pingDisplay.test.ts as current behavior, not endorsed.
export function pingStats(hist: (number | null)[]): PingStats {
  const present = hist.filter((v): v is number => v != null);
  if (present.length === 0) {
    return { min: null, avg: null, max: null, jitter: null, loss: 0 };
  }
  const min = Math.min(...present);
  const max = Math.max(...present);
  const avg = present.reduce((a, b) => a + b, 0) / present.length;
  let jitter = 0;
  if (present.length >= 2) {
    // Consecutive among the SUCCESSES: a dropped ping is skipped, not a break.
    const diffs = present.slice(1).map((v, i) => Math.abs(v - present[i]));
    jitter = diffs.reduce((a, b) => a + b, 0) / diffs.length;
  }
  const loss = hist.length > 0 ? (hist.filter(v => v == null).length / hist.length) * 100 : 0;
  return { min, avg, max, jitter, loss };
}
