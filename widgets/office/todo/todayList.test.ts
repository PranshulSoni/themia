// The virtual Today list (SPEC.md [S-52]). Node-importable like dueDate.test.ts
// — no DOM, no i18n.
import { describe, it, expect, afterEach } from 'vitest';
import { TODAY_LIST_ID, isTodayList, todayTasks, todayDueDate } from './todayList.ts';

const REAL_TZ = process.env.TZ;
afterEach(() => { process.env.TZ = REAL_TZ; });

const NOW = new Date(2026, 7, 10, 14, 30); // Mon 2026-08-10, local

/** A task with just the field Today reads, plus an id to assert order by. */
function task(id: string, due: string | null) {
  return { id, due_date: due };
}

describe('isTodayList [S-52]', () => {
  it('S-52_ recognises the virtual id and nothing else', () => {
    expect(isTodayList(TODAY_LIST_ID)).toBe(true);
    expect(isTodayList('AQMkAGZjNDg9PQAAAA==')).toBe(false);
    expect(isTodayList('')).toBe(false);
    expect(isTodayList(undefined)).toBe(false);
    expect(isTodayList(null)).toBe(false);
  });
});

describe('todayTasks [S-52]', () => {
  it('S-52_ keeps tasks due today and drops later ones', () => {
    const got = todayTasks([
      task('today', '2026-08-10T00:00:00.0000000'),
      task('tomorrow', '2026-08-11T00:00:00.0000000'),
      task('next-week', '2026-08-20T00:00:00.0000000'),
    ], NOW);
    expect(got.map(t => t.id)).toEqual(['today']);
  });

  it('S-52_ keeps overdue tasks, oldest first, ahead of today', () => {
    const got = todayTasks([
      task('today', '2026-08-10T00:00:00.0000000'),
      task('yesterday', '2026-08-09T00:00:00.0000000'),
      task('last-week', '2026-08-03T00:00:00.0000000'),
    ], NOW);
    expect(got.map(t => t.id)).toEqual(['last-week', 'yesterday', 'today']);
  });

  it('S-52_ drops undated tasks — Today is a schedule, not every task', () => {
    const got = todayTasks([
      task('undated', null),
      task('blank', ''),
      task('today', '2026-08-10T00:00:00.0000000'),
    ], NOW);
    expect(got.map(t => t.id)).toEqual(['today']);
  });

  it('S-52_ keeps the backend order within one day (stable sort)', () => {
    const got = todayTasks([
      task('a', '2026-08-10T00:00:00.0000000'),
      task('b', '2026-08-10T00:00:00.0000000'),
      task('c', '2026-08-10T00:00:00.0000000'),
    ], NOW);
    expect(got.map(t => t.id)).toEqual(['a', 'b', 'c']);
  });

  it('S-52_ resolves the To Do mobile encoding to the viewer day, not the raw one', () => {
    // [S-48] / #101: a UTC+7 user's "due 2026-08-10" arrives as the converted
    // local midnight. Comparing the string would file it under the 9th and drop
    // it out of Today for a viewer in that zone.
    process.env.TZ = 'Asia/Bangkok';
    const got = todayTasks([task('mobile', '2026-08-09T17:00:00.0000000')], new Date(2026, 7, 10, 9, 0));
    expect(got.map(t => t.id)).toEqual(['mobile']);
  });

  it('S-52_ leaves the input array untouched', () => {
    const input = [task('b', '2026-08-11T00:00:00.0000000'), task('a', '2026-08-10T00:00:00.0000000')];
    todayTasks(input, NOW);
    expect(input.map(t => t.id)).toEqual(['b', 'a']);
  });
});

describe('todayDueDate [S-52]', () => {
  it('S-52_ is the local day, zero-padded, with no time part', () => {
    expect(todayDueDate(new Date(2026, 7, 10, 23, 59))).toBe('2026-08-10');
    expect(todayDueDate(new Date(2026, 0, 3, 0, 0))).toBe('2026-01-03');
  });

  it('S-52_ round-trips into Today for the day it names', () => {
    const now = new Date(2026, 7, 10, 22, 0);
    const created = task('new', `${todayDueDate(now)}T00:00:00.0000000`);
    expect(todayTasks([created], now).map(t => t.id)).toEqual(['new']);
  });
});
