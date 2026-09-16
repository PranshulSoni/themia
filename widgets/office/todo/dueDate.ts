// Due-date resolution for the To Do widget (SPEC.md [S-09], [S-10], [S-48]).
// Extracted from Todo.tsx so the day-resolution contract can be unit-tested
// without mounting the widget. Kept i18n-free and node-importable: the widget
// maps the returned label kind onto translated text.

import { gdt } from '../calendar/dates.ts';

/**
 * A To Do due date is a *calendar day*, not an instant, but neither backend
 * says so in its type. What arrives is one of:
 *
 *  - Microsoft Graph `dueDateTime.dateTime` — a zoneless datetime whose zone is
 *    carried in a sibling `timeZone` field we don't forward. Two encodings are
 *    seen in the wild: midnight of the intended day (`2026-08-10T00:00:00.0000000`),
 *    and the *creator's* local midnight already converted to UTC
 *    (`2026-08-09T17:00:00.0000000` for a UTC+7 user, i.e. the To Do mobile app).
 *  - Google Tasks `due` — always UTC midnight of the intended day
 *    (`2026-08-10T00:00:00.000Z`); the API documents the time part as meaningless.
 *
 * So the intended day is read in two steps:
 *
 *  1. Wall clock is midnight → the value is date-only under every encoding
 *     above, so the intended day *is* the date part. Read it as such; converting
 *     it through UTC would push negative-offset viewers (the Americas) back a day.
 *  2. Wall clock is not midnight → the value is a real instant (a converted
 *     local midnight, or a demo/fixture timestamp). Resolve the zone the way the
 *     calendar widget does — zoneless means UTC, issue #48 — and take the
 *     viewer's local day, which lands back on midnight for the zone that wrote it.
 *
 * Returns local midnight of the intended day, or null when unparseable.
 */
export function dueDay(iso: string | null | undefined): Date | null {
  if (!iso) return null;
  const m = DUE_RE.exec(iso);
  if (!m) return null;
  const [, y, mo, d, hh, mi, ss] = m;
  if (!hh || (hh === '00' && mi === '00' && (ss ?? '00') === '00')) {
    return new Date(Number(y), Number(mo) - 1, Number(d));
  }
  const at = gdt(iso);
  if (Number.isNaN(at.getTime())) return null;
  return new Date(at.getFullYear(), at.getMonth(), at.getDate());
}

/** `YYYY-MM-DD`, optionally followed by a time. Captures the wall clock exactly
 *  as written, before any zone conversion. */
const DUE_RE = /^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2})(?::(\d{2}))?)?/;

/** Which of the [S-09] labels a due date gets. `weekday` / `date` are rendered
 *  from `day` by the widget, in the user's language. */
export type DueLabel = 'overdue' | 'today' | 'tomorrow' | 'weekday' | 'date';

export interface Due {
  /** Local midnight of the intended day. */
  day: Date;
  label: DueLabel;
  /** Whole days from today; negative when overdue. */
  days: number;
}

/** [S-09] Resolve a due value into the day it means plus the label it earns. */
export function resolveDue(iso: string | null | undefined, now: Date = new Date()): Due | null {
  const day = dueDay(iso);
  if (!day) return null;
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  // Round the difference: a DST boundary between the two dates makes the span
  // 23 or 25 hours, which would otherwise floor/ceil to the wrong day count.
  const days = Math.round((day.getTime() - today.getTime()) / 86_400_000);
  const label: DueLabel =
    days < 0 ? 'overdue' :
    days === 0 ? 'today' :
    days === 1 ? 'tomorrow' :
    days < 7 ? 'weekday' : 'date';
  return { day, label, days };
}

/** [S-10] True when the due day is before today (highlighted as overdue). */
export function isDuePast(iso: string | null | undefined, now: Date = new Date()): boolean {
  return (resolveDue(iso, now)?.days ?? 0) < 0;
}
