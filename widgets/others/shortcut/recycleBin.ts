// Recycle Bin fill state for shortcut items pointing at `shell:RecycleBinFolder`
// (issue #94: "some way to tell if there's something in your recycle bin").
//
// The state is process-global, not per widget: every Shortcut widget instance in
// a window shows the SAME bin, so the status lives in a module-level signal and
// the IPC is single-flighted + age-throttled. N widgets polling therefore cost
// one `recycle_bin_status` call per interval, not N.

import { createSignal } from 'solid-js';
import { invoke, type RecycleBinStatus } from '../../../src/ipc.ts';
import { logger } from '../../../src/debugLog.ts';

const DBG = 'shortcut';

/** The `shell:` URI the Recycle Bin quick-set adds ([S-16]). */
export const RECYCLE_BIN_PATH = 'shell:RecycleBinFolder';

/** How often a widget holding a Recycle Bin item refreshes the status. */
export const RECYCLE_POLL_MS = 15_000;

/** Refreshes closer together than this reuse the last result, so several
 *  widgets (or several windows' worth of ticks) collapse into one IPC. */
const MIN_AGE_MS = 5_000;

export function isRecycleBinPath(path: string | null | undefined): boolean {
  return path === RECYCLE_BIN_PATH;
}

/** Badge text for a bin holding `items` files. Capped so a long-neglected bin
 *  can't stretch the badge past its icon. */
export function binBadgeLabel(items: number): string {
  if (!Number.isFinite(items) || items <= 0) return '';
  const n = Math.floor(items);
  return n > 99 ? '99+' : String(n);
}

/** Compact byte size for the item tooltip, mirroring the folder widget's
 *  `fmtSize` so the two read identically. */
export function fmtBinSize(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) return '0 B';
  if (bytes < 1024) return `${Math.round(bytes)} B`;
  if (bytes < 1024 ** 2) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 ** 3) return `${(bytes / 1024 ** 2).toFixed(1)} MB`;
  return `${(bytes / 1024 ** 3).toFixed(2)} GB`;
}

const [status, setStatus] = createSignal<RecycleBinStatus | null>(null);

/** Last known fill state; `null` until the first successful query (and after a
 *  failure, so an unreachable shell never fabricates "empty"). */
export { status as recycleBinStatus };

/** True only when the bin is known to hold something. Unknown → false, so a
 *  failed query shows no badge rather than a wrong one. */
export function recycleBinHasItems(): boolean {
  const s = status();
  return !!s && s.items > 0;
}

let inFlight: Promise<void> | null = null;
let lastFetchAt = 0;

/**
 * Query the backend for the bin's fill state. Concurrent calls share one IPC,
 * and a call within `MIN_AGE_MS` of the last successful one is a no-op.
 * Failures are logged and leave the previous value in place.
 */
export function refreshRecycleBinStatus(force = false): Promise<void> {
  if (inFlight) return inFlight;
  if (!force && status() !== null && Date.now() - lastFetchAt < MIN_AGE_MS) {
    return Promise.resolve();
  }
  inFlight = invoke('recycle_bin_status')
    .then(s => {
      lastFetchAt = Date.now();
      setStatus(s);
    })
    .catch(e => {
      logger.error(DBG, `recycle_bin_status failed: ${e}`);
    })
    .finally(() => { inFlight = null; });
  return inFlight;
}

/** Test seam: drop the cached status + throttle so each case starts clean. */
export function __resetRecycleBinStatus(): void {
  setStatus(null);
  lastFetchAt = 0;
  inFlight = null;
}
