import { describe, it, expect } from 'vitest';
import {
  parseEntries, addEntry, setEntryNote, removeEntry,
  dateKey, parseDateKey, shiftDays, dayTotals, dayAverage, heatBucket,
  heatmapCells, heatmapColumnDates, windowAverage, loggedStreak,
  entriesForDay, latestOn, parseHistoryWeeks, parseStorageMode,
  MAX_ENTRIES, HEATMAP_COLS, DEFAULT_HISTORY_WEEKS, DEFAULT_STORAGE_MODE,
  type MoodEntry, type DayMood,
} from './mood.ts';

// Entries live in the widget's own config bag (an untrusted `unknown` at read
// time). These pure helpers own the trust boundary, every mutation and every
// chart derivation, so they carry the tests. Tags mirror SPEC.md.

/** Local-time entry factory, so day bucketing is asserted in the zone the
 *  widget actually renders in rather than UTC. */
function at(y: number, m: number, d: number, hh = 12, mm = 0): string {
  return new Date(y, m - 1, d, hh, mm).toISOString();
}

function entry(id: string, iso: string, level: 1 | 2 | 3 | 4 | 5, note = ''): MoodEntry {
  return { id, at: iso, level, note };
}

describe('parseEntries [S-02]', () => {
  it('returns [] for non-array input (absent / corrupt config)', () => {
    expect(parseEntries(undefined)).toEqual([]);
    expect(parseEntries(null)).toEqual([]);
    expect(parseEntries('nope')).toEqual([]);
    expect(parseEntries({ entries: [] })).toEqual([]);
    expect(parseEntries(42)).toEqual([]);
  });

  it('round-trips well-formed entries', () => {
    const iso = at(2026, 8, 10, 9);
    expect(parseEntries([{ id: 'a', at: iso, level: 4, note: 'good run' }]))
      .toEqual([{ id: 'a', at: iso, level: 4, note: 'good run' }]);
  });

  it('defaults a missing/invalid note to "" without dropping the entry', () => {
    const iso = at(2026, 8, 10);
    expect(parseEntries([
      { id: 'a', at: iso, level: 3 },
      { id: 'b', at: iso, level: 3, note: 17 },
    ])).toEqual([
      { id: 'a', at: iso, level: 3, note: '' },
      { id: 'b', at: iso, level: 3, note: '' },
    ]);
  });

  it('drops malformed entries but keeps valid siblings', () => {
    const iso = at(2026, 8, 10);
    expect(parseEntries([
      { id: 'ok', at: iso, level: 5, note: 'keep' },
      { id: 'bad-level', at: iso, level: 0 },
      { id: 'bad-level-high', at: iso, level: 6 },
      { id: 'fractional', at: iso, level: 2.5 },
      { id: 'string-level', at: iso, level: '3' },
      { id: 'unparseable', at: 'not a date', level: 3 },
      { at: iso, level: 3 },
      { id: 'no-at', level: 3 },
      null,
      'string-entry',
    ])).toEqual([{ id: 'ok', at: iso, level: 5, note: 'keep' }]);
  });

  it('sorts newest first regardless of stored order', () => {
    const parsed = parseEntries([
      { id: 'old', at: at(2026, 8, 1), level: 2 },
      { id: 'new', at: at(2026, 8, 9), level: 4 },
      { id: 'mid', at: at(2026, 8, 5), level: 3 },
    ]);
    expect(parsed.map((e) => e.id)).toEqual(['new', 'mid', 'old']);
  });

  it('ignores unknown extra fields', () => {
    const iso = at(2026, 8, 10);
    expect(parseEntries([{ id: 'a', at: iso, level: 3, note: '', tags: ['x'], junk: 1 }]))
      .toEqual([{ id: 'a', at: iso, level: 3, note: '' }]);
  });
});

