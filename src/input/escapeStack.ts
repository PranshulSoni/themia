/**
 * Escape key stack.
 *
 * Centralises Escape handling so modals/modes opt in via push/pop instead of
 * threading their logic through a single if/else chain in App.tsx.
 *
 * Semantics:
 *   - Handlers stored LIFO. Most-recently pushed handler runs first.
 *   - Handler returns `true` when it handled the event → walk stops,
 *     preventDefault + stopPropagation applied.
 *   - Handler returns `false` → walk continues to the next handler.
 *   - Components that already call stopPropagation in their own key handler
 *     (e.g. ContextMenu panel with focus) preempt this stack naturally.
 */

import { onCleanup } from 'solid-js';
import { debugLog } from '../debugLog.ts';

export type EscapeHandler = () => boolean;

const stack: EscapeHandler[] = [];

// Fired when the stack transitions between empty and non-empty. The desktop
// windows use this to tell the native WH_KEYBOARD_LL hook to stand down on
// Escape while a handler is active (see set_escape_wanted), so Escape reaches
// the webview and the stack can run instead of being consumed by the hook.
let onActiveChange: ((active: boolean) => void) | null = null;
export function onEscapeStackActive(cb: (active: boolean) => void): void {
  onActiveChange = cb;
}

/**
 * Push an Escape handler. Returns a cleanup function that removes it.
 * Safe to call inside a createEffect; pair with onCleanup.
 */
export function pushEscape(handler: EscapeHandler): () => void {
  const wasEmpty = stack.length === 0;
  stack.push(handler);
  debugLog('escape', `push handler (depth=${stack.length})`);
  if (wasEmpty) onActiveChange?.(true);
  return () => {
    const idx = stack.lastIndexOf(handler);
    if (idx >= 0) {
      stack.splice(idx, 1);
      debugLog('escape', `pop handler (depth=${stack.length})`);
      if (stack.length === 0) onActiveChange?.(false);
    }
  };
}

/**
 * Convenience for use inside a createEffect: registers the handler and
 * wires its cleanup to the current Solid owner.
 */
export function useEscape(handler: EscapeHandler): void {
  const remove = pushEscape(handler);
  onCleanup(remove);
}

function onKeyDown(e: KeyboardEvent) {
  if (e.key !== 'Escape') return;
  const depth = stack.length;
  if (depth === 0) {
    debugLog('escape', 'esc: no handlers on stack');
    return;
  }
  for (let i = stack.length - 1; i >= 0; i--) {
    if (stack[i]()) {
      debugLog('escape', `esc handled by [${i}] (depth=${depth})`);
      e.preventDefault();
      e.stopPropagation();
      return;
    }
  }
  debugLog('escape', `esc unhandled: all ${depth} handler(s) returned false`);
}

let installed = false;

/** Install the global keydown listener. Idempotent. */
export function initEscapeStack(): void {
  if (installed) return;
  installed = true;
  window.addEventListener('keydown', onKeyDown);
}
