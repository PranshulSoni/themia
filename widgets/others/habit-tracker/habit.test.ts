import { describe, it, expect } from 'vitest';
import {
  dateKey, moveHabit, renameHabit, addHabit, periodKey,
  streak, PERIOD_NOUNS, newHabitId, rowIndexAtY, sameHabitOrder, recentHistory,
  clampHistoryDots, DEFAULT_HISTORY_DOTS, MAX_HISTORY_DOTS, type Habit,
} from './habit.ts';

describe('habit: addHabit [S-16]', () => {
  const base: Habit[] = [{ id: 'a', name: 'Read' }];

  it('appends the habit at the bottom, trimming the name', () => {
    expect(addHabit(base, '  Stretch  ', 'b')).toEqual([
      { id: 'a', name: 'Read' },
      { id: 'b', name: 'Stretch' },
    ]);
  });

  it('creates nothing from a blank or whitespace-only name', () => {
    expect(addHabit(base, '', 'b')).toBe(base);
    expect(addHabit(base, '   ', 'b')).toBe(base);
  });

  it('does not mutate the input array', () => {
    const input: Habit[] = [{ id: 'a', name: 'Read' }];
    addHabit(input, 'Stretch', 'b');
    expect(input).toEqual([{ id: 'a', name: 'Read' }]);
  });
});

describe('habit: dateKey', () => {
  it('formats local Y-M-D zero-padded', () => {
    expect(dateKey(new Date(2026, 0, 5))).toBe('2026-01-05');
    expect(dateKey(new Date(2026, 11, 31))).toBe('2026-12-31');
  });
});

describe('habit: streak', () => {
  const today = new Date(2026, 5, 5); // 2026-06-05
  const k = (d: number) => dateKey(new Date(2026, 5, d));

  it('counts consecutive days ending today', () => {
    expect(streak([k(3), k(4), k(5)], today)).toBe(3);
  });

  it('today not yet done does NOT break the streak (counts to yesterday)', () => {
    expect(streak([k(3), k(4)], today)).toBe(2);
  });

  it('a gap before today breaks the streak', () => {
    expect(streak([k(1), k(2), k(5)], today)).toBe(1);
  });

  it('empty log is zero', () => {
    expect(streak([], today)).toBe(0);
  });
});

describe('habit: reset cadence', () => {
  it('periodKey buckets by day / ISO week / month', () => {
    const d = new Date(2026, 5, 5); // Fri 2026-06-05 → ISO week 23
    expect(periodKey(d, 'daily')).toBe('2026-06-05');
    expect(periodKey(d, 'weekly')).toBe('2026-W23');
    expect(periodKey(d, 'monthly')).toBe('2026-06');
  });

  it('weekly streak counts consecutive ISO weeks', () => {
    const today = new Date(2026, 5, 19); // week 25
    const wk = (d: number) => periodKey(new Date(2026, 5, d), 'weekly');
    // weeks 23, 24, 25 (5th, 12th, 19th of June 2026)
    expect(streak([wk(5), wk(12), wk(19)], today, 'weekly')).toBe(3);
    // gap: missing week 24 → only current week counts
    expect(streak([wk(5), wk(19)], today, 'weekly')).toBe(1);
  });

  it('monthly streak counts consecutive months', () => {
    const today = new Date(2026, 5, 15); // June
    const mk = (m: number) => periodKey(new Date(2026, m, 1), 'monthly');
    expect(streak([mk(3), mk(4), mk(5)], today, 'monthly')).toBe(3); // Apr,May,Jun
  });

  // [S-07] hourly cadence: checkbox clears each local hour.
  it('periodKey buckets by local hour', () => {
    expect(periodKey(new Date(2026, 5, 5, 14, 30), 'hourly')).toBe('2026-06-05-H14');
    expect(periodKey(new Date(2026, 5, 5, 15, 0), 'hourly')).toBe('2026-06-05-H15');
    expect(periodKey(new Date(2026, 5, 5, 9, 5), 'hourly')).toBe('2026-06-05-H09');
  });

  it('hourly streak counts consecutive hours; a gap breaks it', () => {
    const today = new Date(2026, 5, 5, 15, 0);
    const hk = (h: number) => periodKey(new Date(2026, 5, 5, h, 0), 'hourly');
    expect(streak([hk(13), hk(14), hk(15)], today, 'hourly')).toBe(3);
    expect(streak([hk(13), hk(15)], today, 'hourly')).toBe(1); // missing 14:00
  });

  // [S-07] custom interval cadence: checkbox clears every N minutes.
  it('custom interval keys differ across an N-minute boundary', () => {
    const at = (h: number, m: number) => new Date(2026, 5, 5, h, m);
    // Two instants exactly 30 min apart fall in adjacent 30-min buckets.
    expect(periodKey(at(9, 0), 'custom', 30)).not.toBe(periodKey(at(9, 30), 'custom', 30));
    // The same instant always maps to the same bucket.
    expect(periodKey(at(9, 0), 'custom', 30)).toBe(periodKey(at(9, 0), 'custom', 30));
  });

  it('custom streak counts consecutive sub-hour windows', () => {
    const today = new Date(2026, 5, 5, 10, 0);
    const k = (h: number, m: number) => periodKey(new Date(2026, 5, 5, h, m), 'custom', 30);
    // 30-min windows at 9:00, 9:30, 10:00
    expect(streak([k(9, 0), k(9, 30), k(10, 0)], today, 'custom', 30)).toBe(3);
    expect(streak([k(9, 0), k(10, 0)], today, 'custom', 30)).toBe(1); // missing 9:30 window
  });

  // [S-07] The check tooltip must phrase the window per cadence, not always "today".
  it('PERIOD_NOUNS phrases the window per cadence', () => {
    expect(PERIOD_NOUNS.daily).toBe('today');
    expect(PERIOD_NOUNS.hourly).toBe('this hour');
    expect(PERIOD_NOUNS.weekly).toBe('this week');
    expect(PERIOD_NOUNS.monthly).toBe('this month');
    expect(PERIOD_NOUNS.custom).toBe('this interval');
  });
});

