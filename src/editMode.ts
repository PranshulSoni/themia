/**
 * Edit mode, shared by every overlay window in this renderer.
 *
 * Lifted out of App.tsx unchanged so the always-on-top pin overlay ([S-45])
 * reads the SAME state: it stands down while editing and hands its widgets
 * back to the base layer, which it can only do if both windows agree on the
 * flag. Module-level singleton — one window, one edit mode.
 */

import { createSignal } from 'solid-js';
import { onStorageKey, onTransient, postTransient } from './sync.ts';

const EDIT_MODE_KEY = 'themia-edit-mode';

const [editMode, setEditModeLocal] = createSignal(false);

export { editMode };

/**
 * Set edit mode locally, persist, AND broadcast to the other windows.
 *
 * Cross-window sync uses TWO channels because the `storage` event alone is
 * unreliable across separate WebView2 windows (it does not consistently fire
 * between Tauri overlay windows). The BroadcastChannel (`postTransient`) is
 * the same mechanism placing-mode uses and reaches every monitor's window;
 * localStorage is still written for persistence + the `onStorageKey` backup.
 */
export function setEditMode(v: boolean | ((prev: boolean) => boolean)): void {
  const next = typeof v === 'function' ? v(editMode()) : v;
  setEditModeLocal(next);
  localStorage.setItem(EDIT_MODE_KEY, String(next));
  postTransient({ type: 'edit-mode-set', value: next });
}

onStorageKey(EDIT_MODE_KEY, (newValue) => {
  setEditModeLocal(newValue === 'true');
});
// Apply edit-mode changes broadcast by other windows (the reliable path).
// Never unsubscribed: the listener lives as long as the window does.
onTransient('edit-mode-set', (m) => setEditModeLocal(!!m.value));
