// ---------------------------------------------------------------------------
// Client for the local notes store (Rust `notes_*` commands).
//
// The backend owns the notes; this module is the app's single door to it, and
// the reason there is exactly one: a note can be open in several places at once
// (two widgets on the same note, the grid widget plus its pop-out window, the
// config panel's picker), and every one of them has to see the same list.
//
// Shape mirrors src/accountStore.ts — a store for the list, a revision signal
// to force a reload, and a cross-window broadcast so the OTHER windows reload
// too. Mutations go through the helpers here rather than `invoke` directly, so
// the broadcast can never be forgotten at a call site.
// ---------------------------------------------------------------------------

import { createSignal } from 'solid-js';
import { createStore, reconcile } from 'solid-js/store';
import { invoke } from '../../../src/demo/invoke.ts';
import { postTransient } from '../../../src/sync.ts';
import { showToast } from '../../../src/toastStore.ts';
import { t } from '../../../src/i18n.ts';
import type { NoteMeta } from '../../../src/ipc.ts';

export type { NoteMeta };

const [notesStore, setNotesStore] = createStore<{ notes: NoteMeta[]; loaded: boolean }>({
  notes: [],
  loaded: false,
});
export { notesStore };

// Bumped whenever the list changes. Components read it inside an effect to
// re-run `loadNotes` without having to know who changed what.
const [notesRevision, setNotesRevision] = createSignal(0);
export { notesRevision };

/** Report a failed store operation once, in the user's language, and log the
 *  real error for the log file. Never swallowed: a note that silently failed to
 *  save is the worst outcome this widget has. */
function fail(op: string, e: unknown): void {
  console.error(`notes: ${op} failed`, e);
  showToast(t('notes.storeError'), 'error');
}

/** Refresh the list from disk. Safe to call repeatedly; the backend read is a
 *  single small JSON file. */
export async function loadNotes(): Promise<void> {
  try {
    const notes = await invoke('notes_list');
    // `reconcile`, not a bare assign: every load returns freshly-deserialized
    // objects, and assigning them makes `<For>` (which keys on item identity)
    // rebuild every row — destroying the title `<input>` the user may be typing
    // in. Keyed by id, reconcile patches the existing rows in place.
    setNotesStore('notes', reconcile(notes, { key: 'id' }));
    setNotesStore('loaded', true);
  } catch (e) {
    // Mark loaded anyway: an empty-but-loaded list lets the picker offer
    // "New note" instead of spinning forever on a store that will not read.
    setNotesStore('loaded', true);
    fail('list', e);
  }
}

/** Tell this window and every other one that the LIST changed (a note was
 *  created, renamed or deleted). */
function broadcast(): void {
  setNotesRevision(n => n + 1);
  postTransient({ type: 'notes-changed' });
}

/** Tell only the OTHER windows that a note's body changed.
 *
 *  Deliberately does not bump the local revision: the window that wrote the
 *  text already has it, and bumping would make its own editor re-read from disk
 *  on every debounced save — turning each keystroke burst into a round trip
 *  that races the next one. Echo suppression is unnecessary because the echo is
 *  never sent. */
function broadcastBody(id: string): void {
  postTransient({ type: 'notes-changed', id });
}

/** Apply a `notes-changed` that arrived from another window. Separate from
 *  `broadcast` so re-broadcasting can't loop between windows. */
export function applyRemoteChange(): void {
  setNotesRevision(n => n + 1);
  void loadNotes();
}

export async function readNote(id: string): Promise<string | null> {
  try {
    return await invoke('notes_read', { id });
  } catch (e) {
    // A missing note is an expected outcome (deleted in another window), not a
    // store failure, so it must not raise a toast — the caller shows the
    // "note is gone" empty state instead.
    if (String(e).includes('Note not found')) return null;
    fail('read', e);
    return null;
  }
}

/** Persist a body. Returns the new `updated_at` stamp, or null on failure. */
export async function writeNote(id: string, content: string): Promise<number | null> {
  try {
    const stamp = await invoke('notes_write', { id, content });
    broadcastBody(id);
    return stamp;
  } catch (e) {
    if (String(e).includes('Note not found')) return null;
    fail('write', e);
    return null;
  }
}

export async function createNote(title: string, content = ''): Promise<NoteMeta | null> {
  try {
    const meta = await invoke('notes_create', { title, content });
    await loadNotes();
    broadcast();
    return meta;
  } catch (e) {
    fail('create', e);
    return null;
  }
}

export async function renameNote(id: string, title: string): Promise<boolean> {
  try {
    await invoke('notes_rename', { id, title });
    await loadNotes();
    broadcast();
    return true;
  } catch (e) {
    fail('rename', e);
    return false;
  }
}

export async function deleteNote(id: string): Promise<boolean> {
  try {
    await invoke('notes_delete', { id });
    await loadNotes();
    broadcast();
    return true;
  } catch (e) {
    fail('delete', e);
    return false;
  }
}