describe('habit: moveHabit [S-09]', () => {
  const list: Habit[] = [
    { id: 'a', name: 'Water' },
    { id: 'b', name: 'Run' },
    { id: 'c', name: 'Read' },
  ];
  const ids = (l: Habit[]) => l.map((h) => h.id).join('');

  it('[S-09] moves a habit down into the target slot', () => {
    expect(ids(moveHabit(list, 'a', 'b'))).toBe('bac');
    expect(ids(moveHabit(list, 'a', 'c'))).toBe('bca');
  });

  it('[S-09] moves a habit up into the target slot', () => {
    expect(ids(moveHabit(list, 'c', 'a'))).toBe('cab');
    expect(ids(moveHabit(list, 'c', 'b'))).toBe('acb');
  });

  it('[S-09] never drops, duplicates or renames a habit', () => {
    const out = moveHabit(list, 'b', 'a');
    expect(out).toHaveLength(list.length);
    expect(new Set(out.map((h) => h.id)).size).toBe(list.length);
    expect(out.find((h) => h.id === 'b')).toEqual({ id: 'b', name: 'Run' });
  });

  it('[S-09] leaves the input untouched (no in-place mutation)', () => {
    moveHabit(list, 'a', 'c');
    expect(ids(list)).toBe('abc');
  });

  // A dragover that changes nothing must not write the config: the panel skips
  // the persist by identity, so a no-op has to return the SAME array.
  it('[S-09] a no-op move returns the input array by reference', () => {
    expect(moveHabit(list, 'a', 'a')).toBe(list);
    expect(moveHabit(list, 'zz', 'b')).toBe(list);   // stale drag source
    expect(moveHabit(list, 'a', 'zz')).toBe(list);   // unknown target
    expect(moveHabit([], 'a', 'b')).toEqual([]);
  });

  // A stale id from an interrupted drag must be inert rather than reordering
  // around a phantom habit.
  it('[S-09] an unknown id is not a drag participant', () => {
    expect(moveHabit(list, 'gone', 'b')).toBe(list);
    expect(moveHabit(list, 'a', 'gone')).toBe(list);
  });
});

// [S-13] The widget's drag reorder is a poll loop the test env cannot drive
// (it reads the OS cursor), so the two decisions it makes each tick are pulled
// out as pure functions and pinned here: which row is under the cursor, and
// whether the release changed anything worth writing.
describe('habit: drag hit-testing [S-13]', () => {
  // Three 26px rows starting at y=100, as the fit-first list lays them out.
  const rects = [
    { top: 100, bottom: 126 },
    { top: 126, bottom: 152 },
    { top: 152, bottom: 178 },
  ];

  it('[S-13] resolves the row the cursor is inside', () => {
    expect(rowIndexAtY(rects, 100)).toBe(0);
    expect(rowIndexAtY(rects, 125.9)).toBe(0);
    expect(rowIndexAtY(rects, 126)).toBe(1);
    expect(rowIndexAtY(rects, 160)).toBe(2);
  });

  // Dragging past either end must park the habit there, not fall out of the
  // gesture: the cursor leaves the list long before the button comes up.
  it('[S-13] clamps above the first row and below the last', () => {
    expect(rowIndexAtY(rects, -400)).toBe(0);
    expect(rowIndexAtY(rects, 99)).toBe(0);
    expect(rowIndexAtY(rects, 178)).toBe(2);
    expect(rowIndexAtY(rects, 5000)).toBe(2);
  });

  it('[S-13] a gap between rows resolves to the row above it', () => {
    const gapped = [{ top: 0, bottom: 20 }, { top: 24, bottom: 44 }];
    expect(rowIndexAtY(gapped, 22)).toBe(1);
  });

  it('[S-13] an empty list has no row under the cursor', () => {
    expect(rowIndexAtY([], 10)).toBe(-1);
  });
});

