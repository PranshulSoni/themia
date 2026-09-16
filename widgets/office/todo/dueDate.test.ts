// Due-date resolution for the To Do widget (SPEC.md [S-48], [S-09], [S-10]).
// Kept node-importable like screentimeDisplay.test.ts — no DOM, no i18n.
import { describe, it, expect, afterEach } from 'vitest';
import { dueDay, resolveDue, isDuePast } from './dueDate.ts';

// The whole point of this module is what a due date means to a viewer in a
// given zone, so the tests run in real ones. Node re-reads process.env.TZ on
// each Date construction, so this is enough to relocate the "viewer".
const REAL_TZ = process.env.TZ;
afterEach(() => { process.env.TZ = REAL_TZ; });

function inTZ(tz: string): void { process.env.TZ = tz; }

/** Local Y-M-D of a Date, for assertions that don't care about the clock. */
function ymd(d: Date | null): string | null {
  if (!d) return null;
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

// Regression for nwalser/themia-releases#101: tasks created in the Microsoft
// To Do mobile app came back one day early — a task due today (Friday) showed
// "overdue", one due next Monday showed Sunday. The app writes the *creator's*
// local midnight already converted to UTC (UTC+7 user → `...T17:00:00` the day
// before), and the old parser read that zoneless string as local time, so the
// widget kept the converted instant's date instead of the intended day.
describe('dueDay — Microsoft To Do mobile encoding [S-48] (#101)', () => {
  it('S-48_ resolves a converted local midnight back to the intended day', () => {
    inTZ('Asia/Bangkok'); // UTC+7, the zone that wrote the value
    expect(ymd(dueDay('2026-08-09T17:00:00.0000000'))).toBe('2026-08-10');
  });

  it('S-48_ a task due next Monday reads as Monday, not Sunday', () => {
    inTZ('Asia/Bangkok');
    const day = dueDay('2026-08-09T17:00:00.0000000');
    expect(day?.getDay()).toBe(1); // 1 = Monday
  });

  it('S-48_ a task due today is "today", not "overdue"', () => {
    inTZ('Asia/Bangkok');
    const now = new Date(2026, 7, 7, 9, 30); // Friday 7 Aug 2026, local
    // Friday's midnight in UTC+7, as Graph returns it.
    const due = '2026-08-06T17:00:00.0000000';
    expect(resolveDue(due, now)?.label).toBe('today');
    expect(isDuePast(due, now)).toBe(false);
  });
});

// The other encoding: midnight of the intended day. Reading that through UTC
// would push viewers at a negative offset back a day, so the date part is taken
// verbatim. Google Tasks always encodes this way (`due` is documented as
// date-only), which made the same off-by-one latent for the Americas.
describe('dueDay — midnight-of-the-intended-day encodings [S-48]', () => {
  it('S-48_ Graph zoneless midnight keeps its day in the Americas', () => {
    inTZ('America/New_York'); // UTC-4
    expect(ymd(dueDay('2026-08-10T00:00:00.0000000'))).toBe('2026-08-10');
  });

  it('S-48_ Google UTC midnight keeps its day in the Americas', () => {
    inTZ('America/New_York');
    expect(ymd(dueDay('2026-08-10T00:00:00.000Z'))).toBe('2026-08-10');
  });

  it('S-48_ Google UTC midnight keeps its day east of UTC', () => {
    inTZ('Asia/Bangkok');
    expect(ymd(dueDay('2026-08-10T00:00:00.000Z'))).toBe('2026-08-10');
  });

  it('S-48_ a bare date has no time to misread', () => {
    inTZ('America/New_York');
    expect(ymd(dueDay('2026-08-10'))).toBe('2026-08-10');
  });

  it('S-48_ midnight at an explicit offset is that offset\'s day', () => {
    inTZ('America/New_York');
    expect(ymd(dueDay('2026-08-10T00:00:00+07:00'))).toBe('2026-08-10');
  });
});

describe('dueDay — instants [S-48]', () => {
  it('S-48_ a real timestamp resolves to the viewer\'s local day', () => {
    inTZ('Asia/Bangkok'); // UTC+7: 20:00Z is already the next morning here
    expect(ymd(dueDay('2026-08-09T20:00:00.000Z'))).toBe('2026-08-10');
    inTZ('America/New_York'); // UTC-4: still the 9th
    expect(ymd(dueDay('2026-08-09T20:00:00.000Z'))).toBe('2026-08-09');
  });

  it('S-48_ absent or unparseable values yield no day', () => {
    expect(dueDay(null)).toBe(null);
    expect(dueDay(undefined)).toBe(null);
    expect(dueDay('')).toBe(null);
    expect(dueDay('not a date')).toBe(null);
    expect(resolveDue(null)).toBe(null);
    expect(isDuePast(null)).toBe(false);
  });
});

// [S-09] overdue / today / tomorrow / short weekday / "Mon Day".
describe('resolveDue labels [S-09]', () => {
  const now = new Date(2026, 7, 7, 14, 0); // Friday 7 Aug 2026, local

  it('S-09_ labels each day offset', () => {
    const at = (day: string) => resolveDue(day, now)?.label;
    expect(at('2026-08-06')).toBe('overdue');
    expect(at('2026-08-07')).toBe('today');
    expect(at('2026-08-08')).toBe('tomorrow');
    expect(at('2026-08-09')).toBe('weekday');
    expect(at('2026-08-13')).toBe('weekday'); // +6, last day inside the week
    expect(at('2026-08-14')).toBe('date');    // +7 switches to "Mon Day"
  });

  it('S-10_ only days before today are past', () => {
    expect(isDuePast('2026-08-06', now)).toBe(true);
    expect(isDuePast('2026-08-07', now)).toBe(false);
    expect(isDuePast('2026-08-08', now)).toBe(false);
  });

  it('S-09_ day counts survive a DST boundary', () => {
    inTZ('Europe/Zurich'); // clocks go back on 25 Oct 2026
    const before = new Date(2026, 9, 24, 12, 0); // Saturday 24 Oct
    expect(resolveDue('2026-10-25', before)?.days).toBe(1);
    expect(resolveDue('2026-10-26', before)?.days).toBe(2);
    expect(resolveDue('2026-10-25', before)?.label).toBe('tomorrow');
  });
});
