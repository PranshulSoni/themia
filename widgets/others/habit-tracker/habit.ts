export interface Habit {
  id: string;
  name: string;
}

/** How often a habit's checkbox clears. `custom` uses `customHours`. */
export type ResetPeriod = 'hourly' | 'daily' | 'weekly' | 'monthly' | 'custom';

export const RESET_PERIODS: ResetPeriod[] = ['hourly', 'daily', 'weekly', 'monthly', 'custom'];

export const RESET_LABELS: Record<ResetPeriod, string> = {
  hourly: 'Every hour',
  daily: 'Every day',
  weekly: 'Every week',
  monthly: 'Every month',
  custom: 'Custom interval',
};

/** The window a check-off applies to, phrased for the checkbox tooltip. Reflects
 *  the active reset cadence so a weekly habit reads "this week", not "today". */
export const PERIOD_NOUNS: Record<ResetPeriod, string> = {
  hourly: 'this hour',
  daily: 'today',
  weekly: 'this week',
  monthly: 'this month',
  custom: 'this interval',
};

/** Bounds for the `custom` cadence (minutes, supports sub-hour intervals). */
export const DEFAULT_CUSTOM_MINUTES = 30;
export const MIN_CUSTOM_MINUTES = 1;
export const MAX_CUSTOM_MINUTES = 1440; // one day

/** How often the widget re-reads the clock so a rolled-over period clears the
 *  checkbox without a remount. Fine-grained cadences need a faster tick. */
export function resetTickMs(period: ResetPeriod): number {
  return period === 'hourly' || period === 'custom' ? 15_000 : 60_000;
}

export function newHabitId(): string {
  return `habit-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 7)}`;
}

/** Index of the row whose box contains `y`, for the widget's drag reorder
 *  ([S-13]). Above the first row or below the last one the nearest row wins, so
 *  a cursor that runs past either end still parks the habit at that end instead
 *  of dropping out of the gesture. Gaps between rows resolve to the row above.
 *  Returns -1 only for an empty list. */
export function rowIndexAtY(rects: readonly { top: number; bottom: number }[], y: number): number {
  if (rects.length === 0) return -1;
  if (y <= rects[0].top) return 0;
  for (let i = 0; i < rects.length; i++) {
    if (y < rects[i].bottom) return i;
  }
  return rects.length - 1;
}

/** Do two lists hold the same habits in the same order? The drag commits only
 *  when this is false, so a drag that wanders and comes home writes nothing. */
export function sameHabitOrder(a: readonly Habit[], b: readonly Habit[]): boolean {
  return a.length === b.length && a.every((h, i) => h.id === b[i].id);
}

/** APPEND a habit, for the widget's own "Add habit" row ([S-16]). The row sits
 *  at the bottom of the list, so that is where the new habit lands. A
 *  blank/whitespace name is a no-op returning the input list unchanged, so the
 *  add row can never create a nameless habit. Immutable. */
export function addHabit(list: Habit[], name: string, id: string): Habit[] {
  const trimmed = name.trim();
  if (!trimmed) return list;
  return [...list, { id, name: trimmed }];
}

/** Move `id` into `targetId`'s slot, keeping every other habit's relative order
 *  ([S-09]). Returns the INPUT array (same reference) when the move is a no-op
 *  or either id is unknown, so a caller driving this from `dragover` can skip
 *  the persist instead of writing the config on every pointer move -- and a
 *  stale id from an interrupted drag can never drop or duplicate a habit. */
export function moveHabit(list: Habit[], id: string, targetId: string): Habit[] {
  const from = list.findIndex((h) => h.id === id);
  const to = list.findIndex((h) => h.id === targetId);
  if (from < 0 || to < 0 || from === to) return list;
  const next = list.slice();
  const [moved] = next.splice(from, 1);
  next.splice(to, 0, moved);
  return next;
}

/** Local calendar day key, `YYYY-MM-DD` (NOT UTC, so a habit done at 11pm counts
 *  for the local day, not tomorrow). */
/** Replace a habit's name. A blank/whitespace name is a no-op that returns the
 *  input list unchanged, so committing an emptied editor keeps the habit rather
 *  than leaving an "Untitled" row behind. Unknown id → equal list. Immutable.
 *  [S-14] */
export function renameHabit(list: Habit[], id: string, name: string): Habit[] {
  const trimmed = name.trim();
  if (!trimmed) return list;
  return list.map(h => (h.id === id ? { ...h, name: trimmed } : h));
}