describe('habit: sameHabitOrder [S-13]', () => {
  const a: Habit[] = [{ id: 'a', name: 'Water' }, { id: 'b', name: 'Run' }];

  it('[S-13] a drag that comes home commits nothing', () => {
    expect(sameHabitOrder(a, [{ id: 'a', name: 'Water' }, { id: 'b', name: 'Run' }])).toBe(true);
  });

  it('[S-13] a changed order commits', () => {
    expect(sameHabitOrder(a, [{ id: 'b', name: 'Run' }, { id: 'a', name: 'Water' }])).toBe(false);
  });

  it('[S-13] compares ids, not names: a rename mid-drag is not a reorder', () => {
    expect(sameHabitOrder(a, [{ id: 'a', name: 'Hydrate' }, { id: 'b', name: 'Run' }])).toBe(true);
  });

  it('[S-13] differing lengths are never the same order', () => {
    expect(sameHabitOrder(a, a.slice(0, 1))).toBe(false);
    expect(sameHabitOrder([], [])).toBe(true);
  });
});

// [S-14] Renaming from the widget writes the same `habits` array the config
// panel does, through this helper.
describe('habit: renameHabit [S-14]', () => {
  const list: Habit[] = [
    { id: 'a', name: 'Water' },
    { id: 'b', name: 'Run' },
  ];

  it('[S-14] replaces the matching habit name, trimmed', () => {
    expect(renameHabit(list, 'a', '  Drink water  ')).toEqual([
      { id: 'a', name: 'Drink water' },
      { id: 'b', name: 'Run' },
    ]);
  });

  // The `log` is keyed by habit id, so a rename must never touch ids — losing
  // one would strand that habit's entire streak history.
  it('[S-14] never changes an id', () => {
    expect(renameHabit(list, 'a', 'X').map((h) => h.id)).toEqual(['a', 'b']);
  });

  it('[S-14] is a no-op for a blank or whitespace-only name', () => {
    expect(renameHabit(list, 'a', '')).toBe(list);
    expect(renameHabit(list, 'a', '   ')).toBe(list);
  });

  it('[S-14] returns an equal list for an unknown id', () => {
    expect(renameHabit(list, 'zzz', 'X')).toEqual(list);
  });

  it('[S-14] does not mutate the input', () => {
    const input: Habit[] = [{ id: 'a', name: 'Water' }];
    renameHabit(input, 'a', 'Other');
    expect(input).toEqual([{ id: 'a', name: 'Water' }]);
  });
});

describe('habit: newHabitId [S-16]', () => {
  it('mints a namespaced, unique id', () => {
    // `log` is keyed by habit id, so a collision would hand a new habit
    // someone else's streak.
    expect(newHabitId().startsWith('habit-')).toBe(true);
    expect(newHabitId()).not.toBe(newHabitId());
  });
});

