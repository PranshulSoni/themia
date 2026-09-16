// A store feeding a <For> of editable rows must RECONCILE — to-do [S-57],
// habit-tracker [S-18], notes [S-38].
//
// The bug this pins: every list helper is immutable, so a write hands the store
// freshly-built objects. Assigning them straight in changes each row's identity,
// and `<For>` (which keys on identity) tears down and rebuilds every row —
// destroying the `<input>` the user is typing in. The list-name field lost
// focus and threw its caret to the end on EVERY keystroke.
//
// `reconcile(next, { key: 'id' })` patches the existing row objects in place
// instead, so the DOM rows — and the focused input — survive the write.
//
// A source scan: vitest has no DOM for these stores, and the rule is a fact
// about the write path.

import { describe, it, expect } from 'vitest';
import { readSource } from '../../tests/cssRules.ts';

/** Stores that back a rendered list with an editable name field. */
const STORES: { file: string; label: string }[] = [
  { file: 'widgets/office/todo/localListStore.ts', label: 'to-do device lists' },
  { file: 'widgets/others/habit-tracker/habitListStore.ts', label: 'habit lists' },
  { file: 'widgets/others/notes/notesStore.ts', label: 'notes' },
];

describe('list stores reconcile instead of replacing rows', () => {
  for (const { file, label } of STORES) {
    it(`${label}: every array write goes through reconcile`, () => {
      const src = readSource(file).replace(/\r\n/g, '\n');
      expect(src).toContain('reconcile');

      // Any `setX('<arrayProp>', value)` / `setX({ <arrayProp>: value })` that
      // is NOT wrapped in reconcile would rebuild the rows.
      const bareArrayWrites = [...src.matchAll(/set\w*\(\s*'(lists|notes)'\s*,\s*([^)]*)\)/g)]
        .filter(m => !m[2].includes('reconcile'))
        .map(m => m[0]);
      expect(bareArrayWrites, 'these writes replace row identity').toEqual([]);

      // The object form bypasses the check above entirely, so it is banned for
      // the array itself.
      expect(src).not.toMatch(/set\w*Store\(\s*\{[^}]*\b(lists|notes)\s*:/);
    });

    it(`${label}: reconcile is keyed by id, not by index`, () => {
      // The default key happens to be 'id', but an implicit default is a
      // silent dependency — rows are matched by id or they are not matched.
      const src = readSource(file);
      for (const call of src.match(/reconcile\([^)]*\)/g) ?? []) {
        expect(call, `${call} must name its key`).toContain("key: 'id'");
      }
    });
  }
});
