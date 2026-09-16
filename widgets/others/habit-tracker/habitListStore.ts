// The device-wide store of habit lists ([S-18]).
//
// This is the one piece of Habit Tracker state that is NOT a widget config
// value. Habit lists are a POOL the config panel picks from and adds to, so
// they have to outlive the widget that made them and be reachable from every
// window at once (the grid tile, a pop-out, the config window). Widget config
// keeps only the `localListId` pointing into here.
//
// Persistence follows the same contract as the app's other shared stores
// (design, views, To Do's local lists): schema'd load through `persistedStore`
// so a corrupt blob falls back loudly instead of silently, and writes through
// `sharedStorage` so other windows are notified reliably rather than relying on
// the native `storage` event, which WebView2 does not deliver between overlay
// windows.

import { createStore, reconcile } from 'solid-js/store';
import { onStorageKey, sharedStorage } from '../../../src/sync.ts';
import { logger } from '../../../src/debugLog.ts';
import { loadPersisted, savePersistedBare, type PersistedSchema } from '../../../src/persistedStore.ts';
import type { Habit } from './habit.ts';
import {
  parseHabitLists, addHabitList, renameHabitList, removeHabitList,
  setListHabits, setListLog, upsertHabitList, uniqueHabitListName,
  HABIT_LISTS_KEY, type HabitList,
} from './habitLists.ts';

// Node-safe storage handle: unit tests import this module without a DOM.
const LS: Pick<Storage, 'getItem' | 'setItem'> =
  typeof localStorage !== 'undefined'
    ? localStorage
    : { getItem: () => null, setItem: () => {} };

const SCHEMA: PersistedSchema<HabitList[]> = {
  version: 1,
  defaults: () => [],
  parse: parseHabitLists,
};

function loadFromStorage(): HabitList[] {
  return loadPersisted(HABIT_LISTS_KEY, SCHEMA, LS, {
    onCorrupt: (m) => logger.error('habitLists', m),
  });
}

const [store, setStore] = createStore<{ lists: HabitList[] }>({ lists: loadFromStorage() });

/** Reactive accessor for every habit list stored on this device. */
export const habitLists = (): HabitList[] => store.lists;

function write(next: HabitList[]): void {
  // `reconcile`, not a bare assign — see localListStore.ts for the full reason.
  // The helpers are immutable, so `next` holds fresh objects; assigning them
  // straight in makes `<For>` rebuild every row and destroys the `<input>` the
  // user is typing in. Keyed by id, reconcile patches in place instead.
  setStore('lists', reconcile(next, { key: 'id' }));
  savePersistedBare(HABIT_LISTS_KEY, next, sharedStorage, {
    onError: (m) => logger.error('habitLists', m),
  });
}

function newListId(): string {
  return `hlist-${crypto.randomUUID()}`;
}

/** Create a list named `base` (uniquified against the existing names) and return
 *  its id, so the caller can select it straight away. */
export function createHabitList(base: string): string {
  const id = newListId();
  write(addHabitList(store.lists, id, uniqueHabitListName(store.lists, base), base));
  return id;
}

export function renameHabitListById(id: string, name: string): void {
  write(renameHabitList(store.lists, id, name));
}

export function deleteHabitList(id: string): void {
  write(removeHabitList(store.lists, id));
}

/** Replace one list's habits — the single write path for every habit mutation
 *  (add, rename, remove, reorder). Prunes the log of habits that left. */
export function setHabitListHabits(id: string, habits: Habit[]): void {
  write(setListHabits(store.lists, id, habits));
}

/** Replace one list's completion log — the single write path for check-offs. */
export function setHabitListLog(id: string, log: Record<string, string[]>): void {
  write(setListLog(store.lists, id, log));
}

/** The id a widget should show: the one it already had if that list still
 *  exists, else the first list, else a freshly created one. So a widget always
 *  lands on something usable and never makes a duplicate empty list. */
export function ensureHabitList(preferredId: string | undefined, baseName: string): string {
  if (preferredId && store.lists.some(l => l.id === preferredId)) return preferredId;
  if (store.lists.length > 0) return store.lists[0].id;
  return createHabitList(baseName);
}

/** Seed a list at a KNOWN id, leaving it alone if it already exists ([S-21]).
 *  Used by the legacy in-config migration, which derives the id from the widget
 *  id so the grid tile and a pop-out of the same widget converge on one list
 *  instead of seeding two. */
export function seedHabitList(
  id: string,
  baseName: string,
  habits: Habit[],
  log: Record<string, string[]>,
): void {
  if (store.lists.some(l => l.id === id)) return;
  write(upsertHabitList(store.lists, {
    id,
    name: uniqueHabitListName(store.lists, baseName),
    habits,
    log,
  }));
}

// Another window changed the pool: reload and reconcile so existing rows keep
// their identity (and the DOM their focus) instead of being recreated.
onStorageKey(HABIT_LISTS_KEY, () => {
  const loaded = loadFromStorage();
  setStore('lists', reconcile(loaded, { key: 'id' }));
});