export function dateKey(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

/** ISO-8601 week number + ISO week-year for a local date. */
function isoWeek(d: Date): { year: number; week: number } {
  const date = new Date(Date.UTC(d.getFullYear(), d.getMonth(), d.getDate()));
  const day = date.getUTCDay() || 7; // Mon=1..Sun=7
  date.setUTCDate(date.getUTCDate() + 4 - day); // nearest Thursday
  const yearStart = new Date(Date.UTC(date.getUTCFullYear(), 0, 1));
  const week = Math.ceil(((date.getTime() - yearStart.getTime()) / 86_400_000 + 1) / 7);
  return { year: date.getUTCFullYear(), week };
}

/** The completion-bucket key for a date under a given reset cadence. The
 *  checkbox is "done" when this key is present in the habit's log. */
export function periodKey(
  d: Date,
  period: ResetPeriod = 'daily',
  customMinutes: number = DEFAULT_CUSTOM_MINUTES,
): string {
  if (period === 'hourly') {
    return `${dateKey(d)}-H${String(d.getHours()).padStart(2, '0')}`;
  }
  if (period === 'custom') {
    const n = clampMinutes(customMinutes);
    // Fixed-width windows anchored to the epoch: floor of absolute minutes / N.
    const bucket = Math.floor(d.getTime() / (n * 60_000));
    return `C${n}m-${bucket}`;
  }
  if (period === 'weekly') {
    const { year, week } = isoWeek(d);
    return `${year}-W${String(week).padStart(2, '0')}`;
  }
  if (period === 'monthly') {
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
  }
  return dateKey(d);
}

/** Clamp a user-supplied custom interval to the supported range (whole minutes). */
function clampMinutes(m: number): number {
  if (!Number.isFinite(m)) return DEFAULT_CUSTOM_MINUTES;
  return Math.min(MAX_CUSTOM_MINUTES, Math.max(MIN_CUSTOM_MINUTES, Math.floor(m)));
}

/** Step back exactly one period (used for streak walking). */
function prevPeriod(d: Date, period: ResetPeriod, customMinutes: number = DEFAULT_CUSTOM_MINUTES): Date {
  const x = new Date(d);
  if (period === 'hourly') x.setTime(x.getTime() - 3_600_000);
  else if (period === 'custom') x.setTime(x.getTime() - clampMinutes(customMinutes) * 60_000);
  else if (period === 'weekly') x.setDate(x.getDate() - 7);
  else if (period === 'monthly') x.setMonth(x.getMonth() - 1, 1);
  else x.setDate(x.getDate() - 1);
  return x;
}

/** Bounds for the row's history strip ([S-15]). The default is seven, so the
 *  default daily cadence reads as the last seven days. Zero turns the strip off
 *  entirely — a habit list is allowed to be just a list. The ceiling is a month
 *  of daily buckets; past that the dots stop being countable at a glance long
 *  before they stop fitting. */
export const DEFAULT_HISTORY_DOTS = 7;
export const MIN_HISTORY_DOTS = 0;
export const MAX_HISTORY_DOTS = 31;

/** Clamp a user-supplied dot count to the supported range (whole dots). Config
 *  is a trust boundary: a hand-edited layout file can hold anything, and an
 *  unclamped count is a render loop's `for` bound. */
export function clampHistoryDots(n: number | undefined): number {
  if (n === undefined || !Number.isFinite(n)) return DEFAULT_HISTORY_DOTS;
  return Math.min(MAX_HISTORY_DOTS, Math.max(MIN_HISTORY_DOTS, Math.floor(n)));
}

/** One dot of the history strip. */
export interface HistoryEntry {
  /** The completion bucket this dot stands for. */
  key: string;
  /** A moment INSIDE that bucket, for the dot's tooltip. */
  date: Date;
  done: boolean;
}

/** The last `count` completion buckets, OLDEST first, ending with the bucket
 *  `now` falls in ([S-15]). Walks the same `prevPeriod` ladder as `streak`, so
 *  the dots and the flame can never disagree about what "one back" means -- and
 *  under the default daily cadence that ladder IS the last N days. `count` is
 *  clamped here rather than at the call site, so every caller gets the same
 *  bounds whatever the stored config says. */
export function recentHistory(
  keys: readonly string[],
  now: Date,
  period: ResetPeriod = 'daily',
  customMinutes: number = DEFAULT_CUSTOM_MINUTES,
  count: number = DEFAULT_HISTORY_DOTS,
): HistoryEntry[] {
  const n = clampHistoryDots(count);
  const set = new Set(keys);
  const out: HistoryEntry[] = [];
  let d = now;
  for (let i = 0; i < n; i++) {
    const key = periodKey(d, period, customMinutes);
    out.push({ key, date: d, done: set.has(key) });
    d = prevPeriod(d, period, customMinutes);
  }
  return out.reverse();
}

/** Consecutive-period streak ending the current period. The current period not
 *  yet done does NOT break the streak (you still have time left in it); a prior
 *  gap does. */
export function streak(
  keys: string[],
  today: Date,
  period: ResetPeriod = 'daily',
  customMinutes: number = DEFAULT_CUSTOM_MINUTES,
): number {
  const set = new Set(keys);
  let count = 0;
  let d = today;
  if (!set.has(periodKey(d, period, customMinutes))) d = prevPeriod(d, period, customMinutes);
  while (set.has(periodKey(d, period, customMinutes))) {
    count++;
    d = prevPeriod(d, period, customMinutes);
  }
  return count;
}
