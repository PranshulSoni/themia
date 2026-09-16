// Pure helpers for the To Do widget's DEVICE-LEVEL local lists ([S-57]).
//
// Local mode used to hold exactly one anonymous task array per placed widget.
// A named LIST is the unit now: "This device" is a source that owns many lists,
// the same way an account owns many task lists, and each widget picks one to
// show. That is why the lists live in one device-wide store instead of a widget's
// own config bag — a pool you select from has to outlive (and be reachable by)
// any single widget.
//
// Nothing here touches Tauri, storage or Solid: this module owns the SHAPE and
// the rules, `localListStore.ts` owns persistence and reactivity.

import { parseLocalTasks, type LocalTask } from './localTasks.ts';

export interface LocalTaskList {
  id: string;
  name: string;
  tasks: LocalTask[];
}

/** The persisted key the pool lives under. It sits in this side-effect-free
 *  module (rather than next to the store that writes it) so import/export can
 *  name the key without dragging Solid + the cross-window sync hub into a node
 *  unit test. Read raw by `importExport`, so the on-disk shape stays a bare
 *  `LocalTaskList[]`. */
export const TODO_LOCAL_LISTS_KEY = 'todo-local-lists';

/** Narrow an untrusted persisted value to a validated `LocalTaskList[]`. A
 *  non-array (missing or corrupt) yields `[]`; entries without a non-empty
 *  string `id` or a string `name` are dropped rather than trusted, and their
 *  tasks go through `parseLocalTasks` ([S-42]).
 *
 *  A REPEATED id is dropped too, keeping the first: every lookup here is by id,
 *  so a duplicate would make "which list is this widget showing" ambiguous and
 *  let a write land on the wrong one. Never throws. */
export function parseLocalLists(raw: unknown): LocalTaskList[] {
  if (!Array.isArray(raw)) return [];
  const out: LocalTaskList[] = [];
  const seen = new Set<string>();
  for (const entry of raw) {
    if (entry === null || typeof entry !== 'object') continue;
    const o = entry as Record<string, unknown>;
    if (typeof o.id !== 'string' || !o.id) continue;
    if (typeof o.name !== 'string') continue;
    if (seen.has(o.id)) continue;
    seen.add(o.id);
    out.push({ id: o.id, name: o.name, tasks: parseLocalTasks(o.tasks) });
  }
  return out;
}

/** The list with `id`, or undefined. A widget whose selected list is gone shows
 *  the "pick a list" state rather than silently adopting another one — someone
 *  else's tasks appearing in a widget is worse than an empty prompt. */
export function findList(lists: readonly LocalTaskList[], id: string | undefined): LocalTaskList | undefined {
  if (!id) return undefined;
  return lists.find(l => l.id === id);
}

/** The tasks of the list with `id`; unknown id → empty. */
export function listTasks(lists: readonly LocalTaskList[], id: string | undefined): LocalTask[] {
  return findList(lists, id)?.tasks ?? [];
}

/** Append a new empty list. The name is trimmed; a blank one falls back to
 *  `fallbackName` so a list can never end up unnamed and unclickable.
 *  Immutable. */
export function addList(
  lists: readonly LocalTaskList[],
  id: string,
  name: string,
  fallbackName: string,
): LocalTaskList[] {
  return [...lists, { id, name: name.trim() || fallbackName, tasks: [] }];
}

/** Rename the list with `id`. Unlike a task title ([S-50]) a BLANK name is
 *  kept while it is being typed — the name input writes through on every
 *  keystroke, and refusing the empty string would make the field impossible to
 *  clear before retyping. The picker falls back to a placeholder for the empty
 *  case. Unknown id → equal list. Immutable. */
export function renameList(lists: readonly LocalTaskList[], id: string, name: string): LocalTaskList[] {
  return lists.map(l => (l.id === id ? { ...l, name } : l));
}

/** Remove the list with `id`, and its tasks with it. Unknown id → equal list.
 *  Immutable. */
export function removeList(lists: readonly LocalTaskList[], id: string): LocalTaskList[] {
  return lists.filter(l => l.id !== id);
}

/** Replace the tasks of the list with `id`. Every local task mutation (add,
 *  toggle, rename, delete, reorder) goes through here, so `localTasks.ts` keeps
 *  owning the task-array rules and this only owns which list they land in.
 *  Unknown id → equal list, so a write racing a list deletion is dropped rather
 *  than resurrecting the list. Immutable. */
export function setListTasks(
  lists: readonly LocalTaskList[],
  id: string,
  tasks: LocalTask[],
): LocalTaskList[] {
  return lists.map(l => (l.id === id ? { ...l, tasks } : l));
}

/** Insert `list` only if its id is not taken ([S-59]). The legacy migration and
 *  both windows showing the same widget run it with the SAME derived id, so an
 *  upsert-if-absent is what keeps them from seeding two copies — and never
 *  overwrites tasks the user has since edited. Immutable. */
export function upsertList(lists: readonly LocalTaskList[], list: LocalTaskList): LocalTaskList[] {
  return lists.some(l => l.id === list.id) ? [...lists] : [...lists, list];
}

/** `base`, or `base 2` / `base 3` … if that name is taken. Two lists may legally
 *  share a name (they are addressed by id), but "New list" three times over is
 *  a picker nobody can read. */
export function uniqueListName(lists: readonly LocalTaskList[], base: string): string {
  const taken = new Set(lists.map(l => l.name));
  if (!taken.has(base)) return base;
  for (let n = 2; ; n++) {
    const candidate = `${base} ${n}`;
    if (!taken.has(candidate)) return candidate;
  }
}