describe('addEntry [S-03]', () => {
  const base = [entry('old', at(2026, 8, 9), 3)];

  it('inserts the new entry newest-first', () => {
    const next = addEntry(base, 5, at(2026, 8, 10), 'new');
    expect(next.map((e) => e.id)).toEqual(['new', 'old']);
    expect(next[0]).toEqual({ id: 'new', at: at(2026, 8, 10), level: 5, note: '' });
  });

  it('logs a SECOND entry the same day rather than replacing the first', () => {
    const first = addEntry([], 2, at(2026, 8, 10, 9), 'a');
    const second = addEntry(first, 4, at(2026, 8, 10, 17), 'b');
    expect(second.map((e) => e.id)).toEqual(['b', 'a']);
  });

  it('re-sorts when the new entry is older than an existing one', () => {
    const next = addEntry(base, 1, at(2026, 8, 1), 'backdated');
    expect(next.map((e) => e.id)).toEqual(['old', 'backdated']);
  });

  it('is a no-op for an out-of-scale level or unparseable timestamp', () => {
    expect(addEntry(base, 0 as unknown as 1, at(2026, 8, 10), 'x')).toEqual(base);
    expect(addEntry(base, 3, 'not a date', 'x')).toEqual(base);
  });

  it('caps the list at MAX_ENTRIES by pruning the OLDEST', () => {
    const full: MoodEntry[] = Array.from({ length: MAX_ENTRIES }, (_, i) =>
      entry(`e${i}`, new Date(2020, 0, 1, 0, i).toISOString(), 3));
    const next = addEntry(full, 5, at(2026, 8, 10), 'newest');
    expect(next.length).toBe(MAX_ENTRIES);
    expect(next[0].id).toBe('newest');
    expect(next.some((e) => e.id === 'e0')).toBe(false);
    expect(next.some((e) => e.id === 'e1')).toBe(true);
  });

  it('does not mutate the input array', () => {
    const input = [entry('a', at(2026, 8, 9), 3)];
    addEntry(input, 5, at(2026, 8, 10), 'b');
    expect(input.map((e) => e.id)).toEqual(['a']);
  });
});

describe('setEntryNote / removeEntry [S-05]', () => {
  const base = [entry('a', at(2026, 8, 10), 4), entry('b', at(2026, 8, 9), 2, 'old note')];

  it('attaches a note to the matching entry only', () => {
    expect(setEntryNote(base, 'a', 'slept well')).toEqual([
      entry('a', at(2026, 8, 10), 4, 'slept well'),
      entry('b', at(2026, 8, 9), 2, 'old note'),
    ]);
  });

  it('clears a note when given an empty string', () => {
    expect(setEntryNote(base, 'b', '')[1].note).toBe('');
  });

  it('returns an equal list for an unknown id', () => {
    expect(setEntryNote(base, 'zzz', 'x')).toEqual(base);
    expect(removeEntry(base, 'zzz')).toEqual(base);
  });

  it('removes only the matching entry', () => {
    expect(removeEntry(base, 'a').map((e) => e.id)).toEqual(['b']);
  });
});

describe('dateKey / parseDateKey / shiftDays [S-06]', () => {
  it('keys by LOCAL calendar day, so a late-night mood counts for that day', () => {
    expect(dateKey(new Date(2026, 7, 10, 23, 45))).toBe('2026-08-10');
    expect(dateKey(new Date(2026, 7, 10, 0, 5))).toBe('2026-08-10');
  });

  it('round-trips a key back to local midnight', () => {
    const d = parseDateKey('2026-08-10');
    expect(d.getFullYear()).toBe(2026);
    expect(d.getMonth()).toBe(7);
    expect(d.getDate()).toBe(10);
    expect(d.getHours()).toBe(0);
  });

  it('yields an Invalid Date for a malformed key rather than throwing', () => {
    expect(Number.isNaN(parseDateKey('nope').getTime())).toBe(true);
  });

  it('shifts by calendar days across a month boundary', () => {
    expect(dateKey(shiftDays(new Date(2026, 7, 1, 12), -1))).toBe('2026-07-31');
    expect(dateKey(shiftDays(new Date(2026, 7, 31, 12), 1))).toBe('2026-09-01');
  });
});

