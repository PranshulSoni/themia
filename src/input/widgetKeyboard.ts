// Thin wrapper around the Rust WH_KEYBOARD_LL keyboard hook.
//
// Widgets live in WS_EX_NOACTIVATE windows that never receive Win32 keyboard
// focus, so normal DOM keydown events never fire. The Rust side installs a
// global low-level hook, intercepts a whitelisted set of keys when the user
// has clicked a widget (and the cursor is still over the desktop / one of
// our widgets), and emits them back as a "folder-key" Tauri event to the
// active widget's window.
//
// This helper lets any widget subscribe to those synthetic keys without
// re-implementing the plumbing. Call `registerWidgetKeyboard(handler)` from
// `onMount`, call the returned `activate()` on mousedown inside the widget,
// and call the returned dispose in `onCleanup`.

import { getCurrentWindow } from '@tauri-apps/api/window';
import { invoke } from '../demo/invoke.ts';

export interface WidgetKeyEvent {
  key: string;         // Normalized: "ArrowUp", "Enter", "Backspace", "Delete", …
  ctrlKey: boolean;
  metaKey: boolean;
  shiftKey: boolean;
  preventDefault: () => void;
}

// Keys the Rust hook emits WITHOUT implicit Ctrl (anything else, e.g. "c",
// "v", "x", "a", is a Ctrl+<letter> shortcut).
const NON_CTRL_KEYS: ReadonlySet<string> = new Set([
  'delete', 'escape', 'f2', 'tab',
  'up', 'down', 'left', 'right',
  'enter', 'backspace', 'space',
  'home', 'end', 'pageup', 'pagedown',
]);

const KEY_ALIAS: Record<string, string> = {
  up: 'ArrowUp', down: 'ArrowDown', left: 'ArrowLeft', right: 'ArrowRight',
  enter: 'Enter', backspace: 'Backspace', space: ' ',
  home: 'Home', end: 'End', pageup: 'PageUp', pagedown: 'PageDown',
  delete: 'Delete', escape: 'Escape', f2: 'F2', tab: 'Tab',
};

// Only one handler per window is active at a time (the same window may host
// many widgets; the last-clicked one owns keyboard). This matches the Rust
// hook, which routes to a single window label.
let _active: ((e: WidgetKeyEvent) => void) | null = null;
let _listenerWired = false;

function wireListener(): void {
  if (_listenerWired) return;
  _listenerWired = true;
  getCurrentWindow().listen<string>('folder-key', (ev) => {
    let raw = ev.payload;
    let shift = false;
    if (raw.startsWith('shift+')) { shift = true; raw = raw.slice(6); }
    const isCtrlLetter = !NON_CTRL_KEYS.has(raw);
    _active?.({
      key: KEY_ALIAS[raw] ?? raw,
      ctrlKey: isCtrlLetter,
      metaKey: false,
      shiftKey: shift,
      preventDefault: () => {},
    });
  }).catch(() => {});
}

export interface WidgetKeyboardHandle {
  /** Call in a mousedown handler to claim keyboard focus for this widget. */
  activate: () => void;
  /** Clear the active handler. Call from `onCleanup`. */
  dispose: () => void;
}

/**
 * Subscribe a widget to the shared Rust keyboard hook. The handler receives
 * synthetic key events for the whitelisted keys (arrows, Tab, Enter, Backspace,
 * Home/End, PageUp/PageDown, Delete, Escape, F2, and Ctrl+C/V/X/A).
 */
export function registerWidgetKeyboard(
  handler: (e: WidgetKeyEvent) => void,
): WidgetKeyboardHandle {
  wireListener();
  return {
    activate() {
      _active = handler;
      invoke('activate_widget_keyboard', { label: getCurrentWindow().label }).catch(() => {});
    },
    dispose() {
      if (_active === handler) _active = null;
    },
  };
}
