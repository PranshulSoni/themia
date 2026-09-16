// Browser-style back/forward history for the unified config window.
//
// The window's `selection` (a `ConfigTarget`) is the source of truth for what
// is displayed. This stack records the sequence of *user* navigations so the
// Back/Forward gestures (Alt+←/→, mouse side buttons) can step through them
// with the usual semantics: pushing a new target while standing on an older
// entry truncates the forward tail; consecutive duplicates are coalesced.
//
// Kept pure (no Solid, no DOM) so it can be unit-tested in isolation.

import type { ConfigTarget } from './target.ts';
import { encodeTarget } from './target.ts';

export interface NavHistory {
  /** Record a user navigation to `target`, dropping any forward entries. */
  push(target: ConfigTarget): void;
  /** Step back one entry; returns the now-current target, or null at the start. */
  back(): ConfigTarget | null;
  /** Step forward one entry; returns the now-current target, or null at the end. */
  forward(): ConfigTarget | null;
  canBack(): boolean;
  canForward(): boolean;
  /** The entry the cursor currently points at, or null when empty. */
  current(): ConfigTarget | null;
}

export function createNavHistory(): NavHistory {
  const stack: ConfigTarget[] = [];
  // Index of the entry currently displayed; -1 means empty.
  let idx = -1;

  return {
    push(target) {
      // Coalesce a repeat of the current entry: clicking the active node, or a
      // redirect that resolves to the same target, must not create a dupe.
      if (idx >= 0 && encodeTarget(stack[idx]) === encodeTarget(target)) return;
      // Standing on an older entry then navigating drops the forward tail.
      stack.splice(idx + 1);
      stack.push(target);
      idx = stack.length - 1;
    },
    back() {
      if (idx <= 0) return null;
      idx -= 1;
      return stack[idx];
    },
    forward() {
      if (idx >= stack.length - 1) return null;
      idx += 1;
      return stack[idx];
    },
    canBack() { return idx > 0; },
    canForward() { return idx < stack.length - 1; },
    current() { return idx >= 0 ? stack[idx] : null; },
  };
}
