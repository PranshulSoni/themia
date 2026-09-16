// Calendar datetime parsing. Extracted from Calendar.tsx so the offset-handling
// contract ([S-41]) can be unit-tested without mounting the widget.

/**
 * Parse a backend event datetime into a `Date`.
 *
 * Strings arrive in one of two shapes:
 *  - with an explicit RFC3339 zone: either `Z` or a `±HH:MM` offset. Google
 *    Calendar returns timed events in the event's local offset, which for the
 *    Americas is *negative* (e.g. `2026-06-25T10:00:00-04:00`).
 *  - as a naive, zoneless datetime (`2026-06-25T10:00:00`), assumed to be UTC.
 *
 * Only append `Z` when the string carries no zone. Appending it to a string
 * that already has an offset yields `...-04:00Z`, which is `Invalid Date`
 * (issue #48: timed events on US-Eastern Google accounts).
 */
export function gdt(str: string): Date {
  if (!str) return new Date(0);
  return new Date(hasZone(str) ? str : str + 'Z');
}

// True when the string already carries a zone designator: a trailing `Z` or a
// `±HH:MM` / `±HHMM` offset on the *time* portion. We scan only after the `T`
// so the date's own hyphens (`2026-06-25`) are never read as a negative offset.
const ZONE_RE = /(Z|[+-]\d{2}:?\d{2})$/;
function hasZone(str: string): boolean {
  const t = str.indexOf('T');
  return t >= 0 && ZONE_RE.test(str.slice(t));
}

// -- Week start [S-46] --------------------------------------------------------
// The first day of the week is configurable. `getDay()` is a fixed JS mapping
// (0=Sun .. 6=Sat); these helpers translate it into the widget's month-grid
// start, week-view start and weekday-header order for either setting.

/** Closed set (no stringly config): the week can start on Monday or Sunday. */
export type WeekStart = 'monday' | 'sunday';

/** getDay() index of the first column under the given setting (Mon=1, Sun=0). */
function firstDow(weekStart: WeekStart): number {
  return weekStart === 'sunday' ? 0 : 1;
}

/**
 * Number of days to step back from a date whose `getDay() === dow` to reach the
 * first day of its week. Monday start: Monday→0 … Sunday→6. Sunday start:
 * Sunday→0 … Saturday→6.
 */
export function weekStartOffset(dow: number, weekStart: WeekStart): number {
  return (dow - firstDow(weekStart) + 7) % 7;
}

/** The start-of-week `Date` for the week containing `d` (clock time preserved,
 *  input not mutated). */
export function startOfWeek(d: Date, weekStart: WeekStart): Date {
  const r = new Date(d);
  r.setDate(r.getDate() - weekStartOffset(d.getDay(), weekStart));
  return r;
}

/** The 7 weekday `getDay()` values in left→right column order for the header
 *  row. Monday: [1,2,3,4,5,6,0]; Sunday: [0,1,2,3,4,5,6]. */
export function weekdayOrder(weekStart: WeekStart): number[] {
  const first = firstDow(weekStart);
  return Array.from({ length: 7 }, (_, i) => (first + i) % 7);
}
