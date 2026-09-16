// Pure helpers for the Mood Tracker widget (SPEC.md `[S-NN]`).
//
// Entries live in the widget's OWN config bag (per-instance, keyed by
// `_widgetId`) exactly the way the To Do widget's local mode stores its tasks
// (`office/todo/localTasks.ts`) and the Habit Tracker its log — nothing here
// touches Tauri, IPC or accounts, so every rule below is unit-tested.
//
// `parseEntries` owns the trust boundary: config values arrive as `unknown`, so
// it distinguishes "absent / corrupt" (→ empty list) from a single malformed
// entry (→ dropped, siblings kept), and never throws.

/** The five-point scale. Deliberately fixed: the level is persisted as a
 *  number, so a user-editable scale length would silently re-interpret history. */
export type MoodLevel = 1 | 2 | 3 | 4 | 5;

export const MOOD_LEVELS: MoodLevel[] = [1, 2, 3, 4, 5];

/** i18n key suffix per level, so the scale's names live in the locale files
 *  rather than being hardcoded English in the widget. */
export const MOOD_LEVEL_KEYS: Record<MoodLevel, string> = {
  1: 'awful',
  2: 'bad',
  3: 'okay',
  4: 'good',
  5: 'great',
};

export interface MoodEntry {
  id: string;
  /** ISO-8601 instant the mood was recorded. */
  at: string;
  level: MoodLevel;
  /** The thought attached to this moment; `''` when none. */
  note: string;
}

/** History cap. Entries are persisted inside the layout file, which is
 *  rewritten whenever any widget moves or resizes, so the list must stay
 *  bounded: ~5 entries a day for a year is well under this, and the oldest are
 *  pruned rather than refusing the write (never lose the entry the user just
 *  made). */
export const MAX_ENTRIES = 2000;

export function isMoodLevel(v: unknown): v is MoodLevel {
  return v === 1 || v === 2 || v === 3 || v === 4 || v === 5;
}

/** Newest first, the order every consumer here assumes. `Array#sort` is stable,
 *  so entries sharing a timestamp keep their relative order. */
function sortNewestFirst(entries: MoodEntry[]): MoodEntry[] {
  return [...entries].sort((a, b) => Date.parse(b.at) - Date.parse(a.at));
}

/** [S-02] Narrow an untrusted config value to a validated, newest-first
 *  `MoodEntry[]`. A non-array (missing or corrupt config) yields `[]`; entries
 *  missing a string `id`/`at`, carrying an unparseable `at`, or an out-of-scale
 *  `level` are dropped rather than trusted; a non-string `note` becomes `''`.
 *  Never throws. */
export function parseEntries(raw: unknown): MoodEntry[] {
  if (!Array.isArray(raw)) return [];
  const out: MoodEntry[] = [];
  for (const entry of raw) {
    if (entry === null || typeof entry !== 'object') continue;
    const o = entry as Record<string, unknown>;
    if (typeof o.id !== 'string' || typeof o.at !== 'string') continue;
    if (!isMoodLevel(o.level)) continue;
    if (!Number.isFinite(Date.parse(o.at))) continue;
    out.push({
      id: o.id,
      at: o.at,
      level: o.level,
      note: typeof o.note === 'string' ? o.note : '',
    });
  }
  return sortNewestFirst(out);
}

export function newEntryId(): string {
  return `mood-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 7)}`;
}

/** [S-03] Record a mood. The list stays newest-first and capped at
 *  `MAX_ENTRIES` (oldest pruned). An out-of-scale level or unparseable
 *  timestamp is a no-op returning the input list. Immutable. */
export function addEntry(
  entries: MoodEntry[],
  level: MoodLevel,
  at: string,
  id: string,
  note = '',
): MoodEntry[] {
  if (!isMoodLevel(level) || !Number.isFinite(Date.parse(at))) return entries;
  return sortNewestFirst([{ id, at, level, note }, ...entries]).slice(0, MAX_ENTRIES);
}

/** [S-05] Attach (or clear) the thought on one entry. Unknown id → equal list.
 *  Immutable. */
export function setEntryNote(entries: MoodEntry[], id: string, note: string): MoodEntry[] {
  return entries.map((e) => (e.id === id ? { ...e, note } : e));
}

/** Drop one entry. Unknown id → equal list. Immutable. */
export function removeEntry(entries: MoodEntry[], id: string): MoodEntry[] {
  return entries.filter((e) => e.id !== id);
}

/** Local calendar day key, `YYYY-MM-DD` (NOT UTC, so a mood logged at 11pm
 *  counts for the local day, not tomorrow). [S-06] */
