import { describe, it, expect } from 'vitest';
import {
  parseLocalLists,
  findList,
  listTasks,
  addList,
  renameList,
  removeList,
  setListTasks,
  upsertList,
  uniqueListName,
  type LocalTaskList,
} from './localLists.ts';
import type { LocalTask } from './localTasks.ts';

// "This device" owns many named lists ([S-57]); these pure helpers own their
// shape, the trust boundary at the persisted value, and every mutation the
// config panel and the widget make. Tags mirror SPEC.md.

const task = (id: string, title = id, done = false): LocalTask => ({ id, title, done });
const list = (id: string, name: string, tasks: LocalTask[] = []): LocalTaskList => ({ id, name, tasks });

describe('parseLocalLists [S-57]', () => {
  it('returns [] for a non-array (absent / corrupt store)', () => {
    expect(parseLocalLists(undefined)).toEqual([]);
    expect(parseLocalLists(null)).toEqual([]);
    expect(parseLocalLists('nope')).toEqual([]);
    expect(parseLocalLists({ lists: [] })).toEqual([]);
  });

  it('round-trips well-formed lists, validating their tasks [S-42]', () => {
    expect(parseLocalLists([
      { id: 'l1', name: 'Work', tasks: [{ id: 'a', title: 'Ship it', done: true }] },
      { id: 'l2', name: 'Home', tasks: [] },
    ])).toEqual([
      { id: 'l1', name: 'Work', tasks: [{ id: 'a', title: 'Ship it', done: true }] },
      { id: 'l2', name: 'Home', tasks: [] },
    ]);
  });

  it('drops an entry without a usable id or name, keeping its siblings', () => {
    expect(parseLocalLists([
      { id: 'l1', name: 'Keep' },
      { name: 'no id' },
      { id: '', name: 'empty id' },
      { id: 7, name: 'numeric id' },
      { id: 'l2' },
      null,
      'nope',
      { id: 'l3', name: 'Keep too' },
    ]).map(l => l.id)).toEqual(['l1', 'l3']);
  });

  it('a corrupt list of tasks reads as an empty list, not a dropped list', () => {
    expect(parseLocalLists([{ id: 'l1', name: 'Work', tasks: 'garbage' }]))
      .toEqual([{ id: 'l1', name: 'Work', tasks: [] }]);
  });

  it('S-57_ drops a REPEATED id, keeping the first: a lookup by id must be unambiguous', () => {
    const parsed = parseLocalLists([
      { id: 'dup', name: 'Original', tasks: [{ id: 'a', title: 'Mine', done: false }] },
      { id: 'dup', name: 'Impostor', tasks: [{ id: 'b', title: 'Theirs', done: false }] },
    ]);
    expect(parsed).toHaveLength(1);
    expect(parsed[0].name).toBe('Original');
    // …so a widget pointing at `dup` can only ever resolve to one list.
    expect(listTasks(parsed, 'dup').map(t => t.id)).toEqual(['a']);
  });
});

describe('findList / listTasks [S-57]', () => {
  const lists = [list('l1', 'Work', [task('a')]), list('l2', 'Home')];

  it('finds by id', () => {
    expect(findList(lists, 'l2')?.name).toBe('Home');
    expect(listTasks(lists, 'l1').map(t => t.id)).toEqual(['a']);
  });

  it('S-58_ an unknown or absent id resolves to NOTHING, never another list', () => {
    // The widget shows "pick or create a list" instead of silently adopting
    // someone else's tasks.
    expect(findList(lists, 'gone')).toBeUndefined();
    expect(findList(lists, '')).toBeUndefined();
    expect(findList(lists, undefined)).toBeUndefined();
    expect(listTasks(lists, 'gone')).toEqual([]);
  });
});

describe('addList [S-58]', () => {
  it('appends an empty list and leaves the input untouched', () => {
    const before = [list('l1', 'Work')];
    const after = addList(before, 'l2', 'Home', 'Untitled');
    expect(after).toEqual([list('l1', 'Work'), list('l2', 'Home')]);
    expect(before).toHaveLength(1);
  });

  it('trims the name, and falls back when it is blank so no list is unnamed', () => {
    expect(addList([], 'l1', '  Groceries  ', 'Untitled')[0].name).toBe('Groceries');
    expect(addList([], 'l1', '   ', 'Untitled')[0].name).toBe('Untitled');
  });
});

describe('renameList [S-58]', () => {
  it('renames by id, immutably', () => {
    const before = [list('l1', 'Work'), list('l2', 'Home')];
    expect(renameList(before, 'l2', 'House').map(l => l.name)).toEqual(['Work', 'House']);
    expect(before[1].name).toBe('Home');
  });

  it('KEEPS a blank name: the field writes through on every keystroke, so it has to be clearable', () => {
    expect(renameList([list('l1', 'Work')], 'l1', '')[0].name).toBe('');
  });

  it('unknown id changes nothing', () => {
    expect(renameList([list('l1', 'Work')], 'nope', 'X')).toEqual([list('l1', 'Work')]);
  });
});

describe('removeList [S-58]', () => {
  it('removes by id and takes its tasks with it; unknown id is a no-op', () => {
    const before = [list('l1', 'Work', [task('a')]), list('l2', 'Home')];
    expect(removeList(before, 'l1').map(l => l.id)).toEqual(['l2']);
    expect(removeList(before, 'nope')).toEqual(before);
    expect(before).toHaveLength(2);
  });
});

describe('setListTasks [S-57]', () => {
  it('replaces one list\'s tasks and leaves its siblings alone', () => {
    const before = [list('l1', 'Work', [task('a')]), list('l2', 'Home', [task('b')])];
    const after = setListTasks(before, 'l1', [task('a'), task('c')]);
    expect(after[0].tasks.map(t => t.id)).toEqual(['a', 'c']);
    expect(after[1]).toBe(before[1]);
    expect(before[0].tasks).toHaveLength(1);
  });

  it('a write to a list that is gone is DROPPED, not a resurrection', () => {
    const before = [list('l1', 'Work')];
    expect(setListTasks(before, 'deleted', [task('a')])).toEqual(before);
  });
});

describe('upsertList [S-59]', () => {
  it('inserts a list whose id is free', () => {
    expect(upsertList([], list('m', 'Migrated', [task('a')])).map(l => l.id)).toEqual(['m']);
  });

  it('S-59_ leaves an existing id ALONE, so a second seeding cannot duplicate or clobber it', () => {
    // Two windows of the same widget migrate the same legacy bag at once; both
    // derive the same id, so the second one must be a no-op — and must never
    // overwrite tasks the user has edited since.
    const existing = [list('m', 'Migrated', [task('edited', 'Edited since')])];
    const after = upsertList(existing, list('m', 'Migrated', [task('stale', 'Stale copy')]));
    expect(after).toHaveLength(1);
    expect(after[0].tasks.map(t => t.id)).toEqual(['edited']);
  });
});

describe('uniqueListName [S-58]', () => {
  it('uses the base name when it is free', () => {
    expect(uniqueListName([], 'New list')).toBe('New list');
    expect(uniqueListName([list('l1', 'Work')], 'New list')).toBe('New list');
  });

  it('numbers the collisions so a picker of "New list" three times over cannot happen', () => {
    const lists = [list('l1', 'New list')];
    expect(uniqueListName(lists, 'New list')).toBe('New list 2');
    expect(uniqueListName([...lists, list('l2', 'New list 2')], 'New list')).toBe('New list 3');
    // A hole in the numbering is filled rather than skipped past.
    expect(uniqueListName([...lists, list('l2', 'New list 3')], 'New list')).toBe('New list 2');
  });
});
