/**
 * Interactivity store.
 *
 * A shared flag used by Rust (via `set_interactive`) to decide whether the
 * widget window should accept focus-stealing mouse events. When any surface
 * needs the user to click, type, or drag (edit mode, open modal, focused
 * input, active context menu, ongoing drag...), that surface pushes an
 * "interactive reason" onto this store. The aggregate boolean drives the
 * Tauri bridge; the reason set is exposed for debugging.
 *
 * Replaces the ad-hoc OR chain in App.tsx:
 *   isInteractive = editMode() || showPicker() || showDesign() || ...
 *
 * Adding a new surface no longer requires editing App.tsx; just call
 * `useInteractive('my-surface', () => when)`.
 */

import { createSignal, createEffect, onCleanup, type Accessor } from 'solid-js';

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

const reasons: Map<symbol, string> = new Map();
const [count, setCount] = createSignal(0);

/** True when at least one reason is currently active. */
export const isInteractive: Accessor<boolean> = () => count() > 0;

/** Snapshot of currently-active reason labels (for the debug panel). */
export const activeReasons: Accessor<string[]> = () => [...reasons.values()];

// ---------------------------------------------------------------------------
// Push / pop
// ---------------------------------------------------------------------------

/**
 * Push a reason onto the store. Returns a cleanup function that removes it.
 * The same reason label can be pushed multiple times by different surfaces;
 * each push has a unique id so pops are independent.
 */
export function pushInteractive(reason: string): () => void {
  const id = Symbol(reason);
  reasons.set(id, reason);
  setCount(reasons.size);
  return () => {
    if (reasons.delete(id)) setCount(reasons.size);
  };
}

/**
 * Effect helper: push a reason while `when()` is truthy, pop when it turns
 * falsy or the owner disposes. Wire into a Solid component/owner.
 */
export function useInteractive(reason: string, when: () => unknown): void {
  createEffect(() => {
    if (!when()) return;
    const off = pushInteractive(reason);
    onCleanup(off);
  });
}
