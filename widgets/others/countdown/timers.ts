export type DisplayFormat = 'auto' | 'days' | 'dhm' | 'dhms';

export interface Timer {
  id: string;
  label: string;
  target: string;
  format: DisplayFormat;
}

export const FORMAT_LABELS: Record<DisplayFormat, string> = {
  auto: 'Auto',
  days: 'Days only',
  dhm:  'Days · Hours · Min',
  dhms: 'Days · Hours · Min · Sec',
};

export function newTimerId(): string {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) {
    return crypto.randomUUID();
  }
  return `t-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
}

const SEC = 1000;
const MIN = 60 * SEC;
const HOUR = 60 * MIN;
const DAY = 24 * HOUR;

function pluralize(n: number, unit: string): string {
  return `${n} ${unit}${n === 1 ? '' : 's'}`;
}

export interface RemainingResult {
  text: string;
  elapsed: boolean;
}

export function formatRemaining(target: Date, now: Date, format: DisplayFormat): RemainingResult {
  const diffMs = target.getTime() - now.getTime();
  const elapsed = diffMs <= 0;
  const abs = Math.abs(diffMs);

  let text: string;
  switch (format) {
    case 'days':   text = formatDays(abs); break;
    case 'dhm':    text = formatDHM(abs);  break;
    case 'dhms':   text = formatDHMS(abs); break;
    case 'auto':
    default:       text = formatAuto(abs); break;
  }

  if (elapsed) text = format === 'auto' && abs < MIN ? 'just now' : `${text} ago`;
  return { text, elapsed };
}

function formatDays(abs: number): string {
  const d = Math.floor(abs / DAY);
  return pluralize(d, 'day');
}

function formatDHM(abs: number): string {
  const d = Math.floor(abs / DAY);
  const h = Math.floor((abs % DAY) / HOUR);
  const m = Math.floor((abs % HOUR) / MIN);
  return `${d}d ${h}h ${m}m`;
}

function formatDHMS(abs: number): string {
  const d = Math.floor(abs / DAY);
  const h = Math.floor((abs % DAY) / HOUR);
  const m = Math.floor((abs % HOUR) / MIN);
  const s = Math.floor((abs % MIN) / SEC);
  return `${d}d ${h}h ${m}m ${s}s`;
}

function formatAuto(abs: number): string {
  if (abs < MIN) return 'in a moment';
  if (abs < HOUR) {
    const m = Math.floor(abs / MIN);
    return pluralize(m, 'min');
  }
  if (abs < DAY) {
    const h = Math.floor(abs / HOUR);
    const m = Math.floor((abs % HOUR) / MIN);
    return m === 0 ? pluralize(h, 'hr') : `${h}h ${m}m`;
  }
  const d = Math.floor(abs / DAY);
  if (d < 7) {
    const h = Math.floor((abs % DAY) / HOUR);
    return h === 0 ? pluralize(d, 'day') : `${d}d ${h}h`;
  }
  return pluralize(d, 'day');
}

/** Convert an ISO string to the value expected by `<input type="datetime-local">` (local TZ, no seconds). */
export function isoToLocalInput(iso: string): string {
  const d = new Date(iso);
  if (isNaN(d.getTime())) return '';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/**
 * Convert a `<input type="datetime-local">` value (local TZ) back to an ISO string.
 * Returns `null` for empty or unparseable input so the caller keeps the previous
 * target instead of silently retargeting the timer to the current instant.
 */
export function localInputToIso(local: string): string | null {
  if (!local) return null;
  const d = new Date(local);
  if (isNaN(d.getTime())) return null;
  return d.toISOString();
}