describe('dayTotals [S-06]', () => {
  const end = new Date(2026, 7, 10, 12);
  const entries = [
    entry('a', at(2026, 8, 10, 9), 4),
    entry('b', at(2026, 8, 10, 21), 2),
    entry('c', at(2026, 8, 8), 5),
    entry('old', at(2026, 6, 1), 1), // outside the window
  ];

  it('returns one slot per day, oldest first, ending at `end`', () => {
    const totals = dayTotals(entries, end, 4);
    expect(totals.map((d) => d.date)).toEqual([
      '2026-08-07', '2026-08-08', '2026-08-09', '2026-08-10',
    ]);
  });

  it('sums and counts the entries that land on each day', () => {
    const totals = dayTotals(entries, end, 4);
    expect(totals[3]).toEqual({ date: '2026-08-10', count: 2, sum: 6 });
    expect(totals[1]).toEqual({ date: '2026-08-08', count: 1, sum: 5 });
  });

  it('leaves untouched days at count 0 rather than omitting them', () => {
    const totals = dayTotals(entries, end, 4);
    expect(totals[2]).toEqual({ date: '2026-08-09', count: 0, sum: 0 });
    expect(dayAverage(totals[2])).toBe(0);
  });

  it('ignores entries outside the window instead of folding them into an edge day', () => {
    const totals = dayTotals(entries, end, 4);
    expect(totals.reduce((n, d) => n + d.count, 0)).toBe(3);
  });

  it('averages a day with several entries', () => {
    expect(dayAverage(dayTotals(entries, end, 4)[3])).toBe(3);
  });
});

describe('heatBucket [S-07]', () => {
  it('is 0 only when nothing was logged', () => {
    expect(heatBucket({ date: 'x', count: 0, sum: 0 })).toBe(0);
  });

  it('never buckets a logged day as 0, even at the worst level', () => {
    expect(heatBucket({ date: 'x', count: 1, sum: 1 })).toBe(1);
    expect(heatBucket({ date: 'x', count: 3, sum: 3 })).toBe(1);
  });

  it('rounds the day average into 1..5', () => {
    expect(heatBucket({ date: 'x', count: 2, sum: 6 })).toBe(3);   // 3.0
    expect(heatBucket({ date: 'x', count: 2, sum: 7 })).toBe(4);   // 3.5 → 4
    expect(heatBucket({ date: 'x', count: 1, sum: 5 })).toBe(5);
  });
});

describe('heatmapCells / heatmapColumnDates [S-08]', () => {
  function totals(n: number, from = 1): DayMood[] {
    return Array.from({ length: n }, (_, i) => ({
      date: dateKey(new Date(2026, 7, from + i)), count: 0, sum: 0,
    }));
  }

  it('pads with leading blanks so the window ends at the end of a row', () => {
    const cells = heatmapCells(totals(10));
    expect(cells.length % HEATMAP_COLS).toBe(0);
    expect(cells.slice(0, 4).every((c) => c === null)).toBe(true);
    expect(cells[4]).not.toBeNull();
    expect(cells[cells.length - 1]).not.toBeNull();
  });

  it('adds no padding when the window already fills whole rows', () => {
    expect(heatmapCells(totals(14)).length).toBe(14);
  });

  it('puts one fixed weekday per column', () => {
    const cells = heatmapCells(totals(14));
    const cols = heatmapColumnDates(cells);
    expect(cols.length).toBe(HEATMAP_COLS);
    for (const key of cols) {
      expect(key).not.toBeNull();
    }
    // Every cell in a column shares its column date's weekday.
    cells.forEach((c, i) => {
      if (!c) return;
      const colKey = cols[i % HEATMAP_COLS]!;
      expect(parseDateKey(c.date).getDay()).toBe(parseDateKey(colKey).getDay());
    });
  });
});