export function dateKey(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

/** Local midnight of a `YYYY-MM-DD` key. Invalid input yields an Invalid Date,
 *  which every caller here treats as "no such day" rather than throwing. */
export function parseDateKey(key: string): Date {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(key);
  if (!m) return new Date(NaN);
  return new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
}

/** Same wall-clock time, `n` local days away. Uses `setDate`, so DST shifts
 *  land on the intended calendar day rather than 23/25 hours later. */
export function shiftDays(d: Date, n: number): Date {
  const x = new Date(d);
  x.setDate(x.getDate() + n);
  return x;
}

export interface DayMood {
  /** Local `YYYY-MM-DD`. */
  date: string;
  /** How many entries landed on this day. `0` means nothing was logged — which
   *  is NOT the same as a bad day, and the two never share a colour ([S-07]). */
  count: number;
  /** Sum of those entries' levels. Meaningless when `count` is 0. */
  sum: number;
}

/** A day's mean level, or 0 when nothing was logged (guard with `count` first). */
export function dayAverage(day: DayMood): number {
  return day.count > 0 ? day.sum / day.count : 0;
}

/** [S-06] One slot per local day for the `days`-long window ENDING at `end`
 *  (inclusive), oldest first. Days with nothing logged are present with
 *  `count: 0` so the heatmap keeps its calendar shape, and entries outside the
 *  window are ignored rather than folded into an edge day. */
export function dayTotals(entries: MoodEntry[], end: Date, days: number): DayMood[] {
  const n = Math.max(1, Math.floor(days));
  const slots = new Map<string, DayMood>();
  const out: DayMood[] = [];
  for (let i = n - 1; i >= 0; i--) {
    const cell: DayMood = { date: dateKey(shiftDays(end, -i)), count: 0, sum: 0 };
    slots.set(cell.date, cell);
    out.push(cell);
  }
  for (const e of entries) {
    const cell = slots.get(dateKey(new Date(e.at)));
    if (!cell) continue;
    cell.count += 1;
    cell.sum += e.level;
  }
  return out;
}

/** [S-07] The colour bucket for a day: `0` when nothing was logged, otherwise
 *  the day's average rounded into 1..5. A logged day is never bucket 0, so an
 *  awful day and an unlogged day stay visually distinct. */
export function heatBucket(day: DayMood): 0 | MoodLevel {
  if (day.count <= 0) return 0;
  const rounded = Math.round(dayAverage(day));
  return Math.min(5, Math.max(1, rounded)) as MoodLevel;
}

/** [S-08] The heatmap is 7 cells wide, so wrapping the window chronologically
 *  puts a FIXED weekday in each column — no week-start setting needed, the
 *  window's own dates decide. */
export const HEATMAP_COLS = 7;

/** [S-08] Pad the window with leading blanks so it ends exactly at the end of a
 *  row; `null` slots are days before the window started and render empty. */
export function heatmapCells(totals: DayMood[]): (DayMood | null)[] {
  const lead = (HEATMAP_COLS - (totals.length % HEATMAP_COLS)) % HEATMAP_COLS;
  return [...new Array<DayMood | null>(lead).fill(null), ...totals];
}

/** [S-08] One sample date per column for the weekday header row: the first day
 *  that lands in that column (all days in a column share its weekday). */
export function heatmapColumnDates(cells: (DayMood | null)[]): (string | null)[] {
  const out: (string | null)[] = new Array<string | null>(HEATMAP_COLS).fill(null);
  cells.forEach((c, i) => {
    const col = i % HEATMAP_COLS;
    if (c && out[col] === null) out[col] = c.date;
  });
  return out;
}

/** [S-09] Mean level across every entry in the window, or `null` when the
 *  window holds none — so the footer shows a dash rather than a misleading 0. */
export function windowAverage(totals: DayMood[]): number | null {
  let sum = 0;
  let count = 0;
  for (const d of totals) {
    sum += d.sum;
    count += d.count;
  }
  return count > 0 ? sum / count : null;
}

/** [S-10] Consecutive days ending today on which something was logged. Today
 *  not yet logged does NOT break the streak (the day isn't over); an earlier
 *  gap does. Mirrors the Habit Tracker's streak rule. Counting is bounded by
 *  the window, so a streak longer than the shown history reads as the window
 *  length. */
export function loggedStreak(totals: DayMood[]): number {
  let i = totals.length - 1;
  if (i >= 0 && totals[i].count === 0) i -= 1;
  let n = 0;
  for (; i >= 0 && totals[i].count > 0; i -= 1) n += 1;
  return n;
}

/** Entries recorded on one local day, newest first (input order preserved). */
export function entriesForDay(entries: MoodEntry[], key: string): MoodEntry[] {
  return entries.filter((e) => dateKey(new Date(e.at)) === key);
}

/** [S-04] The most recent entry on `now`'s local day, or `null`. Drives which
 *  face reads as selected and which entry the note box edits. */
export function latestOn(entries: MoodEntry[], now: Date): MoodEntry | null {
  const key = dateKey(now);
  for (const e of entries) {
    if (dateKey(new Date(e.at)) === key) return e;
  }
  return null;
}

/** [S-16] Where a widget's entries are kept. A closed set, not a bare string,
 *  so a future second backend is a compile error at every switch rather than a
 *  silently-unhandled value. Today `local` is the only member: entries live in
 *  the widget's own config bag, on this device, per instance. */
export type MoodStorageMode = 'local';

export const DEFAULT_STORAGE_MODE: MoodStorageMode = 'local';

/** [S-16] Narrow an untrusted config value to a supported storage mode. */
export function parseStorageMode(raw: unknown): MoodStorageMode {
  return raw === 'local' ? 'local' : DEFAULT_STORAGE_MODE;
}

/** [S-11] How much history the heatmap covers, in weeks. */
export const HISTORY_WEEKS = [4, 8, 12, 26] as const;
export type HistoryWeeks = (typeof HISTORY_WEEKS)[number];
export const DEFAULT_HISTORY_WEEKS: HistoryWeeks = 8;

/** [S-11] Narrow an untrusted config value to a supported window length. */
export function parseHistoryWeeks(raw: unknown): HistoryWeeks {
  return (HISTORY_WEEKS as readonly number[]).includes(raw as number)
    ? (raw as HistoryWeeks)
    : DEFAULT_HISTORY_WEEKS;
}
