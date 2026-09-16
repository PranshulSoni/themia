// Habit Tracker device lists [S-18]..[S-21].
//
// `habitLists.ts` is the pure half: the SHAPE of a list and the rules for
// mutating one. Everything here is immutable and node-safe, so the rules that
// protect a user's streaks are pinned without a DOM or a storage stub.

import { describe, it, expect } from 'vitest';
import { readSource } from '../../../tests/cssRules.ts';
import {
  parseHabits, parseLog, parseHabitLists, findHabitList, listHabits, listLog,
  addHabitList, renameHabitList, removeHabitList, setListHabits, setListLog,
  upsertHabitList, uniqueHabitListName, type HabitList,
} from './habitLists.ts';

const list = (over: Partial<HabitList> = {}): HabitList => ({
  id: 'l1', name: 'Mornings', habits: [{ id: 'h1', name: 'Read' }], log: { h1: ['2026-08-10'] }, ...over,
});

describe('[S-18] parsing an untrusted pool', () => {
  it('a non-array is an empty pool, never a throw', () => {
    expect(parseHabitLists(undefined)).toEqual([]);
    expect(parseHabitLists(null)).toEqual([]);
    expect(parseHabitLists('nope')).toEqual([]);
    expect(parseHabitLists({ id: 'l1' })).toEqual([]);
  });

  it('drops entries with no usable id or name rather than repairing them', () => {
    const raw = [
      { id: 'ok', name: 'Keep', habits: [], log: {} },
      { id: '', name: 'blank id' },
      { name: 'no id' },
      { id: 'x' },            // no name
      null,
      'string',
    ];
    expect(parseHabitLists(raw).map(l => l.id)).toEqual(['ok']);
  });

  it('keeps the FIRST of a repeated id, so a write cannot land on the wrong list', () => {
    const raw = [
      { id: 'dup', name: 'First', habits: [], log: {} },
      { id: 'dup', name: 'Second', habits: [], log: {} },
    ];
    const out = parseHabitLists(raw);
    expect(out).toHaveLength(1);
    expect(out[0].name).toBe('First');
  });

  it('narrows habits and the log through their own parsers', () => {
    const out = parseHabitLists([{ id: 'l', name: 'L', habits: 'nope', log: 7 }]);
    expect(out[0].habits).toEqual([]);
    expect(out[0].log).toEqual({});
  });
});

describe('[S-18] parsing habits and logs', () => {
  it('drops habits without a string id and name, and repeated ids', () => {
    const raw = [
      { id: 'a', name: 'A' },
      { id: 'a', name: 'dupe' },
      { id: '', name: 'blank' },
      { id: 'b' },
      { name: 'c' },
      42,
    ];
    expect(parseHabits(raw)).toEqual([{ id: 'a', name: 'A' }]);
  });

  it('a log entry that is not an array of strings is dropped, not coerced', () => {
    // `streak`/`recentHistory` build a Set from these; a number in there would
    // silently never match a period key.
    expect(parseLog({ a: ['k1', 'k2'], b: 'nope', c: 5, d: [1, 'k3'] }))
      .toEqual({ a: ['k1', 'k2'], d: ['k3'] });
  });

  it('collapses duplicate period keys — the log is a set of buckets', () => {
    expect(parseLog({ a: ['k', 'k', 'j'] })).toEqual({ a: ['k', 'j'] });
  });

  it('an array or a non-object is an empty log', () => {
    expect(parseLog(['a'])).toEqual({});
    expect(parseLog(null)).toEqual({});
    expect(parseLog('x')).toEqual({});
  });
});

