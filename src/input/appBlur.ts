/**
 * App-blur helper.
 *
 * Runs `handler` whenever the Themia app loses focus, i.e. the user clicked another
 * application or the desktop. Used by widgets with a selection / focus highlight
 * (folder, shortcut, contacts, email, todo, …) so a stale highlight never stays
 * pinned across app-switches.
 *
 * Backed by the Rust hooks' `themia-blur` broadcast, which fires on the
 * WH_MOUSE_LL mouse hook (every physical click outside all themia windows) and
 * the foreground hook (keyboard focus changes like Alt+Tab, Win+D). A DOM
 * `window blur` listener is also installed as a dev/non-Tauri fallback: it is
 * unreliable on our WS_EX_NOACTIVATE windows (they don't cleanly re-acquire
 * focus after the first switch), so the Tauri event is the real signal.
 *
 * Returns a cleanup function. Call it from `onCleanup`.
 */

import { listen } from '@tauri-apps/api/event';

export function onAppBlur(handler: () => void): () => void {
  const onWinBlur = () => handler();
  window.addEventListener('blur', onWinBlur);

  let unlisten: (() => void) | undefined;
  listen('themia-blur', () => handler())
    .then(fn => { unlisten = fn; })
    .catch(() => {});

  return () => {
    window.removeEventListener('blur', onWinBlur);
    unlisten?.();
  };
}
