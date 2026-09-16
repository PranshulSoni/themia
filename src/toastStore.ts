// ---------------------------------------------------------------------------
// Minimal app-wide toast store. One transient banner at a time.
// ---------------------------------------------------------------------------

import { createSignal } from 'solid-js';

export interface Toast {
  id: number;
  message: string;
  kind: 'info' | 'error';
}

const [current, setCurrent] = createSignal<Toast | null>(null);
export { current as toast };

let seq = 0;
let hideTimer: number | null = null;

export function showToast(message: string, kind: 'info' | 'error' = 'info', durationMs = 2500): void {
  if (hideTimer != null) {
    clearTimeout(hideTimer);
    hideTimer = null;
  }
  const id = ++seq;
  setCurrent({ id, message, kind });
  hideTimer = window.setTimeout(() => {
    if (current()?.id === id) setCurrent(null);
    hideTimer = null;
  }, durationMs);
}

export function dismissToast(): void {
  if (hideTimer != null) {
    clearTimeout(hideTimer);
    hideTimer = null;
  }
  setCurrent(null);
}