describe('habit: recentHistory [S-15]', () => {
  const today = new Date(2026, 5, 5); // Fri 2026-06-05
  const k = (d: number) => dateKey(new Date(2026, 5, d));

  it('[S-15] returns seven buckets, oldest first, ending with the current one', () => {
    const h = recentHistory([], today);
    expect(h).toHaveLength(DEFAULT_HISTORY_DOTS);
    // May 30 → Jun 5: the six days before today, then today.
    expect(h.map((e) => e.key)).toEqual([
      '2026-05-30', '2026-05-31', k(1), k(2), k(3), k(4), k(5),
    ]);
    expect(h[h.length - 1].key).toBe(periodKey(today));
  });

  it('[S-15] a bucket is done exactly when the log holds its key', () => {
    const h = recentHistory([k(1), k(3), k(5)], today);
    expect(h.map((e) => e.done)).toEqual([false, false, true, false, true, false, true]);
  });

  it('[S-15] an unlogged habit is seven empty dots, never a short strip', () => {
    expect(recentHistory([], today).every((e) => !e.done)).toBe(true);
    expect(recentHistory([], today)).toHaveLength(DEFAULT_HISTORY_DOTS);
  });

  // Keys outside the window must not leak in: the strip is the last 7, not
  // "whatever the log happens to hold".
  it('[S-15] ignores completions older than the window', () => {
    const h = recentHistory([dateKey(new Date(2026, 4, 1))], today);
    expect(h.some((e) => e.done)).toBe(false);
  });

  it('[S-15] walks the cadence, not the calendar day: weekly gives seven ISO weeks', () => {
    const h = recentHistory([], today, 'weekly');
    expect(h.map((e) => e.key)).toEqual(
      [23, 22, 21, 20, 19, 18, 17].reverse().map((w) => `2026-W${w}`),
    );
  });

  it('[S-15] monthly walks seven months back across the year boundary', () => {
    const h = recentHistory([], new Date(2026, 1, 15), 'monthly'); // Feb 2026
    expect(h.map((e) => e.key)).toEqual(
      ['2025-08', '2025-09', '2025-10', '2025-11', '2025-12', '2026-01', '2026-02'],
    );
  });

  it('[S-15] hourly walks seven local hours', () => {
    const h = recentHistory([], new Date(2026, 5, 5, 14, 30), 'hourly');
    expect(h.map((e) => e.key)).toEqual(
      ['H08', 'H09', 'H10', 'H11', 'H12', 'H13', 'H14'].map((x) => `2026-06-05-${x}`),
    );
  });

  // The dots and the flame must never disagree about what "one back" means:
  // both walk `prevPeriod`, so a full window is a streak of at least 7.
  it('[S-15] agrees with streak on a fully completed window', () => {
    const keys = recentHistory([], today).map((e) => e.key);
    expect(recentHistory(keys, today).every((e) => e.done)).toBe(true);
    expect(streak(keys, today)).toBe(DEFAULT_HISTORY_DOTS);
  });

  it('[S-15] each dot carries a date inside its own bucket, for the tooltip', () => {
    for (const e of recentHistory([], today)) {
      expect(periodKey(e.date)).toBe(e.key);
    }
  });
});

describe('habit: configurable dot count [S-15]', () => {
  const today = new Date(2026, 5, 5); // 2026-06-05
  const k = (d: number) => dateKey(new Date(2026, 5, d));

  it('[S-15] renders exactly the configured number of buckets', () => {
    for (const n of [1, 3, 7, 14, 31]) {
      expect(recentHistory([], today, 'daily', 30, n)).toHaveLength(n);
    }
  });

  it('[S-15] still ends on the current bucket whatever the count', () => {
    for (const n of [1, 5, 31]) {
      const h = recentHistory([], today, 'daily', 30, n);
      expect(h[h.length - 1].key).toBe(periodKey(today));
    }
  });

  it('[S-15] a longer strip is the shorter one with older buckets prepended', () => {
    const short = recentHistory([], today, 'daily', 30, 3).map((e) => e.key);
    const long = recentHistory([], today, 'daily', 30, 10).map((e) => e.key);
    expect(long.slice(-3)).toEqual(short);
  });

  it('[S-15] zero dots turns the strip off entirely', () => {
    expect(recentHistory([k(5)], today, 'daily', 30, 0)).toEqual([]);
  });

  // Config is a trust boundary: a hand-edited layout file can hold anything, and
  // an unclamped count is the bound of a render loop.
  it('[S-15] clamps a stored count that is out of range, absent or junk', () => {
    expect(clampHistoryDots(undefined)).toBe(DEFAULT_HISTORY_DOTS);
    // NaN and Infinity are not out-of-range counts, they are junk: they fall
    // back to the default rather than clamping to an end of the range.
    expect(clampHistoryDots(NaN)).toBe(DEFAULT_HISTORY_DOTS);
    expect(clampHistoryDots(Infinity)).toBe(DEFAULT_HISTORY_DOTS);
    expect(clampHistoryDots(-5)).toBe(0);
    expect(clampHistoryDots(9999)).toBe(MAX_HISTORY_DOTS);
    expect(clampHistoryDots(7.9)).toBe(7);
  });

  it('[S-15] recentHistory clamps too, so a junk count can never size the loop', () => {
    expect(recentHistory([], today, 'daily', 30, -5)).toEqual([]);
    expect(recentHistory([], today, 'daily', 30, 9999)).toHaveLength(MAX_HISTORY_DOTS);
    expect(recentHistory([], today, 'daily', 30, NaN)).toHaveLength(DEFAULT_HISTORY_DOTS);
  });
});
