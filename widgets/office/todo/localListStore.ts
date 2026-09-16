// The device-wide store of local To Do lists ([S-57]).
//
// This is the one piece of To Do state that is NOT a widget config value. Local
// lists are a POOL the config panel picks from and adds to, so they have to
// outlive the widget that made them and be reachable from every window at once
// (the grid tile, a pop-out, the config window). Widget config keeps only the
// `localListId` pointing into here.
//
// Persistence follows the same contract as the app's other shared stores
// (design, views, file clipboard): schema'd load through `persistedStore` so a
// corrupt blob falls back loudly instead of silently, and writes through
// `sharedStorage` so other windows are notified reliably rather than relying on
// the native `storage` event, which WebView2 does not deliver between overlay
// windows.

import { createStore, reconcile } from 'solid-js/store';
import { onStorageKey, sharedStorage } from '../../../src/sync.ts';
import { logger } from '../../../src/debugLog.ts';
import { loadPersisted, savePersistedBare, type PersistedSchema } from '../../../src/persistedStore.ts';
import type { LocalTask } from './localTasks.ts';
import {
  parseLocalLists, addList, renameList, removeList, setListTasks, upsertList,
  uniqueListName, TODO_LOCAL_LISTS_KEY, type LocalTaskList,
} from './localLists.ts';

// Node-safe storage handle: unit tests import this module without a DOM.
const LS: Pick<Storage, 'getItem' | 'setItem'> =
  typeof localStorage !== 'undefined'
    ? localStorage
    : { getItem: () => null, setItem: () => {} };

const SCHEMA: PersistedSchema<LocalTaskList[]> = {
  version: 1,
  defaults: () => [],
  parse: parseLocalLists,
};

function loadFromStorage(): LocalTaskList[] {
  return loadPersisted(TODO_LOCAL_LISTS_KEY, SCHEMA, LS, {
    onCorrupt: (m) => logger.error('todoLocalLists', m),
  });
}

const [store, setStore] = createStore<{ lists: LocalTaskList[] }>({ lists: loadFromStorage() });

/** Reactive accessor for every list stored on this device. */
export const localLists = (): LocalTaskList[] => store.lists;

function write(next: LocalTaskList[]): void {
  // `reconcile`, not a bare assign. Every helper in localLists.ts is immutable,
  // so `next` holds FRESH objects for the rows it touched — and assigning them
  // straight in makes `<For>` (which keys on item identity) tear down and
  // rebuild every row. That destroys the `<input>` the user is typing in: the
  // name field lost focus and jumped its caret to the end on each keystroke.
  // Keyed by id, reconcile patches the existing objects in place instead, so
  // the rows — and the focused input — survive.
  setStore('lists', reconcile(next, { key: 'id' }));
  savePersistedBare(TODO_LOCAL_LISTS_KEY, next, sharedStorage, {
    onError: (m) => logger.error('todoLocalLists', m),
  });
}

function newListId(): string {
  return `list-${crypto.randomUUID()}`;
}

/** Create a list named `base` (uniquified against the existing names) and return
 *  its id, so the caller can select it straight away. */
export function createLocalList(base: string): string {
  const id = newListId();
  write(addList(store.lists, id, uniqueListName(store.lists, base), base));
  return id;
}

export function renameLocalList(id: string, name: string): void {
  write(renameList(store.lists, id, name));
}

export function deleteLocalList(id: string): void {
  write(removeList(store.lists, id));
}

/** Replace one list's tasks — the single write path for every local task
 *  mutation in the widget. */
export function setLocalListTasks(id: string, tasks: LocalTask[]): void {
  write(setListTasks(store.lists, id, tasks));
}

/** The id a widget switching to "This device" should show: the one it already
 *  had if that list still exists, else the first list, else a freshly created
 *  one. So turning the source on always lands on something usable and never
 *  makes a duplicate empty list. */
export function ensureLocalList(preferredId: string | undefined, baseName: string): string {
  if (preferredId && store.lists.some(l => l.id === preferredId)) return preferredId;
  if (store.lists.length > 0) return store.lists[0].id;
  return createLocalList(baseName);
}

/** Seed a list at a KNOWN id, leaving it alone if it already exists ([S-59]).
 *  Used by the legacy `localTasks` migration, which derives the id from the
 *  widget id so the grid tile and a pop-out of the same widget converge on one
 *  list instead of seeding two. */
export function seedLocalList(id: string, baseName: string, tasks: LocalTask[]): void {
  if (store.lists.some(l => l.id === id)) return;
  write(upsertList(store.lists, { id, name: uniqueListName(store.lists, baseName), tasks }));
}

// Another window changed the pool: reload and reconcile so existing rows keep
// their identity (and the DOM their focus) instead of being recreated.
onStorageKey(TODO_LOCAL_LISTS_KEY, () => {
  const loaded = loadFromStorage();
  setStore('lists', reconcile(loaded, { key: 'id' }));
});