describe('[S-18] lookups', () => {
  const lists = [list(), list({ id: 'l2', name: 'Evenings', habits: [], log: {} })];

  it('an unknown or absent id resolves to nothing, never to another list', () => {
    // Someone else's habits appearing in a widget is worse than an empty prompt.
    expect(findHabitList(lists, 'gone')).toBeUndefined();
    expect(findHabitList(lists, undefined)).toBeUndefined();
    expect(listHabits(lists, 'gone')).toEqual([]);
    expect(listLog(lists, 'gone')).toEqual({});
  });

  it('resolves habits and log for a known id', () => {
    expect(listHabits(lists, 'l1')).toEqual([{ id: 'h1', name: 'Read' }]);
    expect(listLog(lists, 'l1')).toEqual({ h1: ['2026-08-10'] });
  });
});

describe('[S-19] pool edits are immutable', () => {
  it('adding falls back to the given name when the typed one is blank', () => {
    expect(addHabitList([], 'l', '   ', 'Fallback')[0].name).toBe('Fallback');
    expect(addHabitList([], 'l', ' Kept ', 'Fallback')[0].name).toBe('Kept');
  });

  it('a new list starts empty', () => {
    const [only] = addHabitList([], 'l', 'X', 'X');
    expect(only.habits).toEqual([]);
    expect(only.log).toEqual({});
  });

  it('rename keeps a BLANK name, so the field can be cleared before retyping', () => {
    expect(renameHabitList([list()], 'l1', '')[0].name).toBe('');
  });

  it('an unknown id leaves the pool equal', () => {
    const lists = [list()];
    expect(renameHabitList(lists, 'nope', 'X')).toEqual(lists);
    expect(removeHabitList(lists, 'nope')).toEqual(lists);
    expect(setListHabits(lists, 'nope', [])).toEqual(lists);
    expect(setListLog(lists, 'nope', {})).toEqual(lists);
  });

  it('removing takes the list, its habits and their streaks with it', () => {
    expect(removeHabitList([list()], 'l1')).toEqual([]);
  });

  it('never mutates its input', () => {
    const lists = [list()];
    const snapshot = JSON.parse(JSON.stringify(lists));
    renameHabitList(lists, 'l1', 'X');
    removeHabitList(lists, 'l1');
    setListHabits(lists, 'l1', []);
    setListLog(lists, 'l1', {});
    expect(lists).toEqual(snapshot);
  });
});

describe('[S-20] setListHabits prunes the log of departed habits', () => {
  it('drops log entries for habits that are no longer in the list', () => {
    const lists = [list({
      habits: [{ id: 'h1', name: 'Read' }, { id: 'h2', name: 'Run' }],
      log: { h1: ['k1'], h2: ['k2'] },
    })];
    const next = setListHabits(lists, 'l1', [{ id: 'h1', name: 'Read' }]);
    expect(next[0].log).toEqual({ h1: ['k1'] });
  });

  it('a later habit reusing a dead id cannot inherit its streak', () => {
    const lists = [list({ habits: [{ id: 'h1', name: 'Read' }], log: { h1: ['k1'] } })];
    const removed = setListHabits(lists, 'l1', []);
    const readded = setListHabits(removed, 'l1', [{ id: 'h1', name: 'Something else' }]);
    expect(readded[0].log).toEqual({});
  });

  it('keeps the log of habits that merely moved', () => {
    const lists = [list({
      habits: [{ id: 'h1', name: 'Read' }, { id: 'h2', name: 'Run' }],
      log: { h1: ['k1'], h2: ['k2'] },
    })];
    const reordered = setListHabits(lists, 'l1', [{ id: 'h2', name: 'Run' }, { id: 'h1', name: 'Read' }]);
    expect(reordered[0].log).toEqual({ h1: ['k1'], h2: ['k2'] });
  });
});

