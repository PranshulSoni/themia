// Pure helpers for the Habit Tracker's DEVICE-LEVEL habit lists ([S-18]).
//
// The widget used to hold exactly one anonymous habit array — plus its
// completion log — per placed widget, inside that widget's own config bag. A
// named LIST is the unit now: "On this device" is a source that owns many
// lists, and each widget picks one to show. That is why the lists live in one
// device-wide store instead of a widget's config: a pool you select from has to
// outlive (and be reachable by) any single widget, so two widgets can track the
// same habits and a pop-out agrees with its tile.
//
// A list owns its `log` as well as its `habits`, because the log is keyed by
// habit id — splitting them would let a list's streaks outlive the habits they
// belong to, or bleed into another list that happened to reuse an id.
//
// Nothing here touches Tauri, storage or Solid: this module owns the SHAPE and
// the rules, `habitListStore.ts` owns persistence and reactivity. Mirrors the
// To Do widget's `localLists.ts` deliberately — same problem, same shape.

import type { Habit } from './habit.ts';

export interface HabitList {
  id: string;
  name: string;
  habits: Habit[];
  /** Completion log: habit id -> the period keys it was checked off in. */
  log: Record<string, string[]>;
}

/** The persisted key the pool lives under. It sits in this side-effect-free
 *  module (rather than next to the store that writes it) so import/export can
 *  name the key without dragging Solid + the cross-window sync hub into a node
 *  unit test. */
export const HABIT_LISTS_KEY = 'habit-lists';

/** Narrow an untrusted value to a validated `Habit[]`. Config and localStorage
 *  are both trust boundaries: a hand-edited layout file can hold anything, and
 *  a habit without a string id/name would render an unclickable row whose log
 *  entries could never be addressed. Entries are dropped, never repaired.
 *  Never throws. */
export function parseHabits(raw: unknown): Habit[] {
  if (!Array.isArray(raw)) return [];
  const out: Habit[] = [];
  const seen = new Set<string>();
  for (const entry of raw) {
    if (entry === null || typeof entry !== 'object') continue;
    const o = entry as Record<string, unknown>;
    if (typeof o.id !== 'string' || !o.id) continue;
    if (typeof o.name !== 'string') continue;
    // A repeated id would make "which habit is this row" ambiguous and let a
    // check-off land on the wrong streak. Keep the first.
    if (seen.has(o.id)) continue;
    seen.add(o.id);
    out.push({ id: o.id, name: o.name });
  }
  return out;
}

/** Narrow an untrusted value to a validated completion log. A non-object yields
 *  `{}`; a habit whose value is not an array of strings is dropped rather than
 *  trusted, since `streak`/`recentHistory` build a `Set` from it. Duplicate
 *  period keys collapse — the log is a set of buckets, and a doubled key would
 *  survive a round trip as noise. Never throws. */
export function parseLog(raw: unknown): Record<string, string[]> {
  if (raw === null || typeof raw !== 'object' || Array.isArray(raw)) return {};
  const out: Record<string, string[]> = {};
  for (const [habitId, value] of Object.entries(raw as Record<string, unknown>)) {
    if (!Array.isArray(value)) continue;
    const keys = [...new Set(value.filter((k): k is string => typeof k === 'string'))];
    out[habitId] = keys;
  }
  return out;
}

/** Narrow an untrusted persisted value to a validated `HabitList[]`. A non-array
 *  (missing or corrupt) yields `[]`; entries without a non-empty string `id` or
 *  a string `name` are dropped rather than trusted.
 *
 *  A REPEATED id is dropped too, keeping the first: every lookup here is by id,
 *  so a duplicate would make "which list is this widget showing" ambiguous and
 *  let a write land on the wrong one. Never throws. */
export function parseHabitLists(raw: unknown): HabitList[] {
  if (!Array.isArray(raw)) return [];
  const out: HabitList[] = [];
  const seen = new Set<string>();
  for (const entry of raw) {
    if (entry === null || typeof entry !== 'object') continue;
    const o = entry as Record<string, unknown>;
    if (typeof o.id !== 'string' || !o.id) continue;
    if (typeof o.name !== 'string') continue;
    if (seen.has(o.id)) continue;
    seen.add(o.id);
    out.push({ id: o.id, name: o.name, habits: parseHabits(o.habits), log: parseLog(o.log) });
  }
  return out;
}