describe('windowAverage [S-09]', () => {
  it('is null for a window with no entries (never a misleading 0)', () => {
    expect(windowAverage([{ date: 'a', count: 0, sum: 0 }])).toBeNull();
    expect(windowAverage([])).toBeNull();
  });

  it('means over ENTRIES, not over days', () => {
    // Two entries on day one (5, 5) and one on day two (2) → 4, not 3.5.
    expect(windowAverage([
      { date: 'a', count: 2, sum: 10 },
      { date: 'b', count: 1, sum: 2 },
    ])).toBe(4);
  });
});

describe('loggedStreak [S-10]', () => {
  function day(count: number): DayMood {
    return { date: 'x', count, sum: count * 3 };
  }

  it('counts consecutive logged days ending today', () => {
    expect(loggedStreak([day(0), day(1), day(1), day(2)])).toBe(3);
  });

  it('does not break the streak when today is not yet logged', () => {
    expect(loggedStreak([day(1), day(1), day(0)])).toBe(2);
  });

  it('breaks on an earlier gap', () => {
    expect(loggedStreak([day(1), day(0), day(1)])).toBe(1);
  });

  it('is 0 for an empty window', () => {
    expect(loggedStreak([day(0), day(0)])).toBe(0);
    expect(loggedStreak([])).toBe(0);
  });
});

describe('entriesForDay / latestOn [S-04]/[S-12]', () => {
  const entries = parseEntries([
    { id: 'morning', at: at(2026, 8, 10, 8), level: 2, note: 'rough start' },
    { id: 'evening', at: at(2026, 8, 10, 20), level: 5, note: '' },
    { id: 'yesterday', at: at(2026, 8, 9, 12), level: 3, note: '' },
  ]);

  it('lists a day\'s entries newest first', () => {
    expect(entriesForDay(entries, '2026-08-10').map((e) => e.id)).toEqual(['evening', 'morning']);
  });

  it('returns [] for a day with nothing logged', () => {
    expect(entriesForDay(entries, '2026-08-08')).toEqual([]);
  });

  it('picks the most recent entry on the local current day', () => {
    expect(latestOn(entries, new Date(2026, 7, 10, 22))?.id).toBe('evening');
  });

  it('is null when nothing was logged today', () => {
    expect(latestOn(entries, new Date(2026, 7, 11, 10))).toBeNull();
  });
});

describe('parseStorageMode [S-16]', () => {
  it('accepts the supported mode', () => {
    expect(parseStorageMode('local')).toBe('local');
  });

  it('falls back to the default for an absent or unrecognised value', () => {
    expect(parseStorageMode(undefined)).toBe(DEFAULT_STORAGE_MODE);
    expect(parseStorageMode(null)).toBe(DEFAULT_STORAGE_MODE);
    expect(parseStorageMode('account')).toBe(DEFAULT_STORAGE_MODE);
    expect(parseStorageMode(7)).toBe(DEFAULT_STORAGE_MODE);
    expect(parseStorageMode({ mode: 'local' })).toBe(DEFAULT_STORAGE_MODE);
  });
});

describe('parseHistoryWeeks [S-11]', () => {
  it('accepts the supported windows', () => {
    expect(parseHistoryWeeks(4)).toBe(4);
    expect(parseHistoryWeeks(26)).toBe(26);
  });

  it('falls back to the default for anything else', () => {
    expect(parseHistoryWeeks(undefined)).toBe(DEFAULT_HISTORY_WEEKS);
    expect(parseHistoryWeeks(7)).toBe(DEFAULT_HISTORY_WEEKS);
    expect(parseHistoryWeeks('8')).toBe(DEFAULT_HISTORY_WEEKS);
    expect(parseHistoryWeeks(null)).toBe(DEFAULT_HISTORY_WEEKS);
  });
});