describe('[S-21] seeding a migrated list', () => {
  it('upsert leaves an existing id alone, so two windows cannot seed twice', () => {
    const lists = [list({ id: 'seed', name: 'Original' })];
    const out = upsertHabitList(lists, { id: 'seed', name: 'Second', habits: [], log: {} });
    expect(out).toHaveLength(1);
    expect(out[0].name).toBe('Original');
    // ...and never overwrites habits the user has since edited.
    expect(out[0].habits).toHaveLength(1);
  });

  it('inserts when the id is free', () => {
    const out = upsertHabitList([], { id: 'seed', name: 'New', habits: [], log: {} });
    expect(out.map(l => l.id)).toEqual(['seed']);
  });

  it('uniquifies a repeated name so the picker stays readable', () => {
    const lists = [list({ id: 'a', name: 'New list' }), list({ id: 'b', name: 'New list 2' })];
    expect(uniqueHabitListName(lists, 'New list')).toBe('New list 3');
    expect(uniqueHabitListName(lists, 'Other')).toBe('Other');
  });
});

// ── Source scans ───────────────────────────────────────────────────────────
// The widget and its panel are .tsx, which the unit runner does not transform,
// so these pin the wiring rather than the rendered behaviour.

const WIDGET = 'widgets/others/habit-tracker/HabitTracker.tsx';
const CONFIG = 'widgets/others/habit-tracker/HabitTrackerConfig.tsx';

describe('[S-22] a habit is deleted from the widget', () => {
  it('the row menu carries Delete, separated and marked danger', () => {
    const src = readSource(WIDGET).replace(/\r\n/g, '\n');
    const menu = src.slice(src.indexOf('function handleRowCtx'));
    expect(menu).toContain('{ sep: true }');
    expect(menu).toContain('removeHabit(habit)');
    expect(menu).toContain('danger: true');
  });

  it('the Delete key removes the focused row', () => {
    expect(readSource(WIDGET)).toMatch(/e\.key === 'Delete'[\s\S]{0,120}removeHabit\(list\[curr\]\)/);
  });

  it('an open editor for that row is cancelled first', () => {
    // Otherwise the editor's blur would commit a rename onto a habit that no
    // longer exists.
    const src = readSource(WIDGET).replace(/\r\n/g, '\n');
    const fn = src.slice(src.indexOf('function removeHabit'));
    expect(fn).toMatch(/if \(editingId\(\) === habit\.id\) cancelEdit\(\)/);
    // ...and the write goes through persistHabits, which prunes the log [S-20].
    expect(fn).toContain('persistHabits(');
  });
});

describe('[S-23] the config panel holds no habit editor', () => {
  it('has no habits list, draft row or per-habit remove', () => {
    const src = readSource(CONFIG);
    expect(src).not.toContain('habitRows');
    expect(src).not.toContain('DRAFT_HABIT_ID');
    expect(src).not.toContain('habit-cfg-row');
    // The habit-writing seam is gone entirely; only the widget writes habits.
    expect(src).not.toContain('setHabitListHabits');
  });

  it('offers source + display, not a habits tab', () => {
    const src = readSource(CONFIG);
    expect(src).toContain("key: 'source'");
    expect(src).toContain("key: 'display'");
    expect(src).not.toContain("key: 'habits'");
  });

  it('the retired draft-row helpers are gone from habit.ts too', () => {
    // Dead code that still exports reads as a supported second path.
    const src = readSource('widgets/others/habit-tracker/habit.ts');
    expect(src).not.toContain('habitRows');
    expect(src).not.toContain('DRAFT_HABIT_ID');
  });
});

describe('[S-24] deleting a habit list always confirms', () => {
  it('the dialog is not gated on the list being non-empty', () => {
    const src = readSource(CONFIG).replace(/\r\n/g, '\n');
    const fn = src.slice(src.indexOf('async function removeList'), src.indexOf('const deviceLists'));
    expect(fn).toContain('await ask(');
    // The empty case swaps the BODY, it does not skip the prompt.
    expect(fn).toContain('common.deleteEmptyConfirm');
    expect(fn).not.toMatch(/if \(list\.habits\.length > 0\) \{[\s\S]*await ask\(/);
    // The delete must sit after the prompt, never before it.
    expect(fn.indexOf('await ask(')).toBeLessThan(fn.indexOf('deleteHabitList('));
  });
});