/** The list with `id`, or undefined. A widget whose selected list is gone shows
 *  the "pick a list" state rather than silently adopting another one — someone
 *  else's habits appearing in a widget is worse than an empty prompt. */
export function findHabitList(lists: readonly HabitList[], id: string | undefined): HabitList | undefined {
  if (!id) return undefined;
  return lists.find(l => l.id === id);
}

/** The habits of the list with `id`; unknown id -> empty. */
export function listHabits(lists: readonly HabitList[], id: string | undefined): Habit[] {
  return findHabitList(lists, id)?.habits ?? [];
}

/** The completion log of the list with `id`; unknown id -> empty. */
export function listLog(lists: readonly HabitList[], id: string | undefined): Record<string, string[]> {
  return findHabitList(lists, id)?.log ?? {};
}

/** Append a new empty list. The name is trimmed; a blank one falls back to
 *  `fallbackName` so a list can never end up unnamed and unclickable.
 *  Immutable. */
export function addHabitList(
  lists: readonly HabitList[],
  id: string,
  name: string,
  fallbackName: string,
): HabitList[] {
  return [...lists, { id, name: name.trim() || fallbackName, habits: [], log: {} }];
}

/** Rename the list with `id`. A BLANK name is kept while it is being typed —
 *  the name input writes through on every keystroke, and refusing the empty
 *  string would make the field impossible to clear before retyping. The picker
 *  falls back to a placeholder for the empty case. Unknown id -> equal list.
 *  Immutable. */
export function renameHabitList(lists: readonly HabitList[], id: string, name: string): HabitList[] {
  return lists.map(l => (l.id === id ? { ...l, name } : l));
}

/** Remove the list with `id`, and its habits + their streaks with it. Unknown
 *  id -> equal list. Immutable. */
export function removeHabitList(lists: readonly HabitList[], id: string): HabitList[] {
  return lists.filter(l => l.id !== id);
}

/** Replace the habits of the list with `id`, dropping log entries for habits
 *  that are no longer in it. That pruning is what keeps a deleted habit's
 *  streak from being resurrected by a later habit reusing its id, and it is
 *  done HERE rather than at the call sites so no mutation path can forget it.
 *  Unknown id -> equal list, so a write racing a list deletion is dropped
 *  rather than resurrecting the list. Immutable. */
export function setListHabits(
  lists: readonly HabitList[],
  id: string,
  habits: Habit[],
): HabitList[] {
  return lists.map(l => {
    if (l.id !== id) return l;
    const live = new Set(habits.map(h => h.id));
    const log: Record<string, string[]> = {};
    for (const [habitId, keys] of Object.entries(l.log)) {
      if (live.has(habitId)) log[habitId] = keys;
    }
    return { ...l, habits, log };
  });
}

/** Replace the completion log of the list with `id`. Unknown id -> equal list.
 *  Immutable. */
export function setListLog(
  lists: readonly HabitList[],
  id: string,
  log: Record<string, string[]>,
): HabitList[] {
  return lists.map(l => (l.id === id ? { ...l, log } : l));
}

/** Insert `list` only if its id is not taken. The legacy migration and both
 *  windows showing the same widget run it with the SAME derived id, so an
 *  upsert-if-absent is what keeps them from seeding two copies — and never
 *  overwrites habits the user has since edited. Immutable. [S-21] */
export function upsertHabitList(lists: readonly HabitList[], list: HabitList): HabitList[] {
  return lists.some(l => l.id === list.id) ? [...lists] : [...lists, list];
}

/** `base`, or `base 2` / `base 3` … if that name is taken. Two lists may legally
 *  share a name (they are addressed by id), but "New list" three times over is
 *  a picker nobody can read. */
export function uniqueHabitListName(lists: readonly HabitList[], base: string): string {
  const taken = new Set(lists.map(l => l.name));
  if (!taken.has(base)) return base;
  for (let n = 2; ; n++) {
    const candidate = `${base} ${n}`;
    if (!taken.has(candidate)) return candidate;
  }
}
